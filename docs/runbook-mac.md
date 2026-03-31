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

### 0. Limpiar estado anterior (si ya corriste la demo antes)

```bash
docker rm -f pos-postgres 2>/dev/null || true
docker compose down -v 2>/dev/null || true
```

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
dotnet run --project src/Hosts/Pos.Host.CloudHub
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
dotnet run --project src/Hosts/Pos.Host.LocalPOS
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
curl -s -X POST http://localhost:5100/api/categories \
  -H "Content-Type: application/json" \
  -d '{"name": "Electronica"}' | jq .
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

## Demo del NuGet dual-mode — Cambiar lógica → empaquetar → ambos hosts actualizados

> Esta es la demo principal del POC. Demuestra que al cambiar la lógica de negocio
> en un módulo, empaquetarlo como NuGet, y reconstruir ambos hosts, **el cambio se
> refleja en LocalPOS y CloudHub sin tocar el código de los hosts**.

### Escenario: agregar validación de precio mínimo al módulo de Productos

Actualmente se puede crear un producto con precio 0 o negativo. Vamos a agregar
una regla de negocio al módulo `Pos.Products.Core` y ver cómo se propaga.

---

### Paso 1 — Verificar el comportamiento actual (sin validación)

Con ambos hosts corriendo (pasos 3 y 4 de arriba):

```bash
# En LocalPOS: crear producto con precio 0 → funciona
curl -s -X POST http://localhost:5100/api/products \
  -H "Content-Type: application/json" \
  -d '{"name": "Test gratis", "price": 0, "categoryId": "<ID_CATEGORIA>"}' | jq .status
# Respuesta: 201 (creado sin problema)

# En CloudHub: lo mismo
curl -s -X POST http://localhost:5200/api/products \
  -H "Content-Type: application/json" \
  -d '{"name": "Test gratis", "price": 0, "categoryId": "<ID_CATEGORIA>"}' | jq .status
# Respuesta: 201
```

Ambos hosts permiten precio 0 porque el módulo no tiene esa validación.

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
> Los hosts solo consumen el módulo como paquete NuGet.

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

> **Esto es lo clave:** los hosts `Pos.Host.LocalPOS` y `Pos.Host.CloudHub` no
> fueron modificados. Solo se cambió el módulo y se re-empaquetó.

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
# Respuesta: 400 { "error": "El precio debe ser mayor a cero." }

# CloudHub: mismo cambio, sin haber tocado el host
curl -s -X POST http://localhost:5200/api/products \
  -H "Content-Type: application/json" \
  -d '{"name": "Test gratis", "price": 0, "categoryId": "<ID_CATEGORIA>"}' | jq .
# Respuesta: 400 { "error": "El precio debe ser mayor a cero." }

# Verificar que con precio válido sigue funcionando
curl -s -X POST http://localhost:5100/api/products \
  -H "Content-Type: application/json" \
  -d '{"name": "Laptop SIESA", "price": 2500000, "categoryId": "<ID_CATEGORIA>"}' | jq .
# Respuesta: 201 (creado correctamente)
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
| Desarrollo | `UseProjectReference=true` (default) | `dotnet build` — referencia directa a proyectos, permite debuggear dentro del módulo |
| CI/CD | `UseProjectReference=false` | `dotnet build -p:UseProjectReference=false` — consume paquetes NuGet, simula despliegue real |

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
| `Conflict. The container name "/pos-postgres" is already in use` | Contenedor de sesión anterior no fue limpiado | `docker rm -f pos-postgres && docker compose up -d` |
| `Connection refused :5432` | PostgreSQL no terminó de iniciar | Esperar 10s y reintentar |
| `Address already in use :5100` | Puerto ocupado por otro proceso | `lsof -i :5100` para ver qué proceso es |
| `Address already in use :5200` | Puerto ocupado | `lsof -i :5200` |
| `NU1103: Unable to find a stable package` al hacer `dotnet build -p:UseProjectReference=false` | Los `.nupkg` locales son prerelease (`0.1.0-local`) y `Version="*"` solo busca estables | Ya corregido: los `.csproj` usan `Version="*-*"` que acepta prerelease |
| Build falla con `TreatWarningsAsErrors` | Warnings en el código | Revisar la salida del build y corregir |
| `jq: command not found` | jq no instalado | `brew install jq` |
