# PharmaFlow Enterprise Architecture Documentation

## 1. System Overview & Technology Stack
PharmaFlow is an enterprise pharmacy management system built with coarse-grained microservices running on ASP.NET Core 8.0 LTS and PostgreSQL 16.

### Core Services
1. **PharmaFlow.Gateway** (`http://localhost:5000`):
   - Reverse proxy powered by YARP.
   - BFF (Backend-for-Frontend) security architecture managing HttpOnly session cookies (`pharmaflow_staff_session` and `pharmaflow_customer_session`).
   - Anti-CSRF protection verifying `X-XSRF-TOKEN` header on mutating requests.
   - Prevents storing JWTs in browser localStorage.

2. **PharmaFlow.Identity.Api** (`http://localhost:5001`):
   - Database: `pharmaflow_identity` (Owner: `identity_user`).
   - Roles, permissions, staff & customer user credentials.
   - PBKDF2 with SHA-256 password hashing (100,000 iterations).
   - Secure first-admin bootstrap without static default passwords.
   - Account lockout (5 failed attempts locks account for 15 minutes).
   - JWT access token generation and refresh token rotation.

3. **PharmaFlow.Operations.Api** (`http://localhost:5002`):
   - Database: `pharmaflow_operations` (Owner: `operations_user`).
   - Organization, branches, registers, cashier shifts with float accounting.
   - Product catalogue with barcodes, pack conversions, price history.
   - Suppliers, purchase orders, partial goods receipts.
   - Batch inventory with strict FEFO allocation and movement ledger.
   - Fast keyboard-friendly POS with sequential invoice generation (`INV-YYYYMMDD-XXXXX`).
   - Returns and refunds routing returned stock to Quarantine.
   - Dispensing authorization with max quantity limits.
   - Local transactional boundary: Inventory consumption, shift cash totals, sale posting, and prescription limits occur in one database transaction.

4. **PharmaFlow.Commerce.Api** (`http://localhost:5003`):
   - Database: `pharmaflow_commerce` (Owner: `commerce_user`).
   - Customer accounts and delivery profiles.
   - Public catalogue projection.
   - Click-and-collect customer order requests.

5. **PharmaFlow.Workforce.Api** (`http://localhost:5004`):
   - Database: `pharmaflow_workforce` (Owner: `workforce_user`).
   - Employee records separate from login identity.
   - Attendance clocking with manager correction audit logs.
   - Work scheduling with cross-branch shift overlap prevention.
   - Leave policy balances with self-approval prevention.

6. **PharmaFlow.Documents.Api** (`http://localhost:5005`):
   - Database: `pharmaflow_documents` (Owner: `documents_user`).
   - Azurite/Azure Blob Storage private documents.
   - File header magic byte signature validation (PDF, JPG, PNG).
   - Malware scan lifecycle (dev mock scanner vs. fail-closed in production).
   - Document access auditing.

7. **PharmaFlow.Reporting.Api** (`http://localhost:5006`):
   - Database: `pharmaflow_reporting` (Owner: `reporting_user`).
   - Rebuildable reporting projections: DailySales, ShiftReconciliation, StockValuation.
   - Gross profit computed using explicit unit acquisition cost basis.

---

## 2. Security & Authentication Architecture
- **Browser Clients**:
  - Connect only through Gateway/BFF at `http://localhost:5000`.
  - On `/api/bff/login`, Gateway stores the JWT server-side and issues an `HttpOnly`, `SameSite=Lax` cookie and an accessible `XSRF-TOKEN` cookie.
  - Angular HttpClient attaches `X-XSRF-TOKEN` to all mutating requests (POST/PUT/DELETE).
  - Gateway validates CSRF token before attaching the downstream `Authorization: Bearer <jwt>` header.
- **Direct Service API Clients**:
  - Direct services (and Gateway downstream forwarding) authenticate using Bearer JWT tokens.
  - Downstream services validate actor type, permissions, and branch scoping.
