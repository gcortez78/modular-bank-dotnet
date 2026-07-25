# Catálogo de eventos de integración FinBank

| Evento CloudEvents | Productor | Routing key | Consumidor(es) | Cola |
|---|---|---|---|---|
| `com.finbank.transfers.transfer-requested.v1` | Transfers Service | `transfers.requested.v1` | Accounts en monolito | `accounts.transfer-requested.v1` |
| `com.finbank.accounts.transfer-applied.v1` | Accounts en monolito | `accounts.transfer-applied.v1` | Transfers Service | `transfers.account-results.v1` |
| `com.finbank.accounts.transfer-rejected.v1` | Accounts en monolito | `accounts.transfer-rejected.v1` | Transfers Service | `transfers.account-results.v1` |
| `com.finbank.transfers.transfer-completed.v1` | Transfers Service | `transfers.completed.v1` | Notifications Service y Audit | `notifications.transfer-completed.v1`, `audit.transfer-results.v1` |
| `com.finbank.transfers.transfer-failed.v1` | Transfers Service | `transfers.failed.v1` | Audit | `audit.transfer-results.v1` |

## Convenciones

- Exchange principal: `finbank.events` (`topic`, durable).
- Exchange de retry: `finbank.events.retry` (`topic`, durable).
- Dead Letter Exchange: `finbank.events.dlx` (`topic`, durable).
- Formato: CloudEvents 1.0 en JSON UTF-8.
- Content type AMQP: `application/cloudevents+json; charset=utf-8`.
- Versión mayor en `type`, `dataschema`, routing key y nombre de cola.
- Semántica de entrega: al menos una vez.
- Idempotencia: `CloudEvent.id` más nombre lógico del consumidor.
- Correlación de la Saga: `correlationid` igual al identificador de la transferencia.
- Causalidad: `causationid` identifica el evento inmediatamente anterior.

## Política de compatibilidad

1. Se permiten campos opcionales nuevos dentro de `data` sin cambiar `v1`.
2. No se renombran ni eliminan campos obligatorios en una versión publicada.
3. Los consumidores ignoran propiedades desconocidas solo cuando el JSON Schema de la versión las permite. En v1 los contratos son estrictos (`additionalProperties: false`) para detectar deriva durante el práctico.
4. Un cambio incompatible genera `v2` y una routing key/cola paralela.
5. La convivencia v1/v2 debe mantenerse hasta migrar todos los consumidores.
