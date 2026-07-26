# Guía 03 — ADR-002: extracción de Transfers y comunicación asíncrona con Notifications

## 1. Objetivo

Extraer el módulo `Transfers` del monolito FinBank como un microservicio independiente, con:

- contenedor propio;
- base PostgreSQL exclusiva `transfers_db`;
- esquema `transfers`;
- ruta `/transfers/*` en YARP;
- patrón Transactional Outbox;
- publicación de `TransferCompletedIntegrationEvent`;
- RabbitMQ como broker;
- consumo idempotente en `Notifications Service`;
- DLQ para eventos inválidos.

La interacción que antes era una llamada directa desde `TransferUseCase` a
`INotificationsService` pasa a ser:

```text
Transfers Service
    └─ guarda transferencia + Outbox en transfers_db
          └─ publica transfers.completed.v1
                └─ RabbitMQ
                      └─ Notifications Service
                            └─ guarda notificación en notifications_db
```

## 2. Alcance y límite arquitectónico

En este paso se extrae únicamente `Transfers`.

`Accounts` continúa en el monolito porque conserva la propiedad de los saldos.
Transfers utiliza una API interna autenticada para aplicar débito y crédito
atómicamente dentro de la base del monolito.

La comunicación que debe quedar asíncrona es la relación:

```text
Transfers → Notifications
```

No se aplica consistencia eventual a los saldos de Accounts.

## 3. Archivos incorporados

```text
src/TransfersService/
src/NotificationsService/Messaging/
src/ModularBank/Modules/Accounts/Api/InternalTransfersEndpoints.cs
src/ModularBank/Modules/Accounts/Domain/ProcessedTransferCommand.cs
src/ModularBank/Modules/Accounts/Migrations/202607250001_AddProcessedTransferCommands.cs
compose.yaml
src/Gateway/appsettings.json
scripts/windows/10-levantar-adr002.ps1
scripts/windows/11-validar-adr002.ps1
scripts/windows/12-evidencia-adr002.ps1
docs/adr/ADR-002-EXTRAER-TRANSFERS-CON-RABBITMQ.md
```

## 4. Preparar una rama

Desde la raíz del repositorio:

```powershell
git status
git checkout -b feature/adr002-extract-transfers
```

Si ya existe:

```powershell
git checkout feature/adr002-extract-transfers
```

## 5. Instalar el kit

Descomprimir el ZIP, por ejemplo en:

```text
C:\FinBank\KIT\ADR002_TRANSFERS_RABBITMQ_KIT
```

Ejecutar:

```powershell
Set-ExecutionPolicy -Scope Process Bypass

C:\FinBank\KIT\ADR002_TRANSFERS_RABBITMQ_KIT\INSTALAR_ADR002.ps1 `
  -RepoPath C:\FinBank\PLATAFORMA_BASE_FINBANK
```

El instalador:

1. verifica el repositorio;
2. crea respaldo en `.adr002-backup`;
3. copia el nuevo microservicio;
4. modifica Notifications, Monolith, Gateway y Compose;
5. agrega `TRANSFERS_INTERNAL_API_KEY`;
6. excluye el respaldo mediante `.gitignore`.

## 6. Revisar configuración

```powershell
Set-Location C:\FinBank\PLATAFORMA_BASE_FINBANK

docker compose config
```

Comprobar:

```powershell
Select-String -Path .env -Pattern `
  "TRANSFERS_DB",
  "TRANSFERS_INTERNAL_API_KEY",
  "RABBITMQ"
```

La clave interna debe ser igual en:

```text
monolith:
  Transfers__InternalApiKey

transfers-service:
  Monolith__InternalApiKey
```

## 7. Construir las imágenes

```powershell
docker compose build --no-cache `
  notifications-service `
  monolith `
  transfers-service `
  gateway
```

Si aparece un error de compilación:

```powershell
docker compose build transfers-service --progress=plain
docker compose build notifications-service --progress=plain
docker compose build monolith --progress=plain
```

