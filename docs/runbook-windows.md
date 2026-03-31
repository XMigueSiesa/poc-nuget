# Runbook — Ejecución en Windows

> POC NuGet POS · Guía paso a paso para desarrollo y demo

---

## Requisitos

| Herramienta | Versión mínima | Verificar en PowerShell |
|-------------|---------------|------------------------|
| .NET SDK | 10.0 | `dotnet --version` |
| Docker Desktop | 4.x | `docker --version` |
| Git | cualquiera | `git --version` |
| Windows | 10 (build 19041) o 11 | — |

> **Importante:** Docker Desktop en Windows requiere **WSL2**. Al instalar Docker Desktop, elegir la opción WSL2 cuando lo solicite (no Hyper-V).

### Instalar prerequisites

**Opción A — winget (recomendado, viene con Windows 11)**

```powershell
winget install Microsoft.DotNet.SDK.10
winget install Docker.DockerDesktop
winget install Git.Git
```

**Opción B — manual**

- .NET 10 SDK: https://dot.net/download
- Docker Desktop: https://www.docker.com/products/docker-desktop
- Git: https://git-scm.com/download/win

### Verificar instalaciones

Abrir **Windows Terminal** o **PowerShell** y ejecutar:

```powershell
dotnet --version    # debe mostrar 10.x.x
docker --version    # debe mostrar 4.x.x o superior
git --version
```

### Terminal recomendada

Instalar **Windows Terminal** desde Microsoft Store — permite múltiples pestañas y es mucho más cómoda que CMD o PowerShell clásico.

**No usar CMD** — algunos comandos del tutorial no funcionan en CMD.

---

## Estructura del proyecto

```
poc-nuget\
├── docs\                           ← documentación
├── pos-backend\
│   ├── docker-compose.yml          ← PostgreSQL 17
│   ├── docker\
│   │   └── init-databases.sql      ← crea pos_local y pos_cloud
│   ├── Directory.Build.props       ← flag UseProjectReference (dual-mode NuGet)
│   ├── src\
│   │   ├── Shared\
│   │   │   └── Pos.SharedKernel\   ← contratos e infraestructura compartida
│   │   ├── Modules\
│   │   │   ├── Pos.Orders.*        ← módulo de órdenes (Contracts + Core)
│   │   │   ├── Pos.Products.*      ← módulo de productos (Contracts + Core)
│   │   │   └── Pos.Payments.*      ← módulo de pagos (Contracts + Core)
│   │   └── Hosts\
│   │       ├── Pos.Host.CloudHub\  ← sistema central (todos los módulos)
│   │       └── Pos.Host.LocalPOS\  ← tienda física (subset + sincronización)
│   └── tests\
│       └── Pos.Tests\              ← tests unitarios
```

---

## Ejecución paso a paso

### 1. Clonar y posicionarse

```powershell
git clone <repo-url>
cd poc-nuget\pos-backend
```

### 2. Levantar la base de datos

```powershell
docker compose up -d
```

Verificar que esté saludable:

```powershell
docker compose ps
# Estado esperado: pos-postgres   Up (healthy)
```

El script `init-databases.sql` crea automáticamente dos bases:
- `pos_local` — usada por **LocalPOS**
- `pos_cloud` — usada por **CloudHub**

### 3. Levantar el CloudHub

Abrir una pestaña nueva en Windows Terminal (`Ctrl+Shift+T`):

```powershell
dotnet run --project src\Hosts\Pos.Host.CloudHub
```

Esperar hasta ver en los logs:

```
info: Migrations complete for ProductsDbContext
info: Migrations complete for OrdersDbContext
info: Migrations complete for PaymentsDbContext
info: Now listening on: http://0.0.0.0:5200
```

### 4. Levantar el LocalPOS

Abrir otra pestaña nueva en Windows Terminal:

```powershell
dotnet run --project src\Hosts\Pos.Host.LocalPOS
```

Esperar hasta ver:

```
info: Migrations complete for SyncDbContext
info: Sync worker started. Polling every 10s to http://localhost:5200
info: Now listening on: http://0.0.0.0:5100
```

### 5. Verificar que ambos están corriendo

Abrir en el navegador:

- http://localhost:5200 → debe mostrar `"service": "POS Cloud Hub"`
- http://localhost:5100 → debe mostrar `"service": "POS Local Store"`

