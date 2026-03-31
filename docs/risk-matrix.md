# Matriz de Riesgos - POC POS NuGet

## Resumen

| # | Riesgo | Probabilidad | Impacto | Severidad | Mitigacion |
|---|--------|-------------|---------|-----------|------------|
| R1 | Version drift entre modulos NuGet | Alta | Alto | Critico | Version centralizada en Directory.Build.props, CI valida consume |
| R2 | Perdida de datos en sync (outbox) | Media | Alto | Alto | Marcar synced SOLO despues de confirmacion, upsert idempotente en cloud |
| R3 | Feed NuGet no disponible | Baja | Alto | Medio | Cache local de NuGet, fallback a ProjectReference para dev |
| R4 | Sin autenticacion en endpoints | Alta (POC) | Alto (Prod) | Medio | Documentado como out-of-scope, API key como primer paso post-MVP |
| R5 | .NET 10 / EF Core 10 estabilidad | Media | Medio | Medio | Versiones fijas, tests de CI, path de rollback a .NET 9 |
| R6 | Sync entries acumuladas sin DLQ | Media | Medio | Medio | Monitoreo via query, DLQ post-MVP |
| R7 | PostgreSQL como SPOF | Media | Alto | Alto | Replicas de lectura, backup automatico en produccion |
| R8 | Breaking changes en Contracts | Media | Alto | Alto | Semantic versioning, CI que compila consumidores |

## Detalle

### R1: Version Drift entre Modulos NuGet

**Descripcion**: Los hosts pueden referenciar versiones diferentes de un mismo modulo, causando incompatibilidades en runtime.

**Mitigacion actual**:
- `Directory.Build.props` centraliza la version (`0.1.0-local`)
- CI workflow `nuget-consume.yml` compila ambos hosts con `UseProjectReference=false`

**Mitigacion futura**:
- Adoptar semantic versioning estricto
- Workflow que valide compatibilidad de interfaces entre versiones
- Considerar herramienta como `dotnet-compatibility` o `ApiCompat`

### R2: Perdida de Datos en Sync

**Descripcion**: Un entry del outbox se marca como synced pero el cloud no lo recibio, o el worker falla entre el POST exitoso y el UPDATE de SyncedAt.

**Mitigacion actual**:
- Se marca como synced DESPUES de HTTP 2xx
- Cloud endpoint es idempotente (upsert por ID)

**Mitigacion futura**:
- Transaccion distribuida (Saga pattern)
- Retry con Polly + exponential backoff
- Dead-letter queue para entries que fallan N veces

### R3: Feed NuGet No Disponible

**Descripcion**: Si GitHub Packages esta caido, CI no puede publicar ni consumir paquetes.

**Mitigacion actual**:
- `UseProjectReference=true` como default permite desarrollo sin feed
- `--skip-duplicate` en push evita errores por re-publicacion

**Mitigacion futura**:
- Feed secundario (Azure Artifacts como backup)
- Cache local de paquetes via `nuget locals` cache

### R4: Sin Autenticacion en Endpoints

**Descripcion**: Todos los endpoints (CRUD y sync) estan abiertos sin autenticacion ni autorizacion.

**Mitigacion actual**:
- Documentado como limitacion del POC
- CORS permite cualquier origen (solo para POC)

**Mitigacion futura**:
- API key en header para sync endpoint (minimo)
- JWT/OAuth para endpoints de negocio
- Rate limiting por IP

### R5: .NET 10 Estabilidad

**Descripcion**: .NET 10 y EF Core 10 pueden tener bugs o breaking changes si son previews.

**Mitigacion actual**:
- Versiones fijas en `Directory.Build.props` (`10.0.0`)
- Build y pack validados localmente

**Mitigacion futura**:
- Tests automaticos en CI
- Rollback path a .NET 9 LTS si es necesario

### R6: Sync Entries Acumuladas

**Descripcion**: Si el cloud esta caido por tiempo prolongado, la tabla `sync.outbox_entries` crece indefinidamente.

**Mitigacion actual**:
- Worker procesa maximo 50 entries por ciclo
- Logging de entries fallidas

**Mitigacion futura**:
- DLQ table para entries que fallan > N veces
- TTL para limpiar entries antiguas sincronizadas
- Health check endpoint que expone backlog count

## Propuestas Post-MVP

1. **Autenticacion**: API key para sync, JWT para CRUD
2. **Observabilidad**: OpenTelemetry para tracing distribuido
3. **Resiliencia**: Polly para retry + circuit breaker en sync
4. **Validacion**: FluentValidation en todos los endpoints
5. **Migrations**: Migrar de `EnsureCreated` a EF Core Migrations
6. **Multi-tenancy**: Contexto de tenant en requests
7. **Health checks avanzados**: outbox backlog, cloud connectivity, DB latency
