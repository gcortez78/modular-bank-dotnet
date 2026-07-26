# Guía 01 — Levantar PLATAFORMA_BASE_FINBANK en Windows 11

## 1. Objetivo de esta primera parte

El objetivo es preparar un entorno local reproducible en un equipo de escritorio con Windows 11 y dejar funcionando la plataforma base del reto FinBank.

Al finalizar deberán estar activos y saludables:

| Servicio | Contenedor | Puerto local | Función |
|---|---|---:|---|
| API Gateway | `finbank-gateway` | 8080 | Punto único de entrada HTTP mediante YARP. |
| Monolito modular | `finbank-monolith` | Interno | Aplicación .NET 10 con Auth, Accounts, Transfers, Notifications y Audit. |
| PostgreSQL | `finbank-postgres-monolith` | 5433 | Base de datos actual del monolito y sus cinco schemas. |
| RabbitMQ | `finbank-rabbitmq` | 5672 | Broker preparado para la extracción futura de módulos. |
| RabbitMQ Management | `finbank-rabbitmq` | 15672 | Consola web del broker. |
| RabbitMQ Metrics | `finbank-rabbitmq` | 15692 | Endpoint Prometheus del broker. |

En esta etapa no se extraen microservicios, no se programan eventos y no se implementa observabilidad completa. Solo se construye la plataforma base sobre la cual se desarrollarán las siguientes fases.

---

## 2. Decisión importante: dónde instalar Git

**Git se instala en Windows 11, no dentro de un contenedor.**

Git debe administrar la carpeta local del proyecto, las ramas, commits y el envío del código a GitHub. Si Git estuviera dentro de un contenedor, el trabajo diario sería innecesariamente complejo y dependería de montar volúmenes para cada operación.

Por tanto:

- Git for Windows se instala en el sistema operativo anfitrión.
- Docker Desktop y Docker Compose también se ejecutan desde Windows.
- Los componentes de FinBank se ejecutan como contenedores Linux.
- YARP no tiene una imagen Docker separada: se descarga como paquete NuGet durante la construcción del Gateway.

---

## 3. Imágenes que se descargarán una por una

Se descargarán cuatro imágenes externas:

| Orden | Imagen | Uso |
|---:|---|---|
| 1 | `postgres:16-alpine` | PostgreSQL del monolito y futuras bases exclusivas. |
| 2 | `rabbitmq:4.3.2-management-alpine` | Broker, consola web y soporte de métricas. |
| 3 | `mcr.microsoft.com/dotnet/sdk:10.0` | Compilar y publicar el monolito y el Gateway. |
| 4 | `mcr.microsoft.com/dotnet/aspnet:10.0` | Ejecutar las aplicaciones .NET. |

Después se construirán localmente otras dos imágenes:

| Imagen local | Se construye desde |
|---|---|
| `plataforma-base-finbank-monolith:1.0` | `src/ModularBank/Dockerfile` |
| `plataforma-base-finbank-gateway:1.0` | `src/Gateway/Dockerfile` |

Las bases futuras de Notifications y Transfers reutilizarán `postgres:16-alpine`; no requieren otra descarga.

---

## 4. Requisitos recomendados del equipo

Para trabajar cómodamente con el build de .NET, PostgreSQL y RabbitMQ se recomienda:

- Windows 11 de 64 bits actualizado.
- Virtualización habilitada en BIOS/UEFI.
- WSL 2 actualizado.
- Docker Desktop usando contenedores Linux.
- Al menos 8 GB de RAM física; 16 GB es preferible.
- Al menos 20 GB libres en disco para imágenes, capas, volúmenes y código.
- Acceso a Internet para Docker Hub, Microsoft Container Registry, NuGet y GitHub.

El repositorio utilizado es:

```text
https://github.com/gcortez78/modular-bank-dotnet.git
```

---

# PARTE A — Preparar Windows 11

## 5. Abrir PowerShell como administrador

1. Abrir el menú Inicio.
2. Buscar `PowerShell` o `Terminal`.
3. Seleccionar **Ejecutar como administrador**.

Comprobar la versión de Windows:

```powershell
winver
```

La ventana debe indicar Windows 11.

---

## 6. Verificar virtualización y WSL 2

Ejecutar:

```powershell
wsl --status
wsl --version
```

Si WSL no está instalado:

```powershell
wsl --install
```

Reiniciar Windows cuando lo solicite. Después del reinicio, abrir nuevamente PowerShell como administrador y ejecutar:

```powershell
wsl --update
wsl --set-default-version 2
```

Comprobar nuevamente:

```powershell
wsl --status
```

Debe mostrarse la versión predeterminada `2`.

---

## 7. Verificar Docker Desktop y Docker Compose

Como ya se dispone de Docker Compose, no es necesario reinstalarlo si todas estas verificaciones funcionan:

```powershell
docker --version
docker compose version
docker info --format '{{.OSType}}'
```

Resultado esperado del último comando:

```text
linux
```

Si aparece un error de conexión al daemon:

1. Abrir Docker Desktop.
2. Esperar hasta que indique que Docker Engine está activo.
3. En Docker Desktop, confirmar que se están usando contenedores Linux.
4. Volver a ejecutar los comandos.

Docker Desktop ya incluye Docker Engine, Docker CLI y Docker Compose; no debe instalarse el antiguo ejecutable independiente `docker-compose`.

---

# PARTE B — Instalar y configurar Git en Windows

## 8. Instalar Git for Windows

En PowerShell como administrador:

```powershell
winget install --id Git.Git -e --source winget
```

Al terminar, cerrar todas las ventanas de PowerShell y abrir una nueva terminal normal.

Verificar:

```powershell
git --version
```

Resultado esperado:

```text
git version 2.x.x.windows.x
```

Si se utiliza el instalador gráfico, pueden aceptarse las opciones predeterminadas. Se recomienda conservar Git Credential Manager para la autenticación con GitHub.

---

## 9. Configurar identidad de Git

Reemplazar el correo por el asociado a la cuenta de GitHub:

```powershell
git config --global user.name "Guillermo Cortez"
git config --global user.email "SU_CORREO_GITHUB"
```

Verificar:

```powershell
git config --global --list
```

No cambiar globalmente la rama inicial, porque el repositorio recibido actualmente utiliza `master`.

---

# PARTE C — Obtener el código desde GitHub

## 10. Crear la carpeta de trabajo

Usaremos una ruta corta y sin espacios:

```powershell
New-Item -ItemType Directory -Force C:\FinBank | Out-Null
Set-Location C:\FinBank
```

---

## 11. Clonar el repositorio

```powershell
git clone https://github.com/gcortez78/modular-bank-dotnet.git PLATAFORMA_BASE_FINBANK
Set-Location C:\FinBank\PLATAFORMA_BASE_FINBANK
```

Verificar:

```powershell
git status
git remote -v
git branch --show-current
```

Debe verse:

```text
master
```

Crear una rama exclusivamente para esta plataforma:

```powershell
git checkout -b feature/plataforma-base-finbank
```

---

# PARTE D — Incorporar los archivos de la plataforma

## 12. Aplicar el kit entregado

El archivo `PLATAFORMA_BASE_FINBANK_WINDOWS11_KIT.zip` contiene una carpeta `archivos_para_repositorio` y el instalador `INSTALAR_KIT.ps1`.

Suponiendo que el ZIP está en Descargas:

```powershell
New-Item -ItemType Directory -Force C:\FinBank\KIT | Out-Null
Expand-Archive `
  -Path "$HOME\Downloads\PLATAFORMA_BASE_FINBANK_WINDOWS11_KIT.zip" `
  -DestinationPath C:\FinBank\KIT `
  -Force
```

Ejecutar el instalador del kit:

```powershell
Set-ExecutionPolicy -Scope Process Bypass
C:\FinBank\KIT\PLATAFORMA_BASE_FINBANK_WINDOWS11_KIT\INSTALAR_KIT.ps1 `
  -RepoPath C:\FinBank\PLATAFORMA_BASE_FINBANK
```

El instalador realiza lo siguiente:

- Verifica que el repositorio sea el monolito FinBank.
- Renombra `docker-compose.yml` como `docker-compose.legacy.yml`.
- Copia `compose.yaml`.
- Agrega el Dockerfile del monolito.
- Crea el proyecto Gateway con YARP.
- Agrega la configuración de RabbitMQ.
- Agrega `.env.example`, `.dockerignore` y `.gitattributes`.
- Agrega los scripts PowerShell de instalación, construcción, arranque y validación.

Regresar al repositorio:

```powershell
Set-Location C:\FinBank\PLATAFORMA_BASE_FINBANK
git status
```

---

## 13. Revisar la estructura esperada

Ejecutar:

```powershell
Get-ChildItem -Force
Get-ChildItem .\src\Gateway
Get-ChildItem .\scripts\windows
```

La estructura principal debe quedar así:

```text
PLATAFORMA_BASE_FINBANK/
├─ compose.yaml
├─ docker-compose.legacy.yml
├─ .env.example
├─ .dockerignore
├─ .gitattributes
├─ infra/
│  └─ rabbitmq/
│     └─ enabled_plugins
├─ scripts/
│  └─ windows/
│     ├─ 01-verificar-entorno.ps1
│     ├─ 02-descargar-imagenes.ps1
│     ├─ 03-construir-imagenes.ps1
│     ├─ 04-levantar-plataforma.ps1
│     ├─ 05-validar-plataforma.ps1
│     └─ 06-detener-plataforma.ps1
└─ src/
   ├─ Gateway/
   │  ├─ Gateway.csproj
   │  ├─ Program.cs
   │  ├─ appsettings.json
   │  └─ Dockerfile
   └─ ModularBank/
      ├─ Dockerfile
      └─ ...
```

---

# PARTE E — Configurar variables locales

## 14. Crear el archivo `.env`

Desde la raíz del repositorio:

```powershell
Copy-Item .env.example .env
```

Abrirlo con Bloc de notas:

```powershell
notepad .env
```

Contenido inicial:

```dotenv
COMPOSE_PROJECT_NAME=plataforma-base-finbank
GATEWAY_PORT=8080

MONOLITH_DB=modular_bank
MONOLITH_DB_USER=bank
MONOLITH_DB_PASSWORD=bank-local
MONOLITH_DB_PORT=5433

JWT_SECRET=finbank-local-jwt-secret-change-this-value-2026

RABBITMQ_USER=finbank
RABBITMQ_PASSWORD=finbank-local
RABBITMQ_AMQP_PORT=5672
RABBITMQ_UI_PORT=15672
RABBITMQ_METRICS_PORT=15692
```

Para este laboratorio los valores pueden mantenerse, aunque es recomendable cambiar las contraseñas. `JWT_SECRET` debe tener al menos 32 caracteres porque el código del monolito valida esa longitud al iniciar.

El archivo `.env` está excluido por `.gitignore` y no debe enviarse a GitHub.

---

## 15. Verificar que los puertos estén libres

```powershell
$ports = 8080, 5433, 5672, 15672, 15692
Get-NetTCPConnection -State Listen -ErrorAction SilentlyContinue |
  Where-Object { $_.LocalPort -in $ports } |
  Select-Object LocalAddress, LocalPort, OwningProcess
```

Si no aparece ningún resultado, los puertos están libres.

Si algún puerto está ocupado, modificar solo el puerto local correspondiente en `.env`. Por ejemplo:

```dotenv
GATEWAY_PORT=8081
MONOLITH_DB_PORT=5443
```

No se deben modificar los puertos internos de los contenedores.

---

# PARTE F — Verificar el entorno mediante el script

## 16. Habilitar scripts para esta sesión

No se cambiará permanentemente la política de ejecución de Windows. En la terminal actual:

```powershell
Set-ExecutionPolicy -Scope Process Bypass
```

Ejecutar:

```powershell
.\scripts\windows\01-verificar-entorno.ps1
```

El script valida:

- Git disponible en `PATH`.
- Docker CLI disponible.
- Docker Compose disponible.
- Docker Engine activo.
- Uso de contenedores Linux.
- Estado de WSL.

No continuar hasta que finalice con:

```text
Entorno correcto para continuar.
```

---

# PARTE G — Descargar una por una las imágenes

## 17. Descarga manual controlada

Las imágenes deben descargarse en este orden. Esperar a que cada comando finalice antes de ejecutar el siguiente.

### 17.1 PostgreSQL

```powershell
docker pull postgres:16-alpine
```

Verificar:

```powershell
docker image inspect postgres:16-alpine --format '{{json .RepoTags}}'
```

### 17.2 RabbitMQ

```powershell
docker pull rabbitmq:4.3.2-management-alpine
```

Verificar:

```powershell
docker image inspect rabbitmq:4.3.2-management-alpine --format '{{json .RepoTags}}'
```

### 17.3 .NET 10 SDK

```powershell
docker pull mcr.microsoft.com/dotnet/sdk:10.0
```

Verificar:

```powershell
docker image inspect mcr.microsoft.com/dotnet/sdk:10.0 --format '{{json .RepoTags}}'
```