## 8. Levantar la plataforma

```powershell
.\scripts\windows\10-levantar-adr002.ps1
```

Orden:

1. PostgreSQL Monolith.
2. PostgreSQL Notifications.
3. PostgreSQL Transfers.
4. RabbitMQ.
5. Notifications Service.
6. Monolito.
7. Transfers Service.
8. Gateway.

Validar:

```powershell
docker compose ps
```

Todos deben estar `healthy`.

## 9. Confirmar Database-per-Service

```powershell
'\dt transfers.*' |
docker compose exec -T postgres-transfers `
  sh -lc 'psql -U "$POSTGRES_USER" -d "${POSTGRES_DB:-$POSTGRES_USER}"'
```

Resultado esperado:

```text
transfers | __EFMigrationsHistory
transfers | outbox_messages
transfers | transfers
```

Confirmar que el monolito no puede resolver el nombre del PostgreSQL de Transfers
por no pertenecer a la red `data-transfers`:

```powershell
docker compose exec -T monolith `
  getent hosts postgres-transfers
```

No debería devolver una dirección.

## 10. Confirmar YARP

```powershell
Get-Content .\src\Gateway\appsettings.json
```

Rutas esperadas:

```text
/transfers/*     → transfers-service
/notifications/* → notifications-service
resto            → monolith
```

Después de una llamada:

```powershell
docker compose logs --since=1m gateway transfers-service monolith
```

Debe aparecer:

```text
Proxying to http://transfers-service:8080/transfers
```

y no una llamada `/transfers` en el monolito.

## 11. Confirmar RabbitMQ

Abrir:

```text
http://localhost:15672
```

Usar las credenciales de `.env`.

Elementos esperados:

```text
Exchange: finbank.events
Routing key: transfers.completed.v1
Queue: notifications.transfer-completed.v1
DLX: finbank.events.dlx
DLQ: notifications.transfer-completed.v1.dlq
```

También se puede validar por terminal:

```powershell
docker compose exec -T rabbitmq `
  rabbitmqctl list_exchanges name type durable

docker compose exec -T rabbitmq `
  rabbitmqctl list_queues name messages_ready messages_unacknowledged durable
```

## 12. Ejecutar la validación automática

```powershell
.\scripts\windows\11-validar-adr002.ps1
```

La prueba realiza:

1. registro de usuario;
2. creación de dos cuentas;
3. asignación de saldo de laboratorio;
4. detención de Notifications;
5. creación de transferencia por Gateway;
6. persistencia en `transfers_db`;
7. almacenamiento y publicación del evento;
8. reinicio de Notifications;
9. consumo del evento;
10. creación idempotente de la notificación.

Resultado esperado:

```text
ADR-002 validado satisfactoriamente.
La transferencia se completó con Notifications detenido.
RabbitMQ conservó el evento y Notifications lo procesó al recuperarse.
```

> La actualización directa del saldo se utiliza únicamente para preparar los
> datos de prueba del laboratorio. No forma parte del flujo de negocio.

## 13. Evidencia manual de resiliencia

### 13.1 Detener Notifications

```powershell
docker compose stop notifications-service
```

### 13.2 Crear transferencia

Usando un JWT válido:

```powershell
$body = @{
  sourceAccountId = $sourceAccountId
  targetAccountId = $targetAccountId
  amount = 25.50
  reference = "Prueba asíncrona"
} | ConvertTo-Json

Invoke-RestMethod `
  -Uri "http://localhost:8080/transfers" `
  -Method Post `
  -Headers @{ Authorization = "Bearer $token" } `
  -ContentType "application/json" `
  -Body $body
```

La transferencia debe responder como completada porque no espera a
Notifications.

### 13.3 Verificar transferencia y Outbox

```powershell
'SELECT id, status, amount, completed_at
 FROM transfers.transfers
 ORDER BY created_at DESC
 LIMIT 5;

 SELECT id, event_type, routing_key, processed_at, attempts
 FROM transfers.outbox_messages
 ORDER BY occurred_at DESC
 LIMIT 5;' |
