# modular-bank-dotnet

Monolito modular bancario implementado en .NET 10 / ASP.NET Core. Referencia técnica paralela a `modular-bank-java`.

## Requisitos
- .NET 10 SDK
- Docker

## Ejecutar

```bash
docker-compose up -d
dotnet run --project src/ModularBank/
```

Las migraciones de EF Core se aplican automáticamente al iniciar la aplicación.

## Módulos

| Módulo | Schema | Interfaz pública |
|---|---|---|
| Auth | auth.* | — (solo JWT) |
| Accounts | accounts.* | IAccountsService |
| Transfers | transfers.* | — (orchestrador) |
| Notifications | notifications.* | INotificationsService |
| Audit | audit.* | IAuditService |

## Arquitectura

### Dependencias entre módulos

```mermaid
graph TD
    Client([Cliente HTTP])

    Client -->|HTTP + W3C traceparent| Gateway["API Gateway / YARP<br/>Puerto 8080"]

    Gateway -->|POST /auth/**| AuthAPI
    Gateway -->|GET, POST /accounts/**| AccAPI
    Gateway -->|GET /audit| AuditAPI
    Gateway -->|POST, GET /transfers| TrAPI
    Gateway -->|GET /notifications| NotifAPI

    %% =====================================================
    %% MONOLITO REMANENTE
    %% =====================================================

    subgraph MONOLITH["Monolito remanente .NET"]
        direction TB

        subgraph AUTH["Módulo Auth"]
            AuthAPI["POST /auth/**"]
            AuthUseCase["Auth Use Cases"]
            AuthAPI --> AuthUseCase
        end

        subgraph ACCOUNTS["Módulo Accounts"]
            AccAPI["GET, POST /accounts/**"]
            AccountsConsumer["AccountsTransferRequestedConsumer<br/>Consume TransferRequested.v1"]
            AccountsUseCase["Accounts Use Cases<br/>Débito / Crédito / Validación"]
            AccountsOutbox["Transactional Outbox<br/>Accounts"]

            AccAPI --> AccountsUseCase
            AccountsConsumer --> AccountsUseCase
            AccountsUseCase --> AccountsOutbox
        end

        subgraph AUDIT["Módulo Audit"]
            AuditAPI["GET /audit"]
            AuditConsumer["AuditTransferResultConsumer<br/>Consume Completed / Failed"]
            AuditUseCase["Audit Use Cases"]

            AuditAPI --> AuditUseCase
            AuditConsumer --> AuditUseCase
        end

        MonolithDB[("PostgreSQL Monolito<br/>auth.*<br/>accounts.*<br/>audit.*<br/>Inbox / Outbox")]

        AuthUseCase --> MonolithDB
        AccountsUseCase --> MonolithDB
        AuditUseCase --> MonolithDB
    end

    %% =====================================================
    %% TRANSFERS SERVICE
    %% =====================================================

    subgraph TRANSFERS["Transfers Service — Microservicio MS2"]
        direction TB

        TrAPI["POST, GET /transfers"]
        TransferUseCase["Transfer Use Case<br/>Saga por coreografía"]
        TransferResultConsumer["AccountTransferResultConsumer<br/>Consume Applied / Rejected"]
        TransfersOutbox["Transactional Outbox<br/>Transfers"]
        TransfersDB[("PostgreSQL exclusivo<br/>transfers_db<br/>transfers.*<br/>Outbox / Inbox")]

        TrAPI --> TransferUseCase
        TransferResultConsumer --> TransferUseCase
        TransferUseCase --> TransfersDB
        TransferUseCase --> TransfersOutbox
    end

    %% =====================================================
    %% NOTIFICATIONS SERVICE
    %% =====================================================

    subgraph NOTIFICATIONS["Notifications Service — Microservicio MS1"]
        direction TB

        NotifAPI["GET /notifications"]
        NotificationQuery["Consulta de notificaciones"]
        NotificationConsumer["TransferCompletedConsumer<br/>Consume TransferCompleted.v1"]
        NotificationUseCase["Notification Use Case"]
        NotificationsDB[("PostgreSQL exclusivo<br/>notifications_db<br/>notifications.*<br/>Inbox / Idempotencia")]

        NotifAPI --> NotificationQuery
        NotificationQuery --> NotificationsDB
        NotificationConsumer --> NotificationUseCase
        NotificationUseCase --> NotificationsDB
    end

    %% =====================================================
    %% EVENTOS Y RESILIENCIA
    %% =====================================================

    subgraph EVENTING["Mensajería asíncrona y resiliencia"]
        direction TB

        Rabbit[["RabbitMQ<br/>Exchange finbank.events<br/>CloudEvents 1.0<br/>Publisher Confirms + ACK manual"]]

        Retry["Colas de Retry por consumidor<br/>5 s → 20 s → 80 s"]
        DLQ["DLQ independiente<br/>por consumidor"]

        Rabbit -. Error transitorio .-> Retry
        Retry -. TTL y reintento .-> Rabbit
        Rabbit -. Error permanente<br/>o reintentos agotados .-> DLQ
    end

    TransfersOutbox -->|TransferRequested.v1| Rabbit
    Rabbit -->|TransferRequested.v1| AccountsConsumer

    AccountsOutbox -->|AccountTransferApplied.v1<br/>AccountTransferRejected.v1| Rabbit
    Rabbit -->|Applied / Rejected| TransferResultConsumer

    TransfersOutbox -->|TransferCompleted.v1<br/>TransferFailed.v1| Rabbit

    Rabbit -->|TransferCompleted.v1| NotificationConsumer
    Rabbit -->|TransferCompleted.v1<br/>TransferFailed.v1| AuditConsumer

    %% =====================================================
    %% OBSERVABILIDAD
    %% =====================================================

    subgraph OBSERVABILITY["Observabilidad y trazabilidad distribuida"]
        direction TB

        OTel["OpenTelemetry Collector<br/>OTLP"]

        Tempo["Grafana Tempo<br/>Trazas distribuidas"]
        Prometheus["Prometheus<br/>Métricas HTTP, negocio y RabbitMQ<br/>Reglas de alerta"]
        Loki["Grafana Loki<br/>Logs estructurados por TraceId"]
        Grafana["Grafana<br/>Dashboards y Explore"]

        OTel -->|Trazas| Tempo
        OTel -->|Métricas| Prometheus
        OTel -->|Logs| Loki

        Tempo --> Grafana
        Prometheus --> Grafana
        Loki --> Grafana
    end

    Gateway -. OTLP .-> OTel

    AuthAPI -. OTLP .-> OTel
    AccAPI -. OTLP .-> OTel
    AccountsConsumer -. OTLP .-> OTel
    AuditAPI -. OTLP .-> OTel
    AuditConsumer -. OTLP .-> OTel

    TrAPI -. OTLP .-> OTel
    TransferResultConsumer -. OTLP .-> OTel
    TransfersOutbox -. Producer Span .-> OTel

    NotifAPI -. OTLP .-> OTel
    NotificationConsumer -. OTLP .-> OTel

    Rabbit -. "Métricas :15692" .-> Prometheus
```

### Capas internas de cada módulo

```mermaid
graph LR
    subgraph módulo
        API["Api/\n(Endpoint)"]
        APP["Application/\n(UseCase + Interface)"]
        INFRA["Infrastructure/\n(Service + DbContext)"]
        DOMAIN["Domain/\n(Entity)"]
    end

    API --> APP
    APP --> DOMAIN
    INFRA --> APP
    INFRA --> DOMAIN

    subgraph "otros módulos"
        EXT["Application/\n(Interface pública)"]
    end

    APP -.->|"solo a través\nde interfaces"| EXT
```

### Aislamiento de schemas en PostgreSQL

```mermaid
graph TD
    subgraph PostgreSQL
        subgraph auth
            users[(users)]
            refresh_tokens[(refresh_tokens)]
        end
        subgraph accounts
            accounts_t[(accounts)]
        end
        subgraph transfers
            transfers_t[(transfers)]
        end
        subgraph notifications
            notifications_t[(notifications)]
        end
        subgraph audit
            audit_entries[(audit_entries)]
        end
    end
```