O desde PowerShell:

```powershell
Invoke-RestMethod http://localhost:5200
Invoke-RestMethod http://localhost:5100
```

---

## Explorar los APIs

Abrir en el navegador:

| Host | URL | Puerto |
|------|-----|--------|
| CloudHub | http://localhost:5200/scalar/v1 | 5200 |
| LocalPOS | http://localhost:5100/scalar/v1 | 5100 |

> Para la demo se recomienda usar **Scalar UI en el navegador** — es la forma más cómoda en Windows ya que no requiere instalar herramientas adicionales como `curl` o `jq`.

---

## Demo del Outbox Pattern

### Opción A — Desde Scalar UI (recomendado)

1. Abrir http://localhost:5100/scalar/v1
2. Ir a `POST /api/products/categories` → crear una categoría y copiar el `id`
3. Ir a `POST /api/products` → crear un producto con ese `categoryId`
4. Verificar la sincronización con los comandos de base de datos (ver abajo)

### Opción B — Desde PowerShell

```powershell
# Crear categoría
$cat = Invoke-RestMethod -Uri http://localhost:5100/api/categories `
  -Method Post `
  -ContentType "application/json" `
  -Body '{"name": "Electronica"}'

$catId = $cat.id
Write-Host "Category ID: $catId"

# Crear producto
$body = @{
  name        = "Laptop SIESA Pro"
  description = "Laptop para demo"
  categoryId  = $catId
  price       = 2500000
} | ConvertTo-Json

$product = Invoke-RestMethod -Uri http://localhost:5100/api/products `
  -Method Post `
  -ContentType "application/json" `
  -Body $body

Write-Host "Product ID: $($product.id)"
```

### Verificar el outbox (antes de sincronizar)

```powershell
docker exec -it pos-postgres psql -U pos -d pos_local `
  -c "SELECT entity_type, entity_id, created_at, synced_at FROM sync.outbox_entries ORDER BY created_at DESC LIMIT 5;"
```

`synced_at` debe ser `NULL` — registro pendiente de sincronización.

### Esperar el ciclo del worker (~10 segundos)

Observar los logs del LocalPOS:

```
info: Found 1 pending sync entries
info: Synced Product 01JXXXXXXXXX to cloud
```

### Verificar que llegó al CloudHub

```powershell
docker exec -it pos-postgres psql -U pos -d pos_cloud `
  -c "SELECT id, name, price FROM products.products ORDER BY created_at DESC LIMIT 5;"
```

### Confirmar que el outbox quedó marcado

```powershell
docker exec -it pos-postgres psql -U pos -d pos_local `
  -c "SELECT entity_id, synced_at FROM sync.outbox_entries ORDER BY created_at DESC LIMIT 3;"
```

`synced_at` debe tener un timestamp — sincronización completa.

> **Nota sobre el backtick:** En PowerShell, el carácter `` ` `` (backtick) es el equivalente al `\` de bash para continuar un comando en la siguiente línea.

---

## Correr los tests

```powershell
cd C:\ruta\poc-nuget\pos-backend
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

```powershell
# En LocalPOS: crear producto con precio 0 → funciona
Invoke-RestMethod -Method POST -Uri http://localhost:5100/api/products `
  -ContentType "application/json" `
  -Body '{"name": "Test gratis", "price": 0, "categoryId": "<ID_CATEGORIA>"}'
# Respuesta: 201 (creado sin problema)

# En CloudHub: lo mismo
Invoke-RestMethod -Method POST -Uri http://localhost:5200/api/products `
  -ContentType "application/json" `
  -Body '{"name": "Test gratis", "price": 0, "categoryId": "<ID_CATEGORIA>"}'
# Respuesta: 201
```

Ambos hosts permiten precio 0 porque el módulo no tiene esa validación.

### Paso 2 — Detener ambos hosts

Ir a cada terminal donde corren LocalPOS y CloudHub y presionar `Ctrl+C`.

### Paso 3 — Modificar la lógica de negocio en el módulo

Abrir `src\Modules\Pos.Products.Core\ProductsModule.cs` y buscar el endpoint de creación:

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

```powershell
dotnet pack --configuration Release
```

Verificar que se regeneraron los paquetes:

```powershell
dir artifacts\nupkg\Pos.Products.Core.*
# Pos.Products.Core.0.1.0-local.nupkg  (con timestamp reciente)
```

### Paso 5 — Reconstruir ambos hosts consumiendo los NuGet actualizados

```powershell
# Limpiar cache para forzar que tome el paquete nuevo
dotnet nuget locals all --clear

# Construir usando los paquetes NuGet (no ProjectReference)
dotnet build -p:UseProjectReference=false
```

> **Esto es lo clave:** los hosts `Pos.Host.LocalPOS` y `Pos.Host.CloudHub` no
> fueron modificados. Solo se cambió el módulo y se re-empaquetó.

### Paso 6 — Levantar ambos hosts de nuevo

Terminal 1:
```powershell
dotnet run --project src\Hosts\Pos.Host.CloudHub
```

Terminal 2:
```powershell
dotnet run --project src\Hosts\Pos.Host.LocalPOS
```

### Paso 7 — Verificar que AMBOS hosts tienen la nueva validación

```powershell
# LocalPOS: intentar crear producto con precio 0
Invoke-RestMethod -Method POST -Uri http://localhost:5100/api/products `
  -ContentType "application/json" `
  -Body '{"name": "Test gratis", "price": 0, "categoryId": "<ID_CATEGORIA>"}'
# Respuesta: 400 { "error": "El precio debe ser mayor a cero." }

# CloudHub: mismo cambio, sin haber tocado el host
Invoke-RestMethod -Method POST -Uri http://localhost:5200/api/products `
  -ContentType "application/json" `
  -Body '{"name": "Test gratis", "price": 0, "categoryId": "<ID_CATEGORIA>"}'
# Respuesta: 400 { "error": "El precio debe ser mayor a cero." }

# Verificar que con precio válido sigue funcionando
Invoke-RestMethod -Method POST -Uri http://localhost:5100/api/products `
  -ContentType "application/json" `
  -Body '{"name": "Laptop SIESA", "price": 2500000, "categoryId": "<ID_CATEGORIA>"}'
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

## Alternativa: Visual Studio 2022 o Rider

Si el equipo usa un IDE con UI, se puede correr todo sin terminal:

**Visual Studio 2022:**
1. Abrir `pos-backend.sln`
2. Click derecho en la solución → *Set Startup Projects*
3. Seleccionar *Multiple startup projects*
4. Poner `Pos.Host.CloudHub` y `Pos.Host.LocalPOS` en *Start*
5. Presionar `F5`

**JetBrains Rider:**
1. Abrir la carpeta `pos-backend`
2. Run → *Edit Configurations*
3. Crear una configuración *Compound*
4. Agregar `Pos.Host.CloudHub` y `Pos.Host.LocalPOS`
5. Ejecutar la configuración compound

---

## Limpieza

```powershell
# Bajar contenedores y eliminar volúmenes (datos)
docker compose down -v

# Limpiar artefactos de build
dotnet clean
Remove-Item -Recurse -Force artifacts\
```

---

## Solución de problemas

| Error | Causa probable | Solución |
|-------|---------------|----------|
| `docker: command not found` | Docker Desktop no está abierto | Buscar "Docker Desktop" en el menú Inicio y abrirlo |
| `Connection refused :5432` | PostgreSQL no terminó de iniciar | Esperar 10s y reintentar |
| `Error: address already in use :5100` | Puerto ocupado | `netstat -ano \| findstr :5100` → identificar el PID → `taskkill /PID <pid> /F` |
| `Error: address already in use :5200` | Puerto ocupado | `netstat -ano \| findstr :5200` |
| WSL2 no instalado | Docker Desktop falla al iniciar | Ejecutar en PowerShell admin: `wsl --install` → reiniciar |
| `Execution Policy` bloquea scripts | Restricción de seguridad de PowerShell | `Set-ExecutionPolicy -Scope CurrentUser RemoteSigned` |
| `NU1103: Unable to find a stable package` al hacer `dotnet build -p:UseProjectReference=false` | Los `.nupkg` locales son prerelease (`0.1.0-local`) y `Version="*"` solo busca estables | Ya corregido: los `.csproj` usan `Version="*-*"` que acepta prerelease |
| Build falla con `TreatWarningsAsErrors` | Warnings en el código | Revisar la salida del build y corregir |
