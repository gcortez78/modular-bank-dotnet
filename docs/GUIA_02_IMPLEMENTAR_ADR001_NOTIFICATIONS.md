# Guía 02 — Implementar ADR-001: extracción de Notifications en FinBank

## 1. Objetivo

Dar continuidad a `PLATAFORMA_BASE_FINBANK` y aplicar el primer corte del patrón
Strangler Fig:

```text
Cliente
   |
   v
YARP Gateway :8080
   |-- /notifications* ---> Notifications Service ---> PostgreSQL Notifications
   |
   `-- resto de rutas ----> Monolito remanente -----> PostgreSQL Monolito
```

El resultado incorpora:

- Un microservicio `.NET 10` independiente para Notifications.
- Una base PostgreSQL exclusiva.
- Enrutamiento específico en YARP.
- Comunicación HTTP interna desde el monolito.
- Contenedores y health checks.
- Validación automática de Database-per-Service.
- Conservación temporal del código y schema legacy para rollback.

RabbitMQ permanece activo, pero en esta etapa no transporta eventos. La
comunicación asíncrona con Outbox debe formalizarse en otro ADR.

---

## 2. Punto de partida requerido

Antes de aplicar el kit, la guía base debe haber quedado validada:

- `finbank-gateway`, `finbank-monolith`, `finbank-postgres-monolith` y
  `finbank-rabbitmq` saludables.
- `http://localhost:8080/health` operativo.
- `/auth/register` funcionando por el Gateway.
- Rama `feature/plataforma-base-finbank` disponible.
- Archivo `.env` local creado.

Desde PowerShell:

```powershell
Set-Location C:\FinBank\PLATAFORMA_BASE_FINBANK
docker compose ps
git status
```

---

## 3. Crear una rama para ADR-001

No implementar la extracción directamente sobre la rama de plataforma base:

```powershell
Set-Location C:\FinBank\PLATAFORMA_BASE_FINBANK
git checkout feature/plataforma-base-finbank
git pull
git checkout -b feature/adr001-extract-notifications
```

Confirmar:

```powershell
git branch --show-current
```

Resultado esperado:

```text
feature/adr001-extract-notifications
```

---

## 4. Detener la plataforma sin borrar datos

```powershell
docker compose down
```

No usar `--volumes`; el volumen del monolito se conserva como respaldo del
histórico legacy.

---

## 5. Descomprimir el kit

Suponiendo que el ZIP está en Descargas:

```powershell
New-Item -ItemType Directory -Force C:\FinBank\ADR001_KIT | Out-Null

Expand-Archive `
  -Path "$HOME\Downloads\FINBANK_ADR001_NOTIFICATIONS_KIT.zip" `
  -DestinationPath C:\FinBank\ADR001_KIT `
  -Force
```

---

## 6. Instalar los archivos

Habilitar scripts únicamente para la terminal actual:

```powershell
Set-ExecutionPolicy -Scope Process Bypass
```

Ejecutar:

```powershell
C:\FinBank\ADR001_KIT\FINBANK_ADR001_NOTIFICATIONS_KIT\INSTALAR_ADR001.ps1 `
  -RepoPath C:\FinBank\PLATAFORMA_BASE_FINBANK
```

El instalador:

1. Verifica la estructura base.
2. Crea respaldo en `.adr001-backup`.
3. Copia el microservicio y los archivos modificados.
4. Actualiza `.env` sin reemplazar los valores existentes.
5. Guarda la ruta del respaldo en `.adr001-last-backup`.

---

## 7. Revisar la nueva estructura

```powershell
Set-Location C:\FinBank\PLATAFORMA_BASE_FINBANK

Get-ChildItem .\src\NotificationsService -Recurse
Get-ChildItem .\scripts\windows\0*-adr001.ps1
git status
```

Elementos principales:

```text
src/
├─ Gateway/
├─ ModularBank/
└─ NotificationsService/
   ├─ Api/
   ├─ Application/
   ├─ Domain/
   ├─ Infrastructure/
   ├─ Migrations/
   ├─ Dockerfile
   ├─ NotificationsService.csproj
   └─ Program.cs
```

---

## 8. Revisar variables locales

Abrir:

```powershell
notepad .env
```

Confirmar que existan:

```dotenv
NOTIFICATIONS_DB=notifications_db
NOTIFICATIONS_DB_USER=notifications
NOTIFICATIONS_DB_PASSWORD=notifications-local
NOTIFICATIONS_DB_PORT=5434
NOTIFICATIONS_INTERNAL_API_KEY=finbank-notifications-internal-key-change-this-2026
```