docker compose exec -T postgres-transfers `
  sh -lc 'psql -U "$POSTGRES_USER" -d "${POSTGRES_DB:-$POSTGRES_USER}"'
```

### 13.4 Verificar mensaje durable

```powershell
docker compose exec -T rabbitmq `
  rabbitmqctl list_queues name messages_ready messages_unacknowledged
```

Con Notifications detenido, la cola debe conservar el mensaje.

### 13.5 Recuperar Notifications

```powershell
docker compose start notifications-service
```

Revisar:

```powershell
docker compose logs -f notifications-service
```

Debe aparecer:

```text
Evento ... consumido; notificación creada para transferencia ...
```

## 14. Cómo funciona el Outbox

La transferencia se marca como `Completed` y el evento se guarda en
`transfers.outbox_messages` dentro de una misma transacción local.

```text
BEGIN
  UPDATE transfers SET status = Completed
  INSERT outbox_messages
COMMIT
```

Un `BackgroundService` consulta mensajes pendientes y los publica en RabbitMQ
con mensajes persistentes y publisher confirms.

Si el proceso se detiene después de publicar pero antes de marcar el Outbox,
el evento puede publicarse nuevamente. Esto se resuelve en Notifications con:

```text
idempotency_key = transfer-completed:{EventId}
```

y un índice único.

## 15. Idempotencia del movimiento contable

Transfers llama a:

```text
POST /internal/transfers/accounts/apply
```

El comando incluye `TransferId`.

Accounts registra ese identificador en:

```text
accounts.processed_transfer_commands
```

Si Transfers repite la llamada por un timeout, Accounts devuelve éxito sin
volver a debitar ni acreditar.

## 16. Logs clave

```powershell
docker compose logs --since=10m `
  gateway `
  transfers-service `
  notifications-service `
  monolith `
  rabbitmq
```

Buscar:

```text
Gateway:
Proxying to http://transfers-service:8080/transfers

Transfers:
Transferencia ... completada; evento ... almacenado en Outbox
Evento ... publicado con routing key transfers.completed.v1

Notifications:
Consumidor conectado a notifications.transfer-completed.v1
Evento ... consumido; notificación creada
```

## 17. Generar evidencias

```powershell
.\scripts\windows\12-evidencia-adr002.ps1
```

Archivos:

```text
docs/evidencias/adr002/<fecha>/
```

Incluyen:

- estado de contenedores;
- logs integrados;
- migraciones y conteos;
- colas de RabbitMQ;
- Compose resuelto.

## 18. Criterios de aceptación

- `Transfers Service` es un contenedor independiente.
- Usa exclusivamente `transfers_db`.
- `/transfers/*` es atendido por Transfers Service.
- El monolito ya no publica endpoints externos de Transfers.
- No existe llamada de Transfers a `INotificationsService`.
- Se publica `TransferCompletedIntegrationEvent`.
- RabbitMQ mantiene una cola durable.
- Notifications consume de forma idempotente.
- La transferencia se completa aunque Notifications esté detenido.
- La notificación aparece al recuperarse Notifications.
- La repetición del comando contable no duplica el movimiento.
- Existe DLQ para mensajes inválidos.

## 19. Guardar en GitHub

Antes del commit:

```powershell
git status --short
git diff --stat
```

No agregar:

```text
.env
.adr002-backup/
volúmenes o respaldos SQL con datos
```

Registrar:

```powershell
git add .
git commit -m "feat: extract transfers service with RabbitMQ outbox"
git push -u origin feature/adr002-extract-transfers
```

## 20. Rollback

Detener la plataforma:

```powershell
docker compose down
```

Restaurar los archivos desde:

```text
.adr002-backup\<fecha>\
```

El código legacy de Transfers permanece en el repositorio, pero deja de
registrarse y mapearse en `Program.cs`. Esto permite un rollback controlado
restaurando la configuración anterior del monolito y del Gateway.
