# ADR-003: Multi-Schema PostgreSQL por Modulo

**Estado**: Aceptado
**Fecha**: 2026-03-26
**Contexto**: POC POS Backend SIESA

## Contexto

El monolito modular tiene 3 modulos de negocio (Orders, Products, Payments) mas un modulo de sincronizacion (Sync). Cada modulo necesita su propio modelo de datos sin interferir con los demas, pero mantener la simplicidad operativa de una sola base de datos.

## Decision

Usar **schemas separados de PostgreSQL** dentro de una misma base de datos:

| Schema | Modulo | Tablas |
|--------|--------|--------|
| `orders` | Orders | `Orders`, `OrderLines` |
| `products` | Products | `Products`, `Categories` |
| `payments` | Payments | `Payments` |
| `sync` | Sync | `outbox_entries` |

Cada `DbContext` configura su schema via `HasDefaultSchema()`. El `MigrationRunner` crea el schema explicitamente con `CREATE SCHEMA IF NOT EXISTS`.

## Alternativas Consideradas

### 1. Base de datos separada por modulo
- **Descartado para POC**: complejidad operativa (backups, conexiones, credenciales).
- Path de migracion viable para produccion.

### 2. Schema unico con prefijos de tabla
- **Descartado**: no ofrece aislamiento real. Un `SELECT *` muestra todas las tablas.

### 3. Schema unico sin prefijos
- **Descartado**: colisiones de nombres posibles. No hay aislamiento logico.

## Consecuencias

### Positivas
- Aislamiento logico sin overhead operativo
- Un solo connection string por host
- EF Core maneja schemas nativamente via `HasDefaultSchema()`
- Path de migracion: mover un schema a su propia DB es un `pg_dump` con `--schema`
- Cada migration history table esta aislada por schema

### Negativas
- No hay foreign keys entre schemas (by design para modularidad)
- `EnsureCreated` del MigrationRunner requiere logica especial para multi-schema
- Performance: una DB grande vs varias DBs pequenas (irrelevante para POC)
