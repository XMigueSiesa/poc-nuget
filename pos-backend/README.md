# POS Backend - POC NuGet Module Distribution

Sistema POS modular donde los modulos de negocio se desarrollan en un solo lugar y se distribuyen via NuGet a diferentes hosts segun el contexto de despliegue.

## Problema

SIESA necesita ejecutar el mismo codigo de negocio en contextos heterogeneos:
- **Nube** (CloudHub): sistema central con todos los modulos, source of truth
- **Tienda fisica** (LocalPOS): subconjunto de modulos, funciona offline-first

La solucion: **modulos como paquetes NuGet** con un switch `ProjectReference`/`PackageReference` controlado por MSBuild.

## Quick Start

```bash
# 1. Levantar PostgreSQL
docker-compose up -d

# 2. Ejecutar ambos hosts (en terminales separadas)
dotnet run --project src/Hosts/Pos.Host.CloudHub   # http://localhost:5200 (Cloud central)
dotnet run --project src/Hosts/Pos.Host.LocalPOS    # http://localhost:5100 (POS tienda)

# 3. Abrir Scalar UI
open http://localhost:5200/scalar/v1  # CloudHub (Orders + Products + Payments + Sync receivers)
open http://localhost:5100/scalar/v1  # LocalPOS (Orders + Products + Payments + Sync worker)
```

## Arquitectura

```
pos-backend/
├── src/
│   ├── Shared/
│   │   └── Pos.SharedKernel          # BaseEntity, IEventBus, SyncOutbox
│   ├── Infrastructure/
│   │   └── Pos.Infrastructure.Postgres # MigrationRunner
│   ├── Modules/
│   │   ├── Pos.Orders.Contracts      # Interfaces + DTOs
│   │   ├── Pos.Orders.Core           # EF + Endpoints
│   │   ├── Pos.Products.Contracts
│   │   ├── Pos.Products.Core
│   │   ├── Pos.Payments.Contracts
│   │   └── Pos.Payments.Core
│   └── Hosts/
│       ├── Pos.Host.CloudHub         # Todos los modulos (nube central)
│       └── Pos.Host.LocalPOS         # Modulos POS (tienda offline-first)
├── docker-compose.yml
├── Directory.Build.props              # Config centralizada + UseProjectReference
└── nuget.config                       # Feed NuGet (GitHub Packages)
```

### Topologia

| Host | Puerto | DB | Modulos | Rol |
|------|--------|----|---------|-----|
| **CloudHub** | 5200 | pos_cloud | Orders + Products + Payments | Sistema central, source of truth |
| **LocalPOS** | 5100 | pos_local | Orders + Products + Payments | POS de tienda, offline-first |

En produccion, CloudHub tendria mas modulos (reportes, admin, integraciones) mientras que cada LocalPOS solo carga los modulos necesarios para su contexto.

## NuGet Dual-Mode

El mecanismo central del POC esta en `Directory.Build.props`:

```xml
<UseProjectReference Condition="'$(UseProjectReference)' == ''">true</UseProjectReference>
```

| Modo | Flag | Dependencias | Uso |
|------|------|-------------|-----|
| **Desarrollo** | `UseProjectReference=true` (default) | `ProjectReference` | Debug directo en modulos |
| **CI/CD** | `UseProjectReference=false` | `PackageReference` (NuGet) | Distribucion via paquetes |

### Empaquetar manualmente

```bash
dotnet pack -c Release -o ./artifacts/nupkg
# Genera 8 paquetes .nupkg en artifacts/nupkg/
```

### Compilar en modo NuGet (requiere paquetes publicados en feed)

```bash
dotnet build -p:UseProjectReference=false
```

## Modulos

| Modulo | Endpoints | Schema DB |
|--------|----------|-----------|
| **Orders** | `GET/POST /api/orders`, `POST /{id}/lines`, `POST /{id}/close` | `orders` |
| **Products** | `GET/POST/PUT/DELETE /api/products`, `GET/POST/DELETE /api/categories` | `products` |
| **Payments** | `GET /api/payments/by-order/{id}`, `POST /api/payments` | `payments` |

## Sync (Outbox Pattern)

El LocalPOS sincroniza datos hacia la nube automaticamente:

1. **Write**: Crear/actualizar producto u orden en LocalPOS -> se escribe al outbox
2. **Push**: Background worker lee outbox cada 10s -> POST a CloudHub
3. **Mark**: Si exitoso, marca entry como sincronizada

Endpoints de sync en CloudHub:
- `POST /api/sync/products` - Recibe productos desde tiendas locales
- `POST /api/sync/orders` - Recibe ordenes desde tiendas locales

Configuracion en `appsettings.json` del LocalPOS:
```json
{
  "Sync": {
    "CloudBaseUrl": "http://localhost:5200",
    "PollingIntervalSeconds": 10
  }
}
```

## CI/CD (GitHub Actions)

| Workflow | Trigger | Funcion |
|----------|---------|---------|
| `nuget-publish.yml` | Push a main | Pack + Push a GitHub Packages |
| `nuget-consume.yml` | Despues de publish | Compila hosts con `UseProjectReference=false` |

## Stack Tecnico

- .NET 10 / C# (records inmutables, nullable)
- EF Core 10 + Npgsql 10 (PostgreSQL, multi-schema)
- ULID como identificadores
- Scalar para OpenAPI UI
- Docker Compose para PostgreSQL local

## Configurar NuGet Feed (GitHub Packages)

```bash
dotnet nuget add source "https://nuget.pkg.github.com/OWNER/index.json" \
  --name github --username USERNAME --password YOUR_PAT \
  --store-password-in-clear-text
```

Reemplazar `OWNER`, `USERNAME`, y `YOUR_PAT` con tus credenciales.

## Documentacion Adicional

- [ADR-001: Modular Monolith + NuGet](../docs/adr/001-modular-monolith-nuget-distribution.md)
- [ADR-002: Outbox Pattern](../docs/adr/002-outbox-pattern-sync.md)
- [ADR-003: Multi-Schema PostgreSQL](../docs/adr/003-multi-schema-postgres.md)
- [Matriz de Riesgos](../docs/risk-matrix.md)
- [Presentacion](../docs/presentation.md)
- [Diagramas](../docs/diagrams/) (Excalidraw)
- [Postman Collection](./pos-backend.postman_collection.json)