Para un laboratorio pueden usarse estos valores. No subir `.env` a GitHub.

---

## 9. Validar Docker Compose

```powershell
docker compose config --quiet
docker compose config --services
```

Servicios esperados:

```text
postgres-monolith
postgres-notifications
rabbitmq
notifications-service
monolith
gateway
```

`postgres-transfers` puede aparecer asociado al perfil `future`.

---

## 10. Construir la arquitectura ADR-001

```powershell
Set-ExecutionPolicy -Scope Process Bypass
.\scripts\windows\07-construir-adr001.ps1
```

El orden es:

1. `notifications-service`.
2. `monolith`, porque ahora usa un cliente HTTP.
3. `gateway`, porque cambió la configuración de rutas.

Construcción manual equivalente:

```powershell
docker compose build notifications-service
docker compose build monolith
docker compose build gateway
```

---

## 11. Levantar los servicios en orden

```powershell
.\scripts\windows\08-levantar-adr001.ps1
```

Orden esperado:

1. PostgreSQL del monolito.
2. PostgreSQL de Notifications.
3. RabbitMQ.
4. Notifications Service.
5. Monolito remanente.
6. Gateway.

Comprobar:

```powershell
docker compose ps
```

Todos deben estar `healthy`.

---

## 12. Validar automáticamente

```powershell
.\scripts\windows\09-validar-adr001.ps1
```

La validación comprueba:

1. Salud de los seis servicios.
2. Registro de usuario por `Gateway -> Monolito`.
3. Consulta autenticada por `Gateway -> Notifications Service`.
4. Creación de una notificación mediante la API interna.
5. Persistencia en `postgres-notifications`.
6. Ausencia de la nueva notificación en la base legacy del monolito.

Resultado esperado:

```text
ADR-001 validado satisfactoriamente.
```

---

## 13. Pruebas manuales esenciales

### 13.1 Gateway

```powershell
Invoke-RestMethod http://localhost:8080/health
```

### 13.2 Health interno del microservicio

El microservicio no se publica directamente al host:

```powershell
docker compose exec -T notifications-service `
  curl -fsS http://localhost:8080/health
```

### 13.3 Confirmar tabla exclusiva

```powershell
$dbUser = (docker compose exec -T postgres-notifications `
  printenv POSTGRES_USER).Trim()

$dbName = (docker compose exec -T postgres-notifications `
  printenv POSTGRES_DB).Trim()

docker compose exec -T postgres-notifications `
  psql -U $dbUser -d $dbName -c `
  "\dt notifications.*"
