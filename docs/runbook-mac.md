# Runbook — Ejecución en macOS

> POC NuGet POS · Guía paso a paso para desarrollo y demo

---

## Requisitos

| Herramienta | Versión mínima | Verificar |
|-------------|---------------|-----------|
| .NET SDK | 10.0 | `dotnet --version` |
| Docker Desktop | 4.x | `docker --version` |
| Git | cualquiera | `git --version` |
| jq (opcional) | cualquiera | `jq --version` |

### Instalar prerequisites

```bash
# .NET 10 SDK
brew install --cask dotnet-sdk

# Docker Desktop (descargar desde https://www.docker.com/products/docker-desktop)
# O via brew:
brew install --cask docker

# jq para formatear JSON en terminal (opcional, la demo funciona sin él)
brew install jq

# Verificar instalaciones
dotnet --version    # debe mostrar 10.x.x
docker --version    # debe mostrar 4.x.x o superior
```

---

## Estructura del proyecto

```
poc-nuget/
├── docs/                          ← documentación y runbooks
├── pos-backend/
│   ├── docker-compose.yml         ← PostgreSQL 17
│   ├── docker/
│   │   └── init-databases.sql     ← crea pos_local y pos_cloud
│   ├── Directory.Build.props      ← flag UseProjectReference (dual-mode NuGet)
│   ├── nuget.config               ← fuentes de paquetes (nuget.org + local)
│   ├── src/
│   │   ├── Shared/
│   │   │   └── Pos.SharedKernel/  ← contratos e infraestructura compartida
│   │   ├── Infrastructure/
│   │   │   └── Pos.Infrastructure.Postgres/ ← MigrationRunner
│   │   ├── Modules/
│   │   │   ├── Pos.Orders.*       ← módulo de órdenes (Contracts + Core)
│   │   │   ├── Pos.Products.*     ← módulo de productos (Contracts + Core)
│   │   │   └── Pos.Payments.*     ← módulo de pagos (Contracts + Core)
│   │   └── Hosts/
│   │       ├── Pos.Host.CloudHub/ ← sistema central (todos los módulos) :5200
│   │       └── Pos.Host.LocalPOS/ ← tienda física (subset + sync)     :5100
│   └── tests/
│       └── Pos.Tests/             ← tests unitarios (8 tests)
```

---

## Ejecución paso a paso

### 0. Limpiar estado anterior (si ya corriste la demo antes)

```bash
docker rm -f pos-postgres 2>/dev/null || true
docker compose down -v 2>/dev/null || true
```

### 1. Clonar y posicionarse

```bash
git clone https://github.com/XMigueSiesa/poc-nuget.git
cd poc-nuget/pos-backend
```

> **Todos los comandos de aquí en adelante asumen que estás en `poc-nuget/pos-backend`.**

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
- `pos_local` — usada por **LocalPOS** (tienda física)
- `pos_cloud` — usada por **CloudHub** (sistema central)

### 3. Levantar el CloudHub (terminal 1)

```bash
dotnet run --project src/Hosts/Pos.Host.CloudHub
```

Esperar hasta ver en los logs:

```
info: Database ready for OrdersDbContext
info: Database ready for ProductsDbContext
info: Database ready for PaymentsDbContext
info: Now listening on: http://0.0.0.0:5200
```

### 4. Levantar el LocalPOS (terminal 2)

Abrir otra terminal, ir a `poc-nuget/pos-backend`:

```bash
dotnet run --project src/Hosts/Pos.Host.LocalPOS
```

Esperar hasta ver:

```
info: Database ready for SyncDbContext
info: Sync worker started. Polling every 10s to http://localhost:5200
info: Now listening on: http://0.0.0.0:5100
```

### 5. Verificar que ambos están corriendo (terminal 3)

Abrir otra terminal para ejecutar comandos:

```bash
curl -s http://localhost:5200 | jq .
# { "service": "POS Cloud Hub", "status": "Running", ... }

curl -s http://localhost:5100 | jq .
# { "service": "POS Local Store", "status": "Running", ... }
```

> Sin jq: `curl -s http://localhost:5200` muestra el JSON sin formatear.

---

## Explorar los APIs

Abrir en el navegador:

| Host | Scalar UI | Puerto |
|------|-----------|--------|
| CloudHub | http://localhost:5200/scalar/v1 | 5200 |
| LocalPOS | http://localhost:5100/scalar/v1 | 5100 |

### Endpoints disponibles

| Grupo | Ruta | Métodos |
|-------|------|---------|
| Products | `/api/products` | GET, POST, PUT, DELETE |
| Categories | `/api/categories` | GET, POST, DELETE |
| Orders | `/api/orders` | GET, POST |
| Order Lines | `/api/orders/{id}/lines` | POST |
| Close Order | `/api/orders/{id}/close` | POST |
| Payments | `/api/payments` | POST |
| Payments by Order | `/api/payments/by-order/{orderId}` | GET |
| Health | `/health` | GET |