### 17.4 ASP.NET Core 10 Runtime

```powershell
docker pull mcr.microsoft.com/dotnet/aspnet:10.0
```

Verificar:

```powershell
docker image inspect mcr.microsoft.com/dotnet/aspnet:10.0 --format '{{json .RepoTags}}'
```

### 17.5 Ver todas las imágenes

```powershell
docker images --format "table {{.Repository}}`t{{.Tag}}`t{{.ID}}`t{{.Size}}"
```

---

## 18. Alternativa automatizada, también una por una

El script ejecuta exactamente las mismas descargas secuenciales:

```powershell
.\scripts\windows\02-descargar-imagenes.ps1
```

No ejecuta descargas en paralelo. Si una imagen falla, el script se detiene y muestra cuál fue.

---

# PARTE H — Validar Docker Compose y construir las imágenes FinBank

## 19. Validar la configuración

```powershell
docker compose config --quiet
```

Si no muestra errores, listar servicios:

```powershell
docker compose config --services
```

Deben estar definidos, como mínimo, `postgres-monolith`, `rabbitmq`, `monolith` y `gateway`. Dependiendo de la versión de Compose, el listado también puede mostrar `postgres-notifications` y `postgres-transfers`; ambos pertenecen al perfil `future` y no se inician en esta primera parte.

---

## 20. Construir primero el monolito

```powershell
docker compose build monolith
```

Durante el build Docker utiliza:

1. `mcr.microsoft.com/dotnet/sdk:10.0` para ejecutar `dotnet restore` y `dotnet publish`.
2. `mcr.microsoft.com/dotnet/aspnet:10.0` para crear la imagen de ejecución.

Verificar:

```powershell
docker image inspect plataforma-base-finbank-monolith:1.0 --format '{{json .RepoTags}}'
```

---

## 21. Construir después el Gateway

```powershell
docker compose build gateway
```

En esta construcción se descarga `Yarp.ReverseProxy` desde NuGet. YARP forma parte de la imagen creada; no existe una imagen adicional que deba descargarse.

Verificar:

```powershell
docker image inspect plataforma-base-finbank-gateway:1.0 --format '{{json .RepoTags}}'
```

---

## 22. Construcción automatizada

También puede ejecutarse:

```powershell
.\scripts\windows\03-construir-imagenes.ps1
```

Este script primero valida Compose, luego construye `monolith` y finalmente `gateway`.

---

# PARTE I — Levantar la plataforma servicio por servicio

## 23. Iniciar PostgreSQL

```powershell
docker compose up -d postgres-monolith
docker compose ps postgres-monolith
```

Esperar hasta ver:

```text
healthy
```

Si tarda, consultar:

```powershell
docker compose logs -f postgres-monolith
```

Salir del seguimiento con `Ctrl+C`.

---

## 24. Iniciar RabbitMQ

```powershell
docker compose up -d rabbitmq
docker compose ps rabbitmq
```

Esperar el estado `healthy`.

Consultar logs si fuera necesario:

```powershell
docker compose logs -f rabbitmq
```

---

## 25. Iniciar el monolito

```powershell
docker compose up -d monolith
docker compose ps monolith
```

En este arranque el monolito aplicará automáticamente las migraciones EF Core de los cinco módulos. Puede tardar más que un reinicio normal.

Ver logs:

```powershell
docker compose logs -f monolith
```

Esperar el estado `healthy`.

---

## 26. Iniciar el Gateway

```powershell
docker compose up -d gateway
docker compose ps gateway
```

Esperar el estado `healthy`.

---

## 27. Inicio automatizado y secuencial

El script inicia los cuatro servicios en el mismo orden y espera la salud de cada uno:

```powershell
.\scripts\windows\04-levantar-plataforma.ps1
```

No intenta iniciar el monolito antes de que PostgreSQL esté saludable ni el Gateway antes de que el monolito responda.

---

# PARTE J — Validar PLATAFORMA_BASE_FINBANK

## 28. Verificar todos los contenedores

```powershell
docker compose ps
```

Resultado esperado:

| NAME | SERVICE | STATUS |
|---|---|---|
| `finbank-postgres-monolith` | `postgres-monolith` | Up, healthy |
| `finbank-rabbitmq` | `rabbitmq` | Up, healthy |
| `finbank-monolith` | `monolith` | Up, healthy |
| `finbank-gateway` | `gateway` | Up, healthy |

---

## 29. Validar el Gateway

```powershell
Invoke-RestMethod http://localhost:8080/health
```

Resultado esperado:

```text
Healthy
```

Este endpoint corresponde al Gateway.

---

## 30. Validar el enrutamiento Gateway → Monolito

Registrar un usuario a través del puerto del Gateway:

```powershell
$email = "finbank.$([DateTimeOffset]::UtcNow.ToUnixTimeSeconds())@example.com"
$body = @{
  email = $email
  password = "FinBank123!"
  name = "Usuario de prueba"
} | ConvertTo-Json