```

### 13.4 Confirmar rutas de YARP

```powershell
Get-Content .\src\Gateway\appsettings.json
```

Debe existir un cluster `notifications` antes del catch-all del monolito.

---

## 14. Prueba de aislamiento de fallas

Detener únicamente Notifications:

```powershell
docker compose stop notifications-service
```

Comprobar que el Gateway y el monolito siguen activos:

```powershell
Invoke-RestMethod http://localhost:8080/health
docker compose ps
```

La ruta `/notifications` fallará temporalmente, pero `/auth/*`,
`/accounts/*`, `/transfers/*` y `/audit/*` continúan dirigidas al monolito.

Volver a iniciar:

```powershell
docker compose start notifications-service
```

Esperar salud:

```powershell
docker compose ps notifications-service
```

---

## 15. Qué cambia dentro del monolito

El módulo legacy no se elimina físicamente en este corte. Se aplican tres cambios:

1. `Program.cs` deja de registrar `NotificationsDbContext`.
2. `Program.cs` deja de publicar `/notifications`.
3. `INotificationsService` se resuelve mediante `NotificationsHttpClient`.

Esta convivencia reduce el riesgo de rollback. La limpieza definitiva del código
legacy debe hacerse después del periodo de estabilización.

---

## 16. Estrategia de datos

### Nuevos datos

Toda notificación creada después del corte se guarda únicamente en:

```text
finbank-postgres-notifications
  database: notifications_db
  schema: notifications
  table: notifications
```

### Datos históricos

El schema `notifications` de `postgres-monolith` permanece como histórico
legacy. No recibe nuevas escrituras.

No se incluye migración masiva automática porque:

- Reduce el riesgo del primer corte.
- Evita bloquear el despliegue por volumen histórico.
- Permite definir primero reglas de retención y validación.
- Facilita rollback.

---

## 17. Seguridad aplicada

- El cliente externo solo accede a `/notifications` mediante JWT.
- El endpoint de escritura interno no pasa por YARP.
- El monolito usa `X-Internal-Api-Key`.
- El microservicio y el monolito se comunican en la red Docker `backend`.
- La base exclusiva solo comparte la red `data-notifications` con su servicio.

Deuda pendiente: los JWT actuales no validan issuer ni audience para conservar
compatibilidad con la plataforma base. Debe resolverse en un ADR de seguridad.

---

## 18. Idempotencia

El monolito envía una clave determinística:

```text
transfer-sent:<transferId>
```

El microservicio almacena la clave y aplica un índice único. Si el mismo mensaje
se envía nuevamente, devuelve la notificación existente en lugar de duplicarla.

---

## 19. Trade-off aceptado en esta fase

La llamada `Transfers -> Notifications` continúa siendo HTTP sincrónica.

Para evitar que una capacidad secundaria invalide una transferencia:

- `TransferUseCase` captura el error.
- La transferencia continúa confirmada.
- El fallo se registra como warning.

Consecuencia negativa: sin Outbox ni reintentos durables, una notificación puede
perderse si el servicio está caído. La evolución recomendada es:

```text
Transfer local transaction
   -> Outbox
   -> Publisher
   -> RabbitMQ
   -> Notifications consumer
   -> Retry / DLQ
```

---

## 20. Diagnóstico

### Notifications Service unhealthy

```powershell
docker compose logs --tail 200 notifications-service
docker compose logs --tail 100 postgres-notifications
```

Revisar:

- `NOTIFICATIONS_INTERNAL_API_KEY` con al menos 32 caracteres.
- Conexión a `postgres-notifications`.
- Migración EF Core inicial.
- Acceso a NuGet durante el build.

### Gateway devuelve 502 en `/notifications`

```powershell
docker compose ps notifications-service gateway
docker compose logs --tail 100 gateway
docker compose logs --tail 100 notifications-service
```

La dirección interna correcta es:

```text
http://notifications-service:8080/
```

No usar `localhost` en YARP.

### Auth funciona, pero Notifications devuelve 401

Confirmar que monolito y microservicio usan el mismo:

```dotenv
JWT_SECRET=...
```

### Puerto 5434 ocupado

Modificar solamente el puerto local:

```dotenv
NOTIFICATIONS_DB_PORT=5444
```

El puerto interno permanece `5432`.

---

## 21. Rollback

Para volver a los archivos anteriores:

```powershell
docker compose down

C:\FinBank\ADR001_KIT\FINBANK_ADR001_NOTIFICATIONS_KIT\RESTAURAR_ADR001.ps1 `
  -RepoPath C:\FinBank\PLATAFORMA_BASE_FINBANK
```

Después:

```powershell
docker compose build monolith gateway
.\scripts\windows\04-levantar-plataforma.ps1
```

El volumen `postgres_notifications_data` puede conservarse para análisis. Para
borrarlo, identificarlo primero con:

```powershell
docker volume ls --filter name=postgres_notifications
```

---

## 22. Guardar el avance

```powershell
git status
git diff --stat
git add .
git commit -m "feat: extract notifications service using strangler fig"
git push -u origin feature/adr001-extract-notifications
```

Confirmar que `.env`, `.adr001-backup` y `.adr001-last-backup` no se incluyan.
Agregar estas entradas a `.gitignore` si fuera necesario:

```gitignore
.adr001-backup/
.adr001-last-backup
```

---

## 23. Criterios de aceptación

ADR-001 queda implementado cuando:

- `notifications-service` está saludable.
- `postgres-notifications` está saludable.
- `/notifications` llega al microservicio mediante YARP.
- El resto de rutas continúa llegando al monolito.
- Las nuevas notificaciones solo aparecen en la base exclusiva.
- El monolito no registra ni migra `NotificationsDbContext`.
- Transfers consume `INotificationsService` mediante HTTP.
- Una caída de Notifications no impide el uso del resto de la plataforma.
- El script `09-validar-adr001.ps1` termina satisfactoriamente.
- Los cambios están versionados en una rama separada.

---

## 24. Archivos principales modificados

| Archivo | Finalidad |
|---|---|
| `compose.yaml` | Agrega microservicio y activa DB exclusiva |
| `.env.example` | Agrega clave interna |
| `src/Gateway/appsettings.json` | Enruta `/notifications` |
| `src/NotificationsService/*` | Nuevo servicio independiente |
| `src/ModularBank/Program.cs` | Retira endpoint y DbContext legacy |
| `NotificationsHttpClient.cs` | Adaptador HTTP del monolito |
| `TransferUseCase.cs` | Idempotencia y tolerancia a falla |
| `scripts/windows/07-*` | Build |
| `scripts/windows/08-*` | Inicio secuencial |
| `scripts/windows/09-*` | Validación |
