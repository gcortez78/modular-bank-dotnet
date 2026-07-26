# Evidencia ADR-001

Fecha de generaciÃ³n: 2026-07-24 21:21:04

## Componentes validados

- API Gateway basado en YARP.
- Notifications Service ejecutado como servicio independiente.
- PostgreSQL exclusivo para Notifications Service.
- Esquema de base de datos: notifications.
- Historial de migraciones de Entity Framework Core.
- Enrutamiento /notifications hacia el microservicio.
- Enrutamiento del resto de rutas hacia el monolito.

## Archivos generados

- Estado de Docker Compose.
- Logs integrados de los servicios.
- Esquema SQL de Notifications.
- Historial de migraciones.
- Conteo de registros.
- Pruebas y logs de enrutamiento, cuando se proporciona un JWT.
