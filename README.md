# 🔐 DataVault — Decentralized-Style Personal Data Vault

> **ASP.NET Core 8 Web API + ASP.NET Core MVC Browser UI + Blazor WebAssembly**  
> AES-256 Encryption · Secure Share Links · Version History · Full Audit Logs · JWT Auth

---

## 📁 Solution Structure

```
DataVault/
├── DataVault.sln
│
├── API/                                  ← ASP.NET Core 8 Web API (backend engine)
│   ├── Controllers/
│   │   ├── AuthController.cs             ← Register, Login → JWT
│   │   ├── FilesController.cs            ← Upload/Download/Delete/Versions/Stats
│   │   ├── SharesController.cs           ← Share links, permissions, anonymous access
│   │   └── LogsController.cs             ← Access audit trail
│   ├── Services/
│   │   ├── EncryptionService.cs          ← AES-256-CBC, PBKDF2, SHA-256
│   │   ├── TokenService.cs               ← JWT generation & validation
│   │   ├── FileStorageService.cs         ← Encrypted file I/O (.enc blobs)
│   │   └── AccessLogService.cs           ← Audit logging
│   ├── Models/
│   │   ├── Models.cs                     ← Domain entities (EF Core)
│   │   └── DTOs.cs                       ← Request/Response records
│   ├── Data/
│   │   ├── VaultDbContext.cs             ← EF Core + SQL Server
│   │   └── Migrations/                   ← Initial schema migration
│   ├── Middleware/
│   │   └── SecurityHeadersMiddleware.cs  ← Security response headers
│   ├── Program.cs                        ← Full DI/pipeline setup
│   └── appsettings.json
│
├── Web/                                  ← ASP.NET Core MVC Browser UI (primary frontend)
│   ├── Controllers/
│   │   ├── AuthController.cs             ← Login/Register/Logout (session-based)
│   │   ├── HomeController.cs             ← Dashboard page
│   │   ├── FilesController.cs            ← File manager (upload, download, delete, versions)
│   │   ├── SharesController.cs           ← Share links & direct permissions management
│   │   └── LogsController.cs             ← Access log viewer
│   ├── Services/
│   │   └── VaultApiClient.cs             ← HTTP client wrapping all API calls
│   ├── Models/
│   │   └── ViewModels.cs                 ← Page view models + API DTOs
│   ├── Views/
│   │   ├── Shared/_Layout.cshtml         ← Full sidebar shell, topbar, alert toasts
│   │   ├── Auth/Login.cshtml             ← Full-screen login with particle background
│   │   ├── Auth/Register.cshtml          ← Vault initialization screen
│   │   ├── Home/Index.cshtml             ← Dashboard: stats, activity, encryption panel
│   │   ├── Files/Index.cshtml            ← File grid, upload modal, share modal
│   │   ├── Files/Detail.cshtml           ← File metadata, version history, per-file logs
│   │   ├── Shares/Index.cshtml           ← Share links table + permissions tab
│   │   └── Logs/Index.cshtml             ← Full audit table with denied-only filter
│   ├── wwwroot/
│   │   ├── css/vault.css                 ← Dark industrial design system
│   │   └── js/vault.js                   ← Canvas particles, clock, modal system
│   ├── Properties/launchSettings.json
│   ├── Program.cs
│   └── appsettings.json
│
└── Blazor/                               ← Blazor WebAssembly (alternative frontend)
    ├── Pages/
    │   ├── Login.razor                   ← Auth (login + register)
    │   ├── Dashboard.razor               ← Stats + recent activity
    │   ├── Files.razor                   ← Upload, share, version, delete
    │   ├── Shares.razor                  ← Manage links & permissions
    │   └── Logs.razor                    ← Full audit log viewer
    ├── Shared/
    │   └── MainLayout.razor              ← Futuristic nav shell
    ├── Services/
    │   └── VaultApiService.cs            ← All HTTP calls to API
    ├── Models/Models.cs                  ← Shared DTOs
    ├── wwwroot/
    │   ├── index.html
    │   ├── css/vault.css
    │   └── appsettings.json
    └── Program.cs
```

