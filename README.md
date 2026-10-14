# PharmaFlow Enterprise Backend

Enterprise-grade microservices backend for the **PharmaFlow Pharmacy Management System**, built with **ASP.NET Core 8.0 LTS**, **YARP (Yet Another Reverse Proxy)**, **PostgreSQL 16**, **RabbitMQ**, and **Azurite Blob Storage**.

---

## 1. Architecture Overview

PharmaFlow employs a coarse-grained microservices architecture adhering to Bounded Context principles. Browser clients communicate exclusively with the **API Gateway / BFF**, which terminates browser sessions, enforces anti-CSRF protection, and proxies authenticated requests to downstream services using Bearer JWT tokens.

```
                    Browser Clients (Staff Web & Storefront)
                                      │
                                      ▼
             ┌──────────────────────────────────────────────────┐
             │       PharmaFlow.Gateway (YARP BFF :5000)        │
             │   - HttpOnly Session Cookies                     │
             │   - Anti-CSRF Token Enforcement (X-XSRF-TOKEN)   │
             │   - JWT Bearer Header Injection Downstream       │
             └────────────────────────┬─────────────────────────┘
                                      │
         ┌──────────────┬─────────────┼──────────────┬──────────────┬─────────────┐
         ▼              ▼             ▼              ▼              ▼             ▼
   ┌───────────┐  ┌───────────┐ ┌───────────┐  ┌───────────┐  ┌───────────┐ ┌───────────┐
   │ Identity  │  │Operations │ │ Commerce  │  │ Workforce │  │ Documents │ │ Reporting │
   │   :5001   │  │   :5002   │ │   :5003   │  │   :5004   │  │   :5005   │ │   :5006   │
   └─────┬─────┘  └─────┬─────┘ └─────┬─────┘  └─────┬─────┘  └─────┬─────┘ └─────┬─────┘
         ▼              ▼             ▼              ▼              ▼             ▼
   [DB: identity] [DB: ops]   [DB: comm]    [DB: work]     [DB: docs]    [DB: report]
         └──────────────┴─────────────┴──────────────┴──────────────┴─────────────┘
                                  PostgreSQL 16 (Port 5432)
```

---

## 2. Microservice & Port Matrix

| Project | Port | Database / Storage | Responsibility |
| :--- | :---: | :--- | :--- |
| **PharmaFlow.Gateway** | `5000` | N/A | YARP Reverse proxy, HttpOnly cookie sessions, Anti-CSRF verification. |
| **PharmaFlow.Identity.Api** | `5001` | `pharmaflow_identity` | Staff & customer credentials, PBKDF2 hashing, roles, bootstrap lockout. |
| **PharmaFlow.Operations.Api** | `5002` | `pharmaflow_operations` | Branches, registers, batch inventory (FEFO), POS checkout, returns, purchasing. |
| **PharmaFlow.Commerce.Api** | `5003` | `pharmaflow_commerce` | Customer accounts, public catalogue projection, click-and-collect orders. |
| **PharmaFlow.Workforce.Api** | `5004` | `pharmaflow_workforce` | Staff schedules, overlap detection, attendance clocking, leave management. |
| **PharmaFlow.Documents.Api** | `5005` | `pharmaflow_documents` + Blob | Azurite/Azure Blob storage, `%PDF` magic bytes validation, virus scan lifecycle. |
| **PharmaFlow.Reporting.Api** | `5006` | `pharmaflow_reporting` | Sales summaries, COGS-based gross profit, shift variance, inventory valuation. |

---

## 3. Prerequisites

Ensure the following tools are installed on your host machine:

- **.NET 8.0 SDK** (or .NET 10 with roll-forward enabled):
  ```bash
  dotnet --version
  ```
- **Docker Desktop** (for PostgreSQL, RabbitMQ, Azurite, MailDev):
  - Ensure Docker Desktop is started and the engine icon is green.

---

## 4. How to Navigate Between Repositories in Terminal

PharmaFlow is organized into two independent repositories side-by-side:
```
PMS New/
├── pharmaflow-backend/   <-- You are here
└── pharmaflow-frontend/  <-- Frontend repository
```

### Switching to Frontend from Backend:
```powershell
# From pharmaflow-backend directory:
cd ..\pharmaflow-frontend
```

### Switching back to Backend from Frontend:
```powershell
# From pharmaflow-frontend directory:
cd ..\pharmaflow-backend
```

