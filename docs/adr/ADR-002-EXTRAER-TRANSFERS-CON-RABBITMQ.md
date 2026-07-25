# ADR-002 — Extraer Transfers y reemplazar Notifications por eventos RabbitMQ

## Estado

Aceptada para implementación.

## Contexto

Después de extraer Notifications, el módulo Transfers conserva una dependencia
con ese componente. La interacción original se ejecutaba mediante una llamada
directa dentro del proceso y, durante ADR-001, mediante un cliente HTTP interno.

El segundo paso requiere extraer un módulo relacionado con el primer
microservicio y reemplazar esa interacción por comunicación asíncrona.

Transfers también depende de Accounts para modificar saldos. Esa operación
requiere consistencia fuerte y no debe convertirse indiscriminadamente en una
cadena de eventos.

## Decisión

1. Extraer Transfers como `Transfers Service`.
2. Crear `transfers_db` y el esquema exclusivo `transfers`.
3. Enrutar `/transfers/*` mediante YARP al nuevo servicio.
4. Mantener Accounts en el monolito.
5. Exponer una API interna idempotente de Accounts para aplicar débito y crédito
   dentro de una transacción serializable.
6. Guardar la transferencia y el evento mediante Transactional Outbox.
7. Publicar `TransferCompletedIntegrationEvent` en RabbitMQ.
8. Consumir el evento desde Notifications Service.
9. Aplicar idempotencia en Notifications mediante `EventId`.
10. Configurar cola durable, DLX y DLQ.

## Topología

```text
Exchange: finbank.events
Routing key: transfers.completed.v1
Queue: notifications.transfer-completed.v1
DLX: finbank.events.dlx
DLQ: notifications.transfer-completed.v1.dlq
```

## Consecuencias positivas

- Transfers y Notifications quedan desacoplados temporalmente.
- Una caída de Notifications no impide completar transferencias.
- Los mensajes sobreviven a reinicios.
- Cada microservicio conserva propiedad exclusiva de sus datos.
- Los duplicados se neutralizan mediante idempotencia.
- El Outbox evita perder el evento después de confirmar una transferencia.

## Consecuencias y riesgos

- La notificación pasa a tener consistencia eventual.
- Se agrega complejidad operativa por RabbitMQ, Outbox, reintentos y DLQ.
- Accounts sigue siendo una dependencia síncrona temporal.
- Puede existir publicación duplicada si el proceso falla después del publish y
  antes de marcar el Outbox; el consumidor debe ser idempotente.
- El `BackgroundService` de Outbox es adecuado para una instancia de laboratorio.
  Para múltiples réplicas debe agregarse locking distribuido o
  `FOR UPDATE SKIP LOCKED`.

## Alternativas descartadas

### Llamada HTTP Transfers → Notifications

Descartada porque mantiene acoplamiento temporal y no cumple el requisito del
paso 2.

### Actualizar saldos por eventos

Descartada porque introduce consistencia eventual en una operación bancaria
crítica sin implementar saga, reservas, compensaciones y controles adicionales.

### Publicar directamente sin Outbox

Descartada porque una caída entre el commit de la transferencia y el publish
podría dejar una transferencia completada sin evento.

## Validación

La decisión se considera implementada cuando una transferencia se completa con
Notifications detenido y, al reiniciar Notifications, el mensaje pendiente se
consume y genera exactamente una notificación.