---

## 🏗️ Architecture Overview

```
Browser (Web UI)
      │  HTTP/HTTPS (Razor Pages, server-rendered)
      ▼
DataVault.Web  ──── Session (JWT stored server-side)
      │  HttpClient → Bearer token on every request
      ▼
DataVault.API  ──── JWT auth, business logic, encryption
      │
      ├── SQL Server  ── users, file metadata, share links, access logs
      └── Disk (.enc) ── AES-256 encrypted file blobs
```

The **Web** project is a traditional server-rendered MVC app. It stores the JWT in an ASP.NET session and acts as a proxy between the browser and the API — the browser never directly calls the API. The **Blazor** project is an alternative SPA frontend that calls the API directly from the browser.

---

## 🔐 Encryption Model (Zero-Knowledge Inspired)

```
User Password
     │
     ▼  PBKDF2-SHA256 (100,000 iterations)
   KEK (Key Encryption Key)
     │
     ▼  AES-256-CBC
Encrypted Master Key  ──── stored in SQL Server
     │
     │  (decrypted in memory using user's password)
     ▼
   Master Key
     │
     ▼  AES-256-CBC
Encrypted File Key  ──── stored in SQL Server per file
     │
     │  (decrypted using master key)
     ▼
   File Key + random IV
     │
     ▼  AES-256-CBC streaming
  .enc file on disk  (filename is a random GUID — content type hidden)
```

**Key Properties:**
- The server **never** stores plaintext passwords or unencrypted file keys
- Each file has its **own unique AES-256 key** and IV
- File keys are encrypted with the user's master key before DB storage
- Master key is encrypted with a PBKDF2-derived key from the user's password + server secret
- Files on disk are stored as `.enc` blobs — original filename and type are never on disk
- SHA-256 hash of plaintext is computed before encryption for integrity verification

### Share Link Security
- Tokens are **cryptographically random** (32 bytes = 256-bit, URL-safe base64)
- Configurable expiry (time-based, in days)
- Configurable max-use count (auto-expires when exhausted)
- Instant revocation at any time
- Permission levels: **View** / **Download** / **Comment**
- Every access attempt (including denied) is written to the audit log

---

## 🚀 Getting Started

### Prerequisites
- .NET 8 SDK
- SQL Server or SQL Server LocalDB
- Visual Studio 2022 / 2026 or VS Code

### 1. Clone & Configure

```bash
git clone https://github.com/Peash02/DataVault.git
cd DataVault
```

Edit `API/appsettings.json` with your values:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=(localdb)\\mssqllocaldb;Database=DataVaultDb;Trusted_Connection=True"
  },
  "Jwt": {
    "Secret": "CHANGE_THIS_TO_A_RANDOM_64_CHARACTER_STRING",
    "Issuer": "DataVault.API",
    "Audience": "DataVault.Blazor",
    "ExpiryHours": "12"
  },
  "Vault": {
    "StoragePath": "C:\\VaultStorage",
    "ServerSecret": "CHANGE_THIS_SERVER_SECRET_TOO",
    "BaseUrl": "https://localhost:7100"
  }
}
```

### 2. Create the Database

```bash
cd API
dotnet ef database update
```

The API also auto-migrates on first run in Development mode.

### 3. Run the API

```bash
cd API
dotnet run   //use commands not f5 to run the project 
# API running at:  https://localhost:7100
# Swagger UI at:   https://localhost:7100/swagger
```

### 4. Run the Browser UI

Open a **second terminal**:

```bash
cd Web
dotnet run    // use command not f5 to run the project 
# App running at:  https://localhost:7200
```

Open `https://localhost:7200` in your browser. Register an account, then start uploading files.

### 5. (Optional) Run the Blazor Frontend

```bash
cd Blazor
dotnet run
# App running at:  https://localhost:7201
```

> **Note:** The API must always be running. Both the Web and Blazor frontends depend on it.

---

## 🖥️ Browser UI Pages

