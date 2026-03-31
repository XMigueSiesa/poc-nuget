# ADR-001: Modular Monolith con Distribucion NuGet

**Estado**: Aceptado
**Fecha**: 2026-03-26
**Contexto**: POC POS Backend SIESA

## Contexto

SIESA necesita que el mismo codigo de negocio (Orders, Products, Payments) se ejecute en:
- Un **hub local** en cada tienda fisica (todos los modulos juntos, funciona offline)
- **Servicios cloud** individuales (un modulo por servicio)

El equipo de desarrollo debe mantener **una sola base de codigo** y que los cambios se propaguen a todos los contextos de despliegue sin duplicacion.

## Decision

Adoptar una arquitectura de **Modular Monolith** donde cada modulo se puede empaquetar como **paquete NuGet** y consumir desde diferentes hosts.

El mecanismo central es un flag de MSBuild `UseProjectReference` en `Directory.Build.props`:
- `true` (default): los hosts referencian modulos via `ProjectReference` (desarrollo local con debug)
- `false` (CI/CD): los hosts consumen modulos via `PackageReference` (paquetes NuGet publicados)

Cada modulo sigue el patron **Contracts/Core split**:
- `Pos.X.Contracts`: interfaces, DTOs, requests (paquete liviano para consumidores)
- `Pos.X.Core`: implementacion EF, endpoints, repositorios

## Alternativas Consideradas

### 1. Microservicios desde el inicio
- **Descartado**: overhead de infraestructura (message broker, service mesh, API gateway) desproporcionado para el tamano actual del equipo.
- Riesgo de distributed monolith si no se hace bien.

### 2. Shared library sin modularidad
- **Descartado**: acopla todo el codigo; un cambio en Payments requiere recompilar Orders.
- No permite despliegue selectivo de modulos.

### 3. Git submodules
- **Descartado**: complejidad operativa (merge conflicts, versionado manual).
- NuGet resuelve el versionado de manera nativa.

### 4. Monorepo con build condicional
- **Parcialmente adoptado**: el monorepo se usa para desarrollo, pero la distribucion es via NuGet.

## Consecuencias

### Positivas
- Una sola base de codigo para todos los contextos
- Debug directo en desarrollo (ProjectReference)
- Versionado semantico de modulos via NuGet
- Path de migracion gradual a microservicios (cada modulo ya es independiente)
- CI/CD valida que los hosts compilan con paquetes NuGet

### Negativas
- Requiere CI/CD para pack+push (GitHub Actions)
- Version drift: hosts pueden referenciar versiones incompatibles
- Complejidad en el `Directory.Build.props` y los `Choose` blocks de cada .csproj
- Los desarrolladores deben entender el mecanismo dual

### Riesgos
- Si un modulo cambia su interfaz publica (Contracts), todos los consumidores deben actualizarse
- El feed NuGet es un punto unico de fallo para despliegues
