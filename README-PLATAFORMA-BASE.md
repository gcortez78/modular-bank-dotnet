# PLATAFORMA_BASE_FINBANK

Plataforma local para Windows 11 y Docker Desktop compuesta por:

- Gateway YARP.
- Monolito modular .NET 10.
- PostgreSQL 16.
- RabbitMQ 4 con consola de administración y métricas Prometheus.
- Bases PostgreSQL futuras para Notifications y Transfers mediante el perfil `future`.

## Inicio rápido en PowerShell

```powershell
Copy-Item .env.example .env
Set-ExecutionPolicy -Scope Process Bypass
.\scripts\windows\01-verificar-entorno.ps1
.\scripts\windows\02-descargar-imagenes.ps1
.\scripts\windows\03-construir-imagenes.ps1
.\scripts\windows\04-levantar-plataforma.ps1
.\scripts\windows\05-validar-plataforma.ps1
```

Accesos:

- API Gateway: http://localhost:8080
- RabbitMQ Management: http://localhost:15672
- RabbitMQ Metrics: http://localhost:15692/metrics
- PostgreSQL: localhost:5433