| Page | URL | Description |
|------|-----|-------------|
| Login | `/login` | Authenticate with email + vault passphrase |
| Register | `/register` | Create a new vault account |
| Dashboard | `/` | Stats overview, recent activity, encryption status |
| My Files | `/files` | Upload, download, share, delete encrypted files |
| File Detail | `/files/{id}/detail` | Metadata, version history, per-file access log |
| Shares | `/shares` | Manage share links and direct permissions |
| Access Logs | `/logs` | Full audit trail, filterable by denied-only |

---

## 📡 API Endpoints

### Auth
| Method | Route | Description |
|--------|-------|-------------|
| POST | `/api/auth/register` | Create vault account |
| POST | `/api/auth/login` | Authenticate → JWT |

### Files
| Method | Route | Description |
|--------|-------|-------------|
| GET | `/api/files` | List all user's files |
| POST | `/api/files/upload` | Upload + AES-256 encrypt |
| GET | `/api/files/{id}/download` | Decrypt + stream file |
| DELETE | `/api/files/{id}` | Soft-delete |
| GET | `/api/files/{id}/versions` | Version history |
| POST | `/api/files/{id}/versions/{vid}/restore` | Restore a previous version |
| GET | `/api/files/stats` | Dashboard statistics |

### Shares & Permissions
| Method | Route | Description |
|--------|-------|-------------|
| POST | `/api/shares` | Create share link |
| GET | `/api/shares` | List my share links |
| DELETE | `/api/shares/{id}` | Revoke share link |
| GET | `/api/shares/access/{token}` | Validate & access (anonymous) |
| POST | `/api/shares/permissions` | Grant direct permission to a user |
| GET | `/api/shares/permissions` | List all direct permissions |
| DELETE | `/api/shares/permissions/{id}` | Revoke direct permission |

### Logs
| Method | Route | Description |
|--------|-------|-------------|
| GET | `/api/logs` | All audit logs (optional `?deniedOnly=true`) |
| GET | `/api/logs/file/{fileId}` | Logs for a specific file |

---

## 🛡️ Security Headers

Every API response includes:
- `X-Content-Type-Options: nosniff`
- `X-Frame-Options: DENY`
- `X-XSS-Protection: 1; mode=block`
- `Referrer-Policy: strict-origin-when-cross-origin`
- `X-Request-ID: <uuid>` (unique per request for tracing)

---

## 🌐 Futuristic Features (Web3-Inspired)

| Concept | Implementation |
|---------|----------------|
| **Data Sovereignty** | Users own their encryption keys; server cannot read files without the passphrase |
| **Immutable Audit Trail** | Every access event logged with IP address, timestamp, and actor identity |
| **Cryptographic Integrity** | SHA-256 plaintext hash stored and verifiable on download |
| **Revocable Access** | Share links and direct permissions are instantly revocable |
| **Version Snapshots** | Automatic versioning on every re-upload; full restore support |
| **Zero-Trust Storage** | Files stored as opaque `.enc` blobs; filenames are random GUIDs on disk |
| **Per-File Keys** | Each file encrypted with its own unique AES-256 key, never reused |

---

## 🔧 Production Checklist

- [ ] Replace `Vault:ServerSecret` and `Jwt:Secret` with strong cryptographically random values
- [ ] Use Azure Key Vault or AWS KMS for the server-side KEK
- [ ] Enforce HTTPS only — remove HTTP binding in `launchSettings.json`
- [ ] Lock down CORS in `API/Program.cs` to your exact Web UI origin
- [ ] Move `StoragePath` to high-availability block storage (Azure Blob Storage, AWS S3)
- [ ] Enable SQL Server Always Encrypted or Transparent Data Encryption
- [ ] Add rate limiting on `/api/auth/*` endpoints to prevent brute force
- [ ] Set a short `Session.IdleTimeout` in `Web/Program.cs` and rotate session secrets
- [ ] Set up a background job to purge expired share links and soft-deleted files
- [ ] Add email notifications on share link creation and denied access spikes
- [ ] Enable structured logging (Serilog / Application Insights) for production audit trails
