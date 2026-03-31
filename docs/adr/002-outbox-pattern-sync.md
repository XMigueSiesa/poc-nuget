# ADR-002: Outbox Pattern para Sincronizacion Local-Cloud

**Estado**: Aceptado
**Fecha**: 2026-03-26
**Contexto**: POC POS Backend SIESA

## Contexto

El POS opera en tiendas fisicas donde la conectividad a internet no es confiable. Los datos creados localmente (productos, ordenes) deben sincronizarse eventualmente con los servicios cloud cuando hay conexion.

Se requiere:
- **Local-first**: las operaciones locales deben completarse en <1ms sin depender de la nube
- **Consistencia eventual**: los datos deben llegar al cloud tarde o temprano
- **Tolerancia a fallas**: si el cloud no esta disponible, los datos no se pierden

## Decision

Implementar el **Outbox Pattern** con un background worker que hace polling:

1. Cuando un modulo crea/actualiza una entidad, ademas de persistirla, escribe un `SyncOutboxEntry` en una tabla dedicada (`sync.outbox_entries`)
2. Un `BackgroundService` (`SyncOutboxWorker`) hace polling cada N segundos buscando entries sin sincronizar
3. Para cada entry pendiente, hace HTTP POST al endpoint de sync del cloud
4. Si exitoso, marca la entry con `SyncedAt` timestamp
5. Si falla, la entry permanece pendiente y se reintenta en el siguiente ciclo

La tabla outbox tiene un indice filtrado sobre `SyncedAt IS NULL` para consultas eficientes.

## Alternativas Consideradas

### 1. Change Data Capture (CDC)
- **Descartado**: requiere Debezium + Kafka. Infraestructura excesiva para POC.
- Viable para produccion futura.

### 2. HTTP directo en cada operacion
- **Descartado**: acopla la operacion local a la disponibilidad del cloud.
- Viola el principio local-first.

### 3. Event Sourcing
- **Descartado**: cambio fundamental en el modelo de datos.
- Complejidad desproporcionada para el alcance del POC.

### 4. Queue-based (RabbitMQ/Redis Streams)
- **Descartado para POC**: agrega dependencia de infraestructura.
- La tabla outbox en PostgreSQL cumple la misma funcion sin dependencias externas.

## Consecuencias

### Positivas
- Zero infraestructura adicional (la DB ya existe)
- Operaciones locales no se bloquean esperando al cloud
- Entries pendientes sobreviven reinicios del servicio
- Facil de monitorear (SELECT COUNT WHERE SyncedAt IS NULL)

### Negativas
- Polling introduce latencia (configurable, default 10s)
- Sin retry con backoff exponencial (MVP: simple retry en siguiente ciclo)
- Sin dead-letter queue para entries que fallan repetidamente
- Sin garantia de orden estricto (acceptable para upsert idempotente)

### Riesgos
- Entries que fallan indefinidamente sin DLQ saturan la tabla
- Sin autenticacion en el endpoint de sync del cloud
