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

    Client --> AuthAPI[POST /auth/**]
    Client --> AccAPI["GET, POST /accounts/**"]
    Client --> TrAPI["POST, GET /transfers"]
    Client --> NotifAPI[GET /notifications]
    Client --> AuditAPI[GET /audit]

    subgraph Auth
        AuthAPI --> AuthUseCase
        AuthUseCase --> AuthDB[(auth.*)]
    end

    subgraph Accounts
        AccAPI --> AccountsUseCase
        AccountsUseCase --> IAccountsService
        IAccountsService --> AccountsDB[(accounts.*)]
    end

    subgraph Transfers
        TrAPI --> TransferUseCase
        TransferUseCase -->|IAccountsService| IAccountsService
        TransferUseCase -->|INotificationsService| INotificationsService
        TransferUseCase -->|IAuditService| IAuditService
        TransferUseCase --> TransfersDB[(transfers.*)]
    end

    subgraph Notifications
        NotifAPI --> INotificationsService
        INotificationsService --> NotifDB[(notifications.*)]
    end

    subgraph Audit
        AuditAPI --> IAuditService
        IAuditService --> AuditDB[(audit.*)]
    end
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
