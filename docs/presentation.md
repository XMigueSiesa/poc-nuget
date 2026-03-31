# POC NuGet: Distribucion de Modulos POS

## Presentacion para Equipo Tecnico SIESA

---

## 1. El Problema

- El POS necesita correr en **tiendas fisicas** (offline-first) y en la **nube** (servicios centralizados)
- Hoy: codigo duplicado entre proyectos, cambios se aplican manualmente en cada contexto
- **Resultado**: inconsistencias, bugs, esfuerzo duplicado de desarrollo

---

## 2. La Propuesta

**Un solo repositorio, multiples despliegues via NuGet**

```
Codigo fuente (modulos)
    → NuGet pack (CI/CD)
        → CloudHub consume TODOS los modulos (nube central)
        → LocalPOS consume un SUBCONJUNTO (tienda offline-first)
```

Desarrollar una vez, desplegar donde sea necesario.

---

## 3. Arquitectura

### Estructura de Modulos

Cada modulo se divide en:
- **Contracts**: interfaces + DTOs (lo que ven los consumidores)
- **Core**: implementacion, EF, endpoints (el "como")

### Hosts (consumidores)

| Host | Modulos | Contexto |
|------|---------|----------|
| **CloudHub** | Orders + Products + Payments + Sync receivers | Nube central (source of truth) |
| **LocalPOS** | Orders + Products + Payments + Sync worker | Tienda fisica (offline-first) |

**Nota**: En produccion, CloudHub tendria mas modulos (reportes, admin, integraciones). Cada LocalPOS carga solo los modulos necesarios para su contexto.

### Dual-Mode (la clave del POC)

Un flag de MSBuild (`UseProjectReference`) controla como se resuelven las dependencias:
- **Desarrollo**: `ProjectReference` (debug directo, F12 al codigo del modulo)
- **CI/CD**: `PackageReference` (NuGet publicado, versionado)

---

## 4. Demo

### Flujo 1: Desarrollo local
1. Abrir solucion en IDE
2. Los hosts referencian modulos como ProjectReference
3. Cambiar codigo en un modulo → el host lo ve inmediatamente
4. Debug con breakpoints directamente en el modulo

### Flujo 2: Distribucion NuGet
1. Push a main → GitHub Actions ejecuta pack+push
2. 8 paquetes NuGet publicados en GitHub Packages
3. Segundo workflow compila hosts con `UseProjectReference=false`
4. Si compila → los paquetes son validos y funcionales

### Flujo 3: Sincronizacion Local → Cloud
1. Crear producto u orden en LocalPOS (POST /api/products o POST /api/orders)
2. Outbox entry se crea en tabla `sync.outbox_entries`
3. Worker lee entry → POST al CloudHub (/api/sync/products o /api/sync/orders)
4. Dato aparece en CloudHub (GET /api/products o /api/orders en :5200)

---

## 5. Hallazgos

### Lo que funciona bien
- El mecanismo `Choose` de MSBuild es robusto y nativo
- 8 paquetes se generan correctamente con `dotnet pack`
- Multi-schema PostgreSQL aisla los modulos sin overhead
- Outbox pattern resuelve la sincronizacion sin infraestructura adicional

### Limitaciones identificadas
- Requiere CI/CD configurado (GitHub Actions o equivalente)
- Sin autenticacion ni validacion (scope de POC)
- Sync es polling-based (latencia de ~10s, no real-time)
- No hay strategy de versionado semantico definida

### Riesgos principales
1. **Version drift**: hosts con versiones incompatibles de modulos
2. **Sin DLQ**: entries fallidas de sync se reintentan indefinidamente
3. **Sin auth**: endpoints abiertos (critico para produccion)

---

## 6. Decisiones Arquitectonicas (ADRs)

| ADR | Decision | Alternativas descartadas |
|-----|----------|-------------------------|
| 001 | Modular Monolith + NuGet | Microservicios, Git submodules, shared library |
| 002 | Outbox Pattern (polling) | CDC/Debezium, HTTP directo, Event Sourcing, RabbitMQ |
| 003 | Multi-Schema PostgreSQL | DB separadas, schema unico, prefijos de tabla |

---

## 7. Stack Tecnico

| Componente | Tecnologia | Version |
|------------|-----------|---------|
| Runtime | .NET | 10.0 |
| ORM | EF Core | 10.0 |
| Base de datos | PostgreSQL | 17 |
| IDs | ULID | 1.3.4 |
| API docs | Scalar (OpenAPI) | 2.6 |
| CI/CD | GitHub Actions | N/A |
| Paquetes | NuGet / GitHub Packages | N/A |
| Contenedores | Docker Compose | N/A |

---

## 8. Roadmap Post-MVP

### Fase 1: Hardening
- Autenticacion (JWT para CRUD, API key para sync)
- Input validation (FluentValidation)
- EF Core Migrations (reemplazar EnsureCreated)

### Fase 2: Resiliencia
- Retry con Polly + circuit breaker en sync
- Dead-letter queue para sync fallido
- Health checks avanzados (outbox backlog, cloud connectivity)

### Fase 3: Observabilidad
- OpenTelemetry (tracing distribuido)
- Metricas de sync (latencia, throughput, error rate)
- Dashboard de estado de sincronizacion

### Fase 4: Escalabilidad
- Multi-tenancy (aislamiento por tienda)
- Versionado semantico con ApiCompat
- Considerar CDC (Debezium) para sync de alto volumen

---

## 9. Conclusion

El POC demuestra que es viable:
- Mantener una sola base de codigo para multiples contextos de despliegue
- Distribuir modulos como NuGet con switch automatico en CI/CD
- Sincronizar datos local→cloud con outbox pattern sin infraestructura adicional
- Evolucionar gradualmente de monolito modular a microservicios

**Recomendacion**: proceder con hardening (Fase 1) y adoptarlo como patron estandar para los modulos POS de SIESA.