$response = Invoke-RestMethod `
  -Method Post `
  -Uri "http://localhost:8080/auth/register" `
  -ContentType "application/json" `
  -Body $body

$response
```

La respuesta debe incluir:

```text
accessToken
refreshToken
```

Esto demuestra que:

1. El cliente entra por el Gateway.
2. YARP reenvía `/auth/register` al monolito.
3. El monolito procesa la operación.
4. PostgreSQL persiste el usuario.
5. La respuesta vuelve por el Gateway.

---

## 31. Validar PostgreSQL y los schemas

```powershell
$dbUser = (docker compose exec -T postgres-monolith printenv POSTGRES_USER).Trim()
$dbName = (docker compose exec -T postgres-monolith printenv POSTGRES_DB).Trim()
$sql = "SELECT schema_name FROM information_schema.schemata WHERE schema_name IN ('auth','accounts','transfers','notifications','audit') ORDER BY schema_name;"
docker compose exec -T postgres-monolith psql -U $dbUser -d $dbName -c $sql
```

Deben aparecer:

```text
accounts
audit
auth
notifications
transfers
```

---

## 32. Validar RabbitMQ

Abrir en el navegador:

```text
http://localhost:15672
```

Credenciales predeterminadas del archivo `.env`:

```text
Usuario: finbank
Contraseña: finbank-local
```

Validar métricas:

```powershell
Invoke-WebRequest -UseBasicParsing http://localhost:15692/metrics |
  Select-Object StatusCode
