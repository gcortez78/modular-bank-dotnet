# ADR-001 — Implementación de la primera extracción: Notifications

**Estado:** Aprobado  
**Módulo:** Notifications  
**Patrón:** Strangler Fig  
**Fecha de implementación propuesta:** julio de 2026

## Mapeo de la decisión a código

| Decisión del ADR | Evidencia en la implementación |
|---|---|
| Extraer Notifications primero | Nuevo proyecto `src/NotificationsService` |
| Despliegue independiente | Imagen `plataforma-base-finbank-notifications:1.0` |
| Database-per-Service | Contenedor `finbank-postgres-notifications` |
| Punto único de entrada | YARP enruta `/notifications` al microservicio |
| Monolito remanente | Auth, Accounts, Transfers y Audit continúan en `monolith` |
| Dependencia saliente controlada | `NotificationsHttpClient` implementa la interfaz del monolito |
| Fallo de notificación no invalida transferencias | `TransferUseCase` registra warning y continúa |
| Migración gradual | El código y schema legacy permanecen temporalmente, sin nuevas escrituras |
| Protección de API interna | Header `X-Internal-Api-Key` |
| Reducción de duplicados | Clave de idempotencia con índice único |

## Límite de esta implementación

Esta etapa mantiene comunicación HTTP sincrónica entre Transfers y Notifications
para limitar el alcance del primer corte. RabbitMQ continúa disponible, pero no se
usa todavía para eventos de negocio.

La evolución recomendada es un ADR posterior para implementar Outbox,
publicación de eventos, reintentos y DLQ. Hasta entonces, una falla al crear una
notificación después de confirmar una transferencia queda registrada en logs y
puede ocasionar una notificación faltante.

## Datos históricos

No se migra automáticamente el historial del schema `notifications` del monolito.
Las nuevas notificaciones se almacenan únicamente en la base exclusiva. El
histórico legacy debe permanecer sin escrituras hasta definir política de
retención, migración o consulta federada.
