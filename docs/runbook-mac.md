# Runbook — Ejecución en macOS

> POC NuGet POS · Guía paso a paso para desarrollo y demo

---

## Requisitos

| Herramienta | Versión mínima | Verificar |
|-------------|---------------|-----------|
| .NET SDK | 10.0 | `dotnet --version` |
| Docker Desktop | 4.x | `docker --version` |
| Git | cualquiera | `git --version` |

### Instalar prerequisites

```bash
# .NET 10 SDK
brew install --cask dotnet-sdk

# Docker Desktop (descargar desde https://www.docker.com/products/docker-desktop)
# O via brew:
brew install --cask docker

# Verificar instalaciones
dotnet --version    # debe mostrar 10.x.x
docker --version    # debe mostrar 4.x.x o superior
```

---

## Estructura del proyecto

```
poc-nuget/
├── docs/                          ← documentación
├── pos-backend/
│   ├── docker-compose.yml         ← PostgreSQL 17
│   ├── docker/
│   │   └── init-databases.sql     ← crea pos_local y pos_cloud
│   ├── Directory.Build.props      ← flag UseProjectReference (dual-mode NuGet)
│   ├── src/
│   │   ├── Shared/
│   │   │   └── Pos.SharedKernel/  ← contratos e infraestructura compartida
│   │   ├── Modules/
│   │   │   ├── Pos.Orders.*       ← módulo de órdenes (Contracts + Core)
│   │   │   ├── Pos.Products.*     ← módulo de productos (Contracts + Core)
│   │   │   └── Pos.Payments.*     ← módulo de pagos (Contracts + Core)
│   │   └── Hosts/
│   │       ├── Pos.Host.CloudHub/  ← sistema central (todos los módulos)
│   │       └── Pos.Host.LocalPOS/ ← tienda física (subset + sincronización)
│   └── tests/
│       └── Pos.Tests/             ← tests unitarios
```

---

## Ejecución paso a paso

### 1. Clonar y posicionarse

```bash
git clone <repo-url>
cd poc-nuget/pos-backend
```

### 2. Levantar la base de datos

```bash
docker compose up -d
```

Verificar que esté saludable:

```bash
docker compose ps
# Estado esperado: pos-postgres   Up (healthy)
```

El script `init-databases.sql` crea automáticamente dos bases:
- `pos_local` — usada por **LocalPOS**
- `pos_cloud` — usada por **CloudHub**

### 3. Levantar el CloudHub

Abrir una terminal nueva:

```bash
cd src/Hosts/Pos.Host.CloudHub
dotnet run
```

Esperar hasta ver en los logs:

```
info: Migrations complete for ProductsDbContext
info: Migrations complete for OrdersDbContext
info: Migrations complete for PaymentsDbContext
info: Now listening on: http://0.0.0.0:5200
```

### 4. Levantar el LocalPOS

Abrir otra terminal nueva:

```bash
cd src/Hosts/Pos.Host.LocalPOS
dotnet run
```

Esperar hasta ver:

```
info: Migrations complete for SyncDbContext
info: Sync worker started. Polling every 10s to http://localhost:5200
info: Now listening on: http://0.0.0.0:5100
```

### 5. Verificar que ambos están corriendo

```bash
curl -s http://localhost:5200 | jq .
# { "service": "POS Cloud Hub", "status": "Running", ... }

curl -s http://localhost:5100 | jq .
# { "service": "POS Local Store", "status": "Running", ... }
```

---

## Explorar los APIs

Abrir en el navegador:

| Host | URL | Puerto |
|------|-----|--------|
| CloudHub | http://localhost:5200/scalar/v1 | 5200 |
| LocalPOS | http://localhost:5100/scalar/v1 | 5100 |

---

## Demo del Outbox Pattern

### Paso 1 — Crear una categoría en LocalPOS

```bash
curl -s -X POST http://localhost:5100/api/products/categories \
  -H "Content-Type: application/json" \
  -d '{"name": "Electrónica"}' | jq .
```

Copiar el `id` del resultado.

### Paso 2 — Crear un producto en LocalPOS

```bash
curl -s -X POST http://localhost:5100/api/products \
  -H "Content-Type: application/json" \
  -d '{
    "name": "Laptop SIESA Pro",
    "description": "Laptop para demo",
    "categoryId": "<ID_CATEGORIA>",
    "price": 2500000
  }' | jq .
```

### Paso 3 — Verificar el outbox (antes de sincronizar)

```bash
docker exec -it pos-postgres psql -U pos -d pos_local \
  -c "SELECT entity_type, entity_id, created_at, synced_at FROM sync.outbox_entries ORDER BY created_at DESC LIMIT 5;"
```

`synced_at` debe ser `NULL` — el registro está pendiente de sincronización.

### Paso 4 — Esperar el ciclo del worker (~10 segundos)

Observar los logs del LocalPOS:

```
info: Found 1 pending sync entries
info: Synced Product 01JXXXXXXXXX to cloud
```

### Paso 5 — Verificar que llegó al CloudHub

```bash
docker exec -it pos-postgres psql -U pos -d pos_cloud \
  -c "SELECT id, name, price FROM products.products ORDER BY created_at DESC LIMIT 5;"
```

### Paso 6 — Confirmar que el outbox quedó marcado

```bash
docker exec -it pos-postgres psql -U pos -d pos_local \
  -c "SELECT entity_id, synced_at FROM sync.outbox_entries ORDER BY created_at DESC LIMIT 3;"
```

`synced_at` debe tener un timestamp — sincronización completa.

---

## Correr los tests

```bash
cd poc-nuget/pos-backend
dotnet test --logger "console;verbosity=normal"
```

Resultado esperado: **8 tests passed**.

---

## Demo del NuGet dual-mode

### Modo desarrollo (default) — ProjectReference

```bash
# UseProjectReference=true por defecto en Directory.Build.props
dotnet build

# Referencia directa a los proyectos: se puede debuggear dentro del módulo
```

### Modo CI/CD — PackageReference

```bash
# Primero empaquetar los módulos
dotnet pack --configuration Release

# Verificar que se generaron los .nupkg
ls artifacts/nupkg/

# Construir los hosts consumiendo los paquetes (simula CI/CD)
dotnet build -p:UseProjectReference=false
```

---

## Limpieza

```bash
# Bajar contenedores y eliminar volúmenes (datos)
docker compose down -v

# Limpiar artefactos de build
dotnet clean
rm -rf artifacts/
```

---

## Solución de problemas

| Error | Causa probable | Solución |
|-------|---------------|----------|
| `docker: command not found` | Docker Desktop no está abierto | Abrir Docker Desktop desde Applications |
| `Connection refused :5432` | PostgreSQL no terminó de iniciar | Esperar 10s y reintentar |
| `Address already in use :5100` | Puerto ocupado por otro proceso | `lsof -i :5100` para ver qué proceso es |
| `Address already in use :5200` | Puerto ocupado | `lsof -i :5200` |
| Build falla con `TreatWarningsAsErrors` | Warnings en el código | Revisar la salida del build y corregir |
| `jq: command not found` | jq no instalado | `brew install jq` |