El CloudHub además tiene endpoints de sincronización:

| Ruta | Método | Propósito |
|------|--------|-----------|
| `/api/sync/categories` | POST | Recibe categorías desde tiendas |
| `/api/sync/products` | POST | Recibe productos desde tiendas |
| `/api/sync/orders` | POST | Recibe órdenes desde tiendas |

---

## Demo del Outbox Pattern

> **Resumen:** Crear datos en LocalPOS → el worker los sincroniza automáticamente al CloudHub.

### Paso 1 — Crear una categoría en LocalPOS

```bash
curl -s -X POST http://localhost:5100/api/categories \
  -H "Content-Type: application/json" \
  -d '{"name": "Electronica"}' | jq .
```

Respuesta esperada:
```json
{
  "id": "01JXXX...",
  "name": "Electronica",
  "description": null,
  "createdAt": "2026-03-31T..."
}
```

Copiar el `id` del resultado — lo usaremos como `categoryId` en el siguiente paso.

### Paso 2 — Esperar que la categoría se sincronice (~10 segundos)

Observar los logs del LocalPOS (terminal 2):

```
info: Found 1 pending sync entries
info: Synced Category 01JXXX... to cloud
```

> **Importante:** Esperar a ver este log antes de crear el producto. La categoría debe existir en el CloudHub antes de que el producto intente sincronizarse (restricción de FK).

### Paso 3 — Crear un producto en LocalPOS

Reemplazar `<ID_CATEGORIA>` con el `id` copiado en el paso 1:

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

### Paso 4 — Verificar el outbox (antes de que se sincronice)

Ejecutar rápido, dentro de los 10 segundos antes del próximo ciclo del worker:

```bash
docker exec -it pos-postgres psql -U pos -d pos_local \
  -c "SELECT entity_type, entity_id, synced_at FROM sync.outbox_entries ORDER BY created_at DESC LIMIT 5;"
```

El registro del producto debe tener `synced_at = NULL` (pendiente de sincronización).
La categoría debe tener `synced_at` con timestamp (ya sincronizada en el paso 2).

### Paso 5 — Esperar el ciclo del worker (~10 segundos)

Observar los logs del LocalPOS:

```
info: Found 1 pending sync entries
info: Synced Product 01JXXX... to cloud
```

### Paso 6 — Verificar que el producto llegó al CloudHub

```bash
docker exec -it pos-postgres psql -U pos -d pos_cloud \
  -c "SELECT id, name, price FROM products.products ORDER BY created_at DESC LIMIT 5;"
```

Debe mostrar "Laptop SIESA Pro" con precio 2500000.

### Paso 7 — Confirmar que el outbox quedó marcado

```bash
docker exec -it pos-postgres psql -U pos -d pos_local \
  -c "SELECT entity_type, entity_id, synced_at FROM sync.outbox_entries ORDER BY created_at DESC LIMIT 5;"
```

Todos los registros deben tener `synced_at` con timestamp — sincronización completa.

### Paso 8 — Verificar categorías en el cloud (bonus)

```bash
docker exec -it pos-postgres psql -U pos -d pos_cloud \
  -c "SELECT id, name FROM products.categories;"
```

La categoría "Electronica" también fue sincronizada.

---

## Correr los tests

Desde la carpeta `poc-nuget/pos-backend`:

```bash
dotnet test --logger "console;verbosity=normal"
```

Resultado esperado: **8 tests passed** (SyncOutboxEntry, InProcessEventBus, Packaging).

---

## Demo del NuGet dual-mode

> Esta demo demuestra que al cambiar la lógica de negocio en un módulo,
> empaquetarlo como NuGet, y reconstruir ambos hosts, **el cambio se
> refleja en LocalPOS y CloudHub sin tocar el código de los hosts**.

### Escenario: agregar validación de precio mínimo al módulo de Productos

Actualmente se puede crear un producto con precio 0 o negativo. Vamos a agregar
una regla de negocio al módulo `Pos.Products.Core` y ver cómo se propaga.

---

### Paso 1 — Verificar el comportamiento actual (sin validación)

Con ambos hosts corriendo:

```bash
# En LocalPOS: crear producto con precio 0 → funciona
curl -s -X POST http://localhost:5100/api/products \
  -H "Content-Type: application/json" \
  -d '{"name": "Test gratis", "price": 0, "categoryId": "<ID_CATEGORIA>"}' | jq .
# Respuesta: HTTP 201 — producto creado sin problema
```

### Paso 2 — Detener ambos hosts

Ir a cada terminal donde corren LocalPOS y CloudHub y presionar `Ctrl+C`.

### Paso 3 — Modificar la lógica de negocio en el módulo

Abrir `src/Modules/Pos.Products.Core/ProductsModule.cs` y buscar el endpoint de creación:

```csharp
products.MapPost("/", async (CreateProductRequest request, IProductRepository repo, CancellationToken ct) =>
{
    var product = await repo.CreateAsync(request, ct);
    return Results.Created($"/api/products/{product.Id}", product);
});
```

Agregar la validación de precio **antes** del `CreateAsync`:

```csharp
products.MapPost("/", async (CreateProductRequest request, IProductRepository repo, CancellationToken ct) =>
{
    if (request.Price <= 0)
        return Results.BadRequest(new { error = "El precio debe ser mayor a cero." });

    var product = await repo.CreateAsync(request, ct);
    return Results.Created($"/api/products/{product.Id}", product);
});
```

> **Nota:** Este cambio está en el módulo `Pos.Products.Core`, NO en los hosts.

### Paso 4 — Empaquetar el módulo modificado como NuGet

```bash
dotnet pack --configuration Release
```

Verificar que se regeneraron los paquetes:

```bash
ls -la artifacts/nupkg/Pos.Products.Core.*
# Pos.Products.Core.0.1.0-local.nupkg  (con timestamp reciente)
```

### Paso 5 — Reconstruir ambos hosts consumiendo los NuGet actualizados

```bash
# Limpiar cache para forzar que tome el paquete nuevo
dotnet nuget locals all --clear

# Construir usando los paquetes NuGet (no ProjectReference)
dotnet build -p:UseProjectReference=false
```

> **Esto es lo clave:** los hosts no fueron modificados. Solo se cambió el módulo.

### Paso 6 — Levantar ambos hosts de nuevo

Terminal 1:
```bash
dotnet run --project src/Hosts/Pos.Host.CloudHub
```

Terminal 2:
```bash
dotnet run --project src/Hosts/Pos.Host.LocalPOS
```

### Paso 7 — Verificar que AMBOS hosts tienen la nueva validación

```bash
# LocalPOS: intentar crear producto con precio 0
curl -s -X POST http://localhost:5100/api/products \
  -H "Content-Type: application/json" \
  -d '{"name": "Test gratis", "price": 0, "categoryId": "<ID_CATEGORIA>"}' | jq .
# Respuesta: { "error": "El precio debe ser mayor a cero." }

# CloudHub: mismo cambio, sin haber tocado el host
curl -s -X POST http://localhost:5200/api/products \
  -H "Content-Type: application/json" \
  -d '{"name": "Test gratis", "price": 0, "categoryId": "<ID_CATEGORIA>"}' | jq .
# Respuesta: { "error": "El precio debe ser mayor a cero." }

# Verificar que con precio válido sigue funcionando
curl -s -X POST http://localhost:5100/api/products \
  -H "Content-Type: application/json" \
  -d '{"name": "Laptop SIESA", "price": 2500000, "categoryId": "<ID_CATEGORIA>"}' | jq .
# Respuesta: HTTP 201 (creado correctamente)
```

### Resultado

| | Antes | Después |
|--|-------|---------|
| Código modificado | — | Solo `Pos.Products.Core` |
| Hosts modificados | — | **Ninguno** |
| LocalPOS rechaza precio ≤ 0 | No | **Sí** |
| CloudHub rechaza precio ≤ 0 | No | **Sí** |

**Una sola base de código → un `dotnet pack` → ambos despliegues actualizados.**

---

### Referencia: modo desarrollo vs CI/CD

| Modo | Flag | Uso |
|------|------|-----|
| Desarrollo | `UseProjectReference=true` (default) | `dotnet build` — referencia directa, permite debuggear |
| CI/CD | `UseProjectReference=false` | `dotnet build -p:UseProjectReference=false` — consume NuGet |

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
| `Conflict. The container name "/pos-postgres" is already in use` | Contenedor anterior no limpiado | `docker rm -f pos-postgres && docker compose up -d` |
| `Connection refused :5432` | PostgreSQL no terminó de iniciar | Esperar 10s y reintentar, verificar con `docker compose ps` |
| `Address already in use :5100` | Puerto ocupado | `lsof -i :5100` para encontrar el PID, `kill <PID>` |
| `Address already in use :5200` | Puerto ocupado | `lsof -i :5200` para encontrar el PID, `kill <PID>` |
| `NU1103: Unable to find a stable package` | Los `.nupkg` son prerelease | Ya corregido: `.csproj` usan `Version="*-*"` |
| `Failed to sync Product ... InternalServerError` | La categoría no existe en el cloud | Esperar ~10s entre crear categoría y producto |
| `relation "products.products" does not exist` | CloudHub no arrancó o DB no fue limpiada | `docker compose down -v && docker compose up -d` + reiniciar hosts |
| `column "EntityType" does not exist` | DB tiene columnas PascalCase de ejecución anterior | `docker compose down -v && docker compose up -d` + reiniciar hosts |
| Build falla con `TreatWarningsAsErrors` | Warnings en el código | Revisar la salida del build |
| `jq: command not found` | jq no instalado | `brew install jq` (opcional, la demo funciona sin él) |
