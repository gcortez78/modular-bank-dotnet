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

## Aplicar migraciones (primera vez)

```bash
dotnet ef database update --context AuthDbContext --project src/ModularBank/
dotnet ef database update --context AccountsDbContext --project src/ModularBank/
dotnet ef database update --context TransfersDbContext --project src/ModularBank/
dotnet ef database update --context NotificationsDbContext --project src/ModularBank/
dotnet ef database update --context AuditDbContext --project src/ModularBank/
```

## Módulos

| Módulo | Schema | Interfaz pública |
|---|---|---|
| Auth | auth.* | — (solo JWT) |
| Accounts | accounts.* | IAccountsService |
| Transfers | transfers.* | — (orchestrador) |
| Notifications | notifications.* | INotificationsService |
| Audit | audit.* | IAuditService |

## Arquitectura

Cada módulo tiene su propio `DbContext` apuntando a su schema.
Los módulos se comunican únicamente a través de interfaces en `Application/`.
Ningún módulo referencia el `DbContext` de otro módulo.

## Migración a microservicios

Ver `README-migration.md` en cada módulo. Orden recomendado:
1. Notifications
2. Audit
3. Auth
4. Accounts
5. Transfers
