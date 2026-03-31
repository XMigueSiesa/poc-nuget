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
$cat = Invoke-RestMethod -Uri http://localhost:5100/api/products/categories `
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

## Demo del NuGet dual-mode

### Modo desarrollo (default) — ProjectReference

```powershell
# UseProjectReference=true por defecto en Directory.Build.props
dotnet build

# Referencia directa a los proyectos: se puede debuggear dentro del módulo
```

### Modo CI/CD — PackageReference

```powershell
# Primero empaquetar los módulos
dotnet pack --configuration Release

# Verificar que se generaron los .nupkg
dir artifacts\nupkg\

# Construir los hosts consumiendo los paquetes (simula CI/CD)
dotnet build -p:UseProjectReference=false
```

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
| Build falla con `TreatWarningsAsErrors` | Warnings en el código | Revisar la salida del build y corregir |