```

Resultado esperado:

```text
200
```

En esta fase RabbitMQ no tendrá todavía exchanges ni colas de negocio; su disponibilidad es la evidencia requerida.

---

## 33. Ejecutar la validación automática completa

```powershell
.\scripts\windows\05-validar-plataforma.ps1
```

El script comprueba:

- Estado de Compose.
- Health del Gateway.
- Consola RabbitMQ.
- Schemas creados en PostgreSQL.
- Registro de usuario a través del Gateway.

Debe terminar con:

```text
PLATAFORMA_BASE_FINBANK validada satisfactoriamente.
```

---

# PARTE K — Guardar el avance en GitHub

## 34. Revisar cambios antes del commit

```powershell
git status
git diff --stat
```

Confirmar que `.env` no aparece en la lista.

---

## 35. Crear el commit

```powershell
git add .
git commit -m "chore: implement PLATAFORMA_BASE_FINBANK with Docker Compose"
```

---

## 36. Enviar la rama a GitHub

```powershell
git push -u origin feature/plataforma-base-finbank
```

Si GitHub solicita autenticación, completar el inicio de sesión en la ventana del navegador o en Git Credential Manager.

Verificar:

```powershell
git status
```

Resultado esperado:

```text
Your branch is up to date with 'origin/feature/plataforma-base-finbank'.
nothing to commit, working tree clean
```

---

# PARTE L — Operación diaria

## 37. Detener sin borrar datos

```powershell
.\scripts\windows\06-detener-plataforma.ps1
```

También puede usarse:

```powershell
docker compose down
```

Los volúmenes permanecen y los datos sobreviven al siguiente arranque.

---

## 38. Volver a iniciar

```powershell
docker compose up -d
```

Como las imágenes ya están construidas y los volúmenes existen, el arranque será más rápido.

---

## 39. Borrar completamente el laboratorio

Solo cuando se necesite reiniciar desde cero:

```powershell
.\scripts\windows\06-detener-plataforma.ps1 -EliminarDatos
```

Equivale a:

```powershell
docker compose down --volumes --remove-orphans
```

**Advertencia:** elimina usuarios, cuentas, transferencias, auditoría, notificaciones y datos de RabbitMQ almacenados localmente.

---

# PARTE M — Diagnóstico de problemas frecuentes

## 40. Docker no responde

Mensaje típico:

```text
error during connect
```

Acciones:

```powershell
wsl --update
wsl --shutdown
```

Después abrir Docker Desktop y esperar el inicio del motor.

---

## 41. Docker está usando Windows containers

Comprobar:

```powershell
docker info --format '{{.OSType}}'
```

Si devuelve `windows`, cambiar Docker Desktop a Linux containers. Las imágenes PostgreSQL, RabbitMQ y .NET de esta guía son Linux.

---

## 42. Un puerto está ocupado

Ejemplo para el puerto 8080:

```powershell
Get-NetTCPConnection -LocalPort 8080 -ErrorAction SilentlyContinue
```

Modificar `.env`, por ejemplo:

```dotenv
GATEWAY_PORT=8081
```

Recrear el Gateway:

```powershell
docker compose up -d --force-recreate gateway
```

---

## 43. El monolito aparece `unhealthy`

```powershell
docker compose logs --tail 200 monolith
```

Revisar especialmente:

- Conexión a `postgres-monolith`.
- Longitud de `JWT_SECRET`.
- Errores de migraciones EF Core.
- Fallos al restaurar o publicar paquetes.

Revisar PostgreSQL:

```powershell
docker compose ps postgres-monolith
docker compose logs --tail 100 postgres-monolith
```

---

## 44. El Gateway devuelve 502 o 503

```powershell
docker compose ps monolith gateway
docker compose logs --tail 100 gateway
docker compose logs --tail 100 monolith
```

El Gateway debe resolver al monolito mediante el nombre interno:

```text
http://monolith:8080/
```

No debe configurarse `localhost` dentro de `src/Gateway/appsettings.json`, porque dentro del contenedor `localhost` se referiría al propio Gateway.

---

## 45. Fallo al descargar paquetes NuGet

El build necesita acceso a Internet:

```powershell
docker compose build --no-cache gateway
```

Si aparece `NU1301`, revisar proxy corporativo, VPN, DNS y acceso a `api.nuget.org` desde Docker Desktop.

---

## 46. RabbitMQ no permite ingresar

Confirmar los valores efectivos:

```powershell
docker compose exec rabbitmq printenv RABBITMQ_DEFAULT_USER
docker compose exec rabbitmq printenv RABBITMQ_DEFAULT_PASS
```

Si las credenciales se cambiaron después del primer arranque, RabbitMQ conservará los usuarios en el volumen. Para reiniciarlo desde cero:

```powershell
docker compose down
docker volume ls --filter name=rabbitmq
docker compose down --volumes
```

Esto borra también el resto de volúmenes del proyecto; usarlo únicamente en el laboratorio.

---

# 47. Criterio de finalización de esta primera parte

La plataforma base se considera completada cuando se cumplen simultáneamente estos puntos:

- Git está instalado en Windows 11 y el repositorio fue clonado desde GitHub.
- Existe la rama `feature/plataforma-base-finbank`.
- Las cuatro imágenes externas se descargaron individualmente.
- Las imágenes locales del monolito y Gateway se construyeron sin errores.
- Los cuatro contenedores base están `healthy`.
- `http://localhost:8080/health` responde correctamente.
- `/auth/register` funciona a través del Gateway.
- PostgreSQL contiene los schemas `auth`, `accounts`, `transfers`, `notifications` y `audit`.
- RabbitMQ Management abre en `http://localhost:15672`.
- El avance está enviado a GitHub.

Al alcanzar este estado, `PLATAFORMA_BASE_FINBANK` está lista para iniciar el Paso 1 del reto: análisis y primera extracción mediante Strangler Fig.

---

## Fuentes oficiales consultadas

- [Docker Desktop para Windows](https://docs.docker.com/desktop/setup/install/windows-install/)
- [Docker Desktop con WSL 2](https://docs.docker.com/desktop/features/wsl/)
- [Instalación de Docker Compose](https://docs.docker.com/compose/install/)
- [Git for Windows](https://git-scm.com/install/windows)
- [Imágenes oficiales de .NET](https://learn.microsoft.com/en-us/dotnet/core/docker/container-images)
- [ASP.NET Core en Docker](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/docker/building-net-docker-images?view=aspnetcore-10.0)
- [YARP — inicio](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/servers/yarp/getting-started?view=aspnetcore-10.0)
- [RabbitMQ mediante Docker](https://www.rabbitmq.com/docs/download)
- [Imagen oficial PostgreSQL](https://hub.docker.com/_/postgres)
