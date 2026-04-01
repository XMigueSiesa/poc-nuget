# Diagramas de Arquitectura — SIESA POS

> Todos los diagramas usan sintaxis [Mermaid](https://mermaid.js.org/) y se renderizan
> directamente en GitHub, GitLab, Notion, y la mayoria de viewers Markdown.

---

## 1. Topologia del Sistema

Vista general: CloudHub en GCP Cloud Run, tiendas fisicas con LocalPOS on-premise.

```mermaid
graph TB
    subgraph Internet
        LB["Cloud Load Balancer<br/>HTTPS + managed SSL"]
    end

    subgraph GCP["GCP Project"]
        LB --> CR["Cloud Run<br/><b>pos-cloudhub</b><br/>autoscale 1-10<br/>port 8080"]
        CR -- "private IP<br/>VPC Connector" --> CSQL[("Cloud SQL<br/>PostgreSQL 17<br/>HA Regional<br/><i>pos_cloud</i>")]
        CR -. "reads" .-> SM["Secret Manager<br/>DB conn string<br/>JWT signing key"]
        CR -. "writes" .-> CL["Cloud Logging<br/>JSON stdout"]
        CR -. "exports" .-> CT["Cloud Trace<br/>+ Monitoring<br/>OTLP"]
        AR["Artifact Registry<br/><i>pos-images</i>"] -. "image pull" .-> CR
    end

    subgraph Tienda1["Tienda Fisica 1"]
        LP1["LocalPOS<br/>Windows Service<br/>o systemd"] --> PG1[("PostgreSQL 17<br/>local<br/><i>pos_local</i>")]
    end

    subgraph Tienda2["Tienda Fisica 2"]
        LP2["LocalPOS<br/>self-contained<br/>.NET 10"] --> PG2[("PostgreSQL 17<br/>local<br/><i>pos_local</i>")]
    end

    LP1 -- "HTTPS<br/>Bearer JWT" --> LB
    LP2 -- "HTTPS<br/>Bearer JWT" --> LB

    style GCP fill:#e8f5e9,stroke:#2e7d32
    style Tienda1 fill:#e3f2fd,stroke:#1565c0
    style Tienda2 fill:#e3f2fd,stroke:#1565c0
    style CR fill:#fff3e0,stroke:#e65100
    style CSQL fill:#fce4ec,stroke:#c62828
```

---

## 2. Arquitectura de Modulos (NuGet Dual-Mode)

Cada modulo se distribuye como paquete NuGet. Los hosts los componen sin conocer su implementacion interna.

```mermaid
graph LR
    subgraph SharedKernel["Pos.SharedKernel"]
        SK_Events["IEventBus"]
        SK_Sync["SyncOutboxEntry<br/>SyncDbContext"]
        SK_Auth["StoreCredential<br/>AuthDbContext"]
        SK_Health["OutboxBacklogHealthCheck"]
        SK_Otel["SyncMetrics<br/>OpenTelemetryExtensions"]
    end

    subgraph Modules["Modulos de Negocio"]
        direction TB
        PC["Pos.Products<br/>.Contracts + .Core"]
        OC["Pos.Orders<br/>.Contracts + .Core"]
        PAY["Pos.Payments<br/>.Contracts + .Core"]
    end

    subgraph Infra["Infraestructura"]
        MR["Pos.Infrastructure<br/>.Postgres<br/>MigrationRunner"]
    end

    subgraph Hosts["Hosts (Composicion)"]
        CH["Pos.Host<br/>.CloudHub<br/>:5200 / :8080"]
        LP["Pos.Host<br/>.LocalPOS<br/>:5100"]
    end

    PC --> SK_Events
    OC --> SK_Events
    PAY --> SK_Events

    CH --> PC & OC & PAY & MR & SK_Auth
    LP --> PC & OC & PAY & MR & SK_Sync

    subgraph DualMode["Dual-Mode Reference"]
        PR["ProjectReference<br/><i>dev: dotnet build</i>"]
        NR["PackageReference<br/><i>CI: dotnet build<br/>-p:UseProjectReference=false</i>"]
    end

    style SharedKernel fill:#f3e5f5,stroke:#6a1b9a
    style Modules fill:#e8eaf6,stroke:#283593
    style Hosts fill:#fff8e1,stroke:#f57f17
    style DualMode fill:#efebe9,stroke:#4e342e
```

---

## 3. Flujo de Sincronizacion (Outbox Pattern)

Datos creados en LocalPOS se sincronizan al CloudHub via HTTP push con backoff exponencial.

```mermaid
sequenceDiagram
    participant App as LocalPOS API
    participant DB as PostgreSQL Local
    participant Worker as SyncOutboxWorker
    participant CB as CircuitBreaker
    participant TP as TokenProvider
    participant Cloud as CloudHub

    Note over App,DB: 1. Escritura transaccional

    App->>DB: BEGIN
    App->>DB: INSERT INTO products.products (...)
    App->>DB: INSERT INTO sync.outbox_entries (entity_type, payload)
    App->>DB: COMMIT
    App-->>App: HTTP 201 Created

    Note over Worker,Cloud: 2. Polling cada 10s

    loop Cada 10 segundos
        Worker->>CB: IsOpen()?
        alt Circuit breaker OPEN
            CB-->>Worker: true (skip cycle)
        else Circuit breaker CLOSED / HALF-OPEN
            CB-->>Worker: false
            Worker->>DB: SELECT * FROM outbox_entries<br/>WHERE synced_at IS NULL<br/>AND dead_lettered_at IS NULL<br/>AND (next_retry_at IS NULL OR next_retry_at <= now)<br/>ORDER BY created_at LIMIT 50

            loop Para cada entry
                Worker->>TP: GetTokenAsync()
                TP-->>Worker: Bearer JWT (cached)
                Worker->>Cloud: POST /api/sync/{type}<br/>Authorization: Bearer xxx
                alt HTTP 2xx
                    Cloud-->>Worker: 200 OK
                    Worker->>DB: UPDATE SET synced_at = now
                    Worker->>CB: RecordSuccess()
                else HTTP 4xx/5xx o timeout
                    Cloud-->>Worker: error
                    Worker->>CB: RecordFailure()
                    alt retry_count < 5
                        Worker->>DB: UPDATE SET retry_count++,<br/>next_retry_at = now + 2^n sec,<br/>last_error = "..."
                    else retry_count >= 5
                        Worker->>DB: UPDATE SET dead_lettered_at = now
                        Note over Worker: Entry movida a dead-letter
                    end
                end
            end
        end
    end
```

---

## 4. Flujo de Autenticacion (OAuth2 Client Credentials)

Cada tienda tiene un `client_id` + `client_secret`. El CloudHub emite JWTs.

```mermaid
sequenceDiagram
    participant Admin as Administrador
    participant CH as CloudHub
    participant DB as AuthDbContext
    participant LP as LocalPOS
    participant TP as TokenProvider

    Note over Admin,DB: Paso 0 — Provisionar tienda (una vez)

    Admin->>CH: POST /api/admin/stores<br/>{"storeId":"bogota","storeName":"Tienda Bogota"}
    CH->>CH: Generar client_id (ULID)<br/>Generar client_secret (32 bytes base64)
    CH->>DB: INSERT store_credentials<br/>(client_secret_hash = BCrypt)
    CH-->>Admin: {"clientId":"01JX...","clientSecret":"abc123..."}
    Note over Admin: Guardar el secret — no se puede recuperar

    Note over LP,CH: Paso 1 — Obtener token (automatico)

    LP->>TP: GetTokenAsync()
    Note over TP: Cache vacio o token expira en < 5min
    TP->>CH: POST /api/auth/token<br/>{"client_id":"01JX...","client_secret":"abc123..."}
    CH->>DB: SELECT WHERE client_id = ?
    CH->>CH: BCrypt.Verify(secret, hash)
    CH->>CH: Generar JWT (HS256)<br/>sub=storeId, client_id, exp=60min
    CH-->>TP: {"access_token":"eyJ...","expires_in":3600}
    TP->>TP: Cache token + expiry
    TP-->>LP: "eyJ..."

    Note over LP,CH: Paso 2 — Sync con token

    LP->>CH: POST /api/sync/products<br/>Authorization: Bearer eyJ...
    CH->>CH: Validar JWT (firma, issuer, audience, expiry)
    CH->>CH: Policy "SyncEndpoint": requiere claim client_id
    CH-->>LP: 200 OK

    Note over LP,CH: Caso: Token expirado

    LP->>CH: POST /api/sync/products<br/>Authorization: Bearer eyJ...(expired)
    CH-->>LP: 401 Unauthorized
    LP->>TP: GetTokenAsync() — cache invalido
    TP->>CH: POST /api/auth/token (refresh)
    CH-->>TP: nuevo JWT
    LP->>CH: POST /api/sync/products<br/>Authorization: Bearer (nuevo)
    CH-->>LP: 200 OK
```

---

## 5. Pipeline CI/CD

Dos workflows en GitHub Actions: uno para CloudHub (push a main) y otro para LocalPOS (tags de release).

```mermaid
graph LR
    subgraph Trigger1["Push a main"]
        T1["git push origin main"]
    end

    subgraph CloudHubPipeline["deploy-cloudhub.yml"]
        direction TB
        TEST1["dotnet test"]
        PACK["dotnet pack<br/>NuGet 1.0.0-ci.N"]
        DOCKER["docker build<br/>-f CloudHub.Dockerfile"]
        PUSH["docker push<br/>Artifact Registry"]
        DEPLOY["gcloud run deploy<br/>pos-cloudhub"]

        TEST1 --> PACK --> DOCKER --> PUSH --> DEPLOY
    end

    subgraph Auth["GCP Auth"]
        WIF["Workload Identity<br/>Federation<br/>(sin service account keys)"]
    end

    T1 --> TEST1
    DOCKER -.-> WIF
    PUSH -.-> WIF
    DEPLOY -.-> WIF

    subgraph Trigger2["Push tag v*"]
        T2["git tag v1.0.0<br/>git push --tags"]
    end

    subgraph LocalPOSPipeline["release-localpos.yml"]
        direction TB
        TEST2["dotnet test"]
        BUILD_WIN["dotnet publish<br/>win-x64<br/>self-contained<br/>single-file"]
        BUILD_LIN["dotnet publish<br/>linux-x64<br/>self-contained<br/>single-file"]
        ZIP["Crear .zip"]
        RELEASE["GitHub Release<br/>+ assets .zip"]

        TEST2 --> BUILD_WIN & BUILD_LIN
        BUILD_WIN --> ZIP
        BUILD_LIN --> ZIP
        ZIP --> RELEASE
    end

    T2 --> TEST2

    style CloudHubPipeline fill:#e8f5e9,stroke:#2e7d32
    style LocalPOSPipeline fill:#e3f2fd,stroke:#1565c0
    style Auth fill:#fff3e0,stroke:#e65100
```

---

## 6. Resiliencia del Outbox

Estado de las entries y transiciones del circuit breaker.

```mermaid
stateDiagram-v2
    [*] --> Pending: INSERT (transaccional)

    Pending --> Synced: HTTP 2xx
    Pending --> RetryWait: HTTP error / timeout

    RetryWait --> Pending: next_retry_at reached<br/>(backoff: 2s, 4s, 8s, 16s, 32s...5min cap)
    RetryWait --> DeadLettered: retry_count >= MaxRetryCount (5)

    DeadLettered --> [*]: Requiere intervencion manual

    Synced --> [*]

    note right of Pending
        synced_at IS NULL
        dead_lettered_at IS NULL
        next_retry_at IS NULL or <= now
    end note

    note right of RetryWait
        retry_count > 0
        next_retry_at > now
        last_error = "..."
    end note

    note right of DeadLettered
        dead_lettered_at IS NOT NULL
        Alerta via OutboxBacklogHealthCheck
    end note
```

### Circuit Breaker

```mermaid
stateDiagram-v2
    [*] --> Closed

    Closed --> Open: N fallos consecutivos<br/>(default: 10)
    Open --> HalfOpen: Timeout expirado<br/>(default: 60s)
    HalfOpen --> Closed: Primer exito
    HalfOpen --> Open: Primer fallo

    note right of Closed
        Trafico normal
        Contador de fallos activo
    end note

    note right of Open
        Todo el trafico bloqueado
        Worker salta ciclos de sync
    end note

    note right of HalfOpen
        Permite 1 request de prueba
        Exito → Closed
        Fallo → Open de nuevo
    end note
```

---

## 7. Modelo de Datos

Schemas en PostgreSQL (tanto `pos_cloud` como `pos_local`).

```mermaid
erDiagram
    PRODUCTS_CATEGORIES {
        varchar(26) id PK
        varchar(200) name
        text description
        timestamptz created_at
    }

    PRODUCTS_PRODUCTS {
        varchar(26) id PK
        varchar(200) name
        text description
        varchar(26) category_id FK
        decimal(18_2) price
        int stock
        timestamptz created_at
    }

    ORDERS_ORDERS {
        varchar(26) id PK
        varchar(50) status
        decimal(18_2) total
        timestamptz created_at
        timestamptz closed_at
    }

    ORDERS_ORDER_LINES {
        varchar(26) id PK
        varchar(26) order_id FK
        varchar(26) product_id
        varchar(200) product_name
        decimal(18_2) unit_price
        int quantity
        decimal(18_2) line_total
    }

    PAYMENTS_PAYMENTS {
        varchar(26) id PK
        varchar(26) order_id FK
        varchar(30) method
        decimal(18_2) amount
        varchar(20) status
        varchar(100) transaction_id UK
        timestamptz created_at
    }

    SYNC_OUTBOX_ENTRIES {
        varchar(26) id PK
        varchar(100) entity_type
        varchar(100) entity_id
        text payload
        timestamptz created_at
        timestamptz synced_at
        int retry_count
        timestamptz next_retry_at
        varchar(2000) last_error
        timestamptz dead_lettered_at
    }

    AUTH_STORE_CREDENTIALS {
        varchar(26) id PK
        varchar(50) store_id UK
        varchar(100) client_id UK
        varchar(200) client_secret_hash
        varchar(200) store_name
        boolean is_active
        timestamptz created_at
    }

    PRODUCTS_CATEGORIES ||--o{ PRODUCTS_PRODUCTS : "category_id"
    ORDERS_ORDERS ||--o{ ORDERS_ORDER_LINES : "order_id"
    ORDERS_ORDERS ||--o{ PAYMENTS_PAYMENTS : "order_id"
```

> **Nota:** `sync.outbox_entries` solo existe en `pos_local`. `auth.store_credentials` solo existe en `pos_cloud`.

---

## 8. Observabilidad

Flujo de señales: logs, traces y metricas.

```mermaid
graph LR
    subgraph App["Aplicacion (.NET 10)"]
        LOG["ILogger<br/>JSON stdout"]
        TRACE["OpenTelemetry<br/>TracerProvider"]
        METRIC["OpenTelemetry<br/>MeterProvider"]
        CUSTOM["SyncMetrics<br/>entries_synced<br/>entries_failed<br/>duration_ms<br/>pending_entries"]
    end

    subgraph GCP["GCP (produccion)"]
        CL["Cloud Logging<br/>captura stdout"]
        CTR["Cloud Trace<br/>OTLP receiver"]
        CM["Cloud Monitoring<br/>OTLP receiver"]
        ALERT["Alert Policies<br/>5xx > 5%<br/>0 instancias"]
    end

    subgraph Dev["Desarrollo"]
        CONSOLE["Console<br/>(colored text)"]
        OTEL_CONSOLE["Console Exporter<br/>(traces + metrics)"]
    end

    LOG -- "Production" --> CL
    LOG -- "Development" --> CONSOLE
    TRACE -- "OTLP" --> CTR
    TRACE -- "dev" --> OTEL_CONSOLE
    METRIC -- "OTLP" --> CM
    METRIC -- "dev" --> OTEL_CONSOLE
    CUSTOM --> METRIC
    CM --> ALERT

    style App fill:#e8eaf6,stroke:#283593
    style GCP fill:#e8f5e9,stroke:#2e7d32
    style Dev fill:#fff8e1,stroke:#f57f17
```

---

## 9. Flujo de Despliegue de LocalPOS

Instalacion en tiendas fisicas (Windows o Linux).

```mermaid
graph TB
    subgraph GitHub["GitHub"]
        TAG["git tag v1.0.0"]
        REL["GitHub Release<br/>win-x64.zip<br/>linux-x64.zip"]
    end

    TAG --> REL

    subgraph Windows["Tienda (Windows)"]
        DL_WIN["Descargar win-x64.zip"]
        PS["install-windows.ps1<br/>-CloudBaseUrl https://...<br/>-ClientId 01JX...<br/>-ClientSecret abc..."]
        SVC_WIN["Windows Service<br/>'PosLocalPOS'<br/>Auto-Start"]
    end

    REL --> DL_WIN --> PS --> SVC_WIN

    subgraph Linux["Tienda (Linux)"]
        DL_LIN["Descargar linux-x64.zip"]
        UNZIP["Extraer a /opt/siesa/localpos"]
        ENV["Crear /etc/siesa/localpos.env<br/>AUTH__CLIENTID=...<br/>AUTH__CLIENTSECRET=..."]
        SYSTEMD["systemctl enable --now<br/>pos-localpos"]
    end

    REL --> DL_LIN --> UNZIP --> ENV --> SYSTEMD

    style GitHub fill:#f3e5f5,stroke:#6a1b9a
    style Windows fill:#e3f2fd,stroke:#1565c0
    style Linux fill:#e8f5e9,stroke:#2e7d32
```