### Terminal Tips:
- **Use Tab Completion**: Type `cd ..\pharm` and hit `Tab` to autocomplete.
- **Open Split Terminals in VS Code / Antigravity**: Use `Ctrl + Shift + 5` (Split Terminal) or click the `+` button in the terminal panel to keep one terminal inside `pharmaflow-backend` and another in `pharmaflow-frontend`.

---

## 5. How to Run the Backend

### Step 1: Start Infrastructure Containers (Docker)

Navigate to the local infrastructure directory:
```powershell
cd infra\local
docker compose up -d postgres rabbitmq azurite maildev
```

Verify that containers are healthy:
```powershell
docker compose ps
```
Containers started:
- **PostgreSQL 16**: `localhost:5432`
- **RabbitMQ Management**: `http://localhost:15672` (User: `pharmaflow`, Pass: `rabbit_dev_pwd`)
- **Azurite Blob Storage**: `http://localhost:10000`
- **MailDev UI**: `http://localhost:1080`

### Step 2: Build the Solution

From the `pharmaflow-backend` root folder:
```powershell
dotnet build PharmaFlow.slnx
```

### Step 3: Run the Services

You can run services individually in separate terminal windows:

#### Terminal 1 — Gateway (Port 5000):
```powershell
dotnet run --project gateway\PharmaFlow.Gateway --urls "http://localhost:5000"
```

#### Terminal 2 — Identity Service (Port 5001):
```powershell
dotnet run --project services\identity\PharmaFlow.Identity.Api --urls "http://localhost:5001"
```

#### Terminal 3 — Operations Service (Port 5002):
```powershell
dotnet run --project services\operations\PharmaFlow.Operations.Api --urls "http://localhost:5002"
```

#### Terminal 4 — Commerce Service (Port 5003):
```powershell
dotnet run --project services\commerce\PharmaFlow.Commerce.Api --urls "http://localhost:5003"
```

#### Terminal 5 — Workforce Service (Port 5004):
```powershell
dotnet run --project services\workforce\PharmaFlow.Workforce.Api --urls "http://localhost:5004"
```

#### Terminal 6 — Documents Service (Port 5005):
```powershell
dotnet run --project services\documents\PharmaFlow.Documents.Api --urls "http://localhost:5005"
```

#### Terminal 7 — Reporting Service (Port 5006):
```powershell
dotnet run --project services\reporting\PharmaFlow.Reporting.Api --urls "http://localhost:5006"
```

> **Automated Startup Alternative:** You can also use the root orchestration script from the workspace root:
> ```powershell
> .\scripts\start-services.ps1 -IncludeSecondaryServices
> ```

---

## 6. Manual Setup & Initial Configuration

### First-Time SuperAdmin Bootstrap
PharmaFlow follows a zero-default-password security policy. The first administrator account must be bootstrapped:

1. Open your browser to the Staff Web app: `http://localhost:4200/bootstrap`
2. Enter the **Master Bootstrap Token**:
   ```
   PharmaFlowBootstrapKey2026!
   ```
   *(Configured in `services\identity\PharmaFlow.Identity.Api\appsettings.json` under `Bootstrap:MasterBootstrapKey`)*
3. Supply the initial administrator credentials:
   - **Full Name**: e.g., `Lead System Administrator`
   - **Username**: `admin`
   - **Email**: `admin@pharmaflow.local`
   - **Password**: Your secure password (minimum 8 characters)
4. Submit. The system registers the SuperAdmin account, assigns the `SuperAdmin` role, and permanently locks the bootstrap endpoint.
5. You can now log in at `http://localhost:4200/login`.

---

## 7. Running Unit Tests

The backend includes test coverage for FEFO batch allocation, excess return quarantine enforcement, workforce cross-branch schedule conflict detection, file magic bytes verification, and PBKDF2 password security.

Run all tests from the `pharmaflow-backend` root:
```powershell
dotnet test PharmaFlow.slnx
```

---

## 8. Database Migrations

Initial EF Core migrations are committed under `Migrations/` in each service. When a service connects to PostgreSQL on startup, EF Core automatically applies pending migrations:

If you modify models and need to generate a new migration manually:
```powershell
# Example for Operations service:
dotnet ef migrations add AddNewFeature --project services\operations\PharmaFlow.Operations.Api --startup-project services\operations\PharmaFlow.Operations.Api
dotnet ef database update --project services\operations\PharmaFlow.Operations.Api
```
