# 🔐 DataVault — Secure Personal Data Vault

> **ASP.NET Core 8 MVC — Standalone Single-Project Application**  
> AES-256 Encryption · Secure Share Links · Version History · Full Audit Logs · Cookie Auth · Light/Dark Theme

---

## 📁 Project Structure

```
DataVault-5.0/
│
├── Controllers/
│   ├── AuthController.cs             ← Register, Login, Logout (cookie-based)
│   ├── HomeController.cs             ← Dashboard: stats + recent activity
│   ├── FilesController.cs            ← Upload, download, delete, versions
│   ├── SharesController.cs           ← Share links + direct permissions
│   └── LogsController.cs             ← Audit log viewer
│
├── Services/
│   ├── EncryptionService.cs          ← AES-256-CBC, PBKDF2 (100k iters), SHA-256
│   ├── FileStorageService.cs         ← Encrypted .enc blob I/O
│   └── AccessLogService.cs           ← Writes every access event to DB
│
├── Models/
│   ├── Models.cs                     ← EF Core domain entities
│   └── ViewModels.cs                 ← Page-level view models
│
├── Data/
│   └── VaultDbContext.cs             ← EF Core + SQL Server context
│
├── Migrations/
│   ├── 20260305072437_InitialCreate  ← Initial DB schema
│   └── 20260506181737_RenameToIntendedFor ← ShareLink.IntendedFor column rename
│
├── Middleware/
│   └── SecurityHeadersMiddleware.cs  ← Adds security response headers
│
├── Views/
│   ├── Shared/
│   │   └── _Layout.cshtml            ← Sidebar shell, topbar, theme toggle, alerts
│   ├── Auth/
│   │   ├── Login.cshtml              ← Full-screen login (particle bg + theme toggle)
│   │   └── Register.cshtml           ← Vault initialization screen (+ theme toggle)
│   ├── Home/
│   │   └── Index.cshtml              ← Dashboard: stats, activity feed, encryption info
│   ├── Files/
│   │   ├── Index.cshtml              ← File grid, upload modal, share modal
│   │   └── Detail.cshtml             ← Metadata, version history, per-file logs
│   ├── Shares/
│   │   ├── Index.cshtml              ← Share links table + permissions tab
│   │   ├── PublicShare.cshtml        ← Anonymous public download page
│   │   ├── ShareDownloadLogin.cshtml ← Owner-login required gate (see Share Link Security)
│   │   └── ShareInvalid.cshtml       ← Expired / invalid token page
│   └── Logs/
│       └── Index.cshtml              ← Full audit table with denied-only filter
│
├── wwwroot/
│   ├── css/
│   │   └── vault.css                 ← Dark industrial design system + light theme overrides
│   └── js/
│       └── vault.js                  ← Canvas particles, live clock, modal system, theme toggle
│
├── Properties/
│   └── launchSettings.json           ← Dev server ports (HTTP: 5100, HTTPS: 7100)
│
├── Program.cs                        ← DI setup, EF Core, cookie auth, middleware pipeline
├── appsettings.json                  ← Connection string, vault storage path, server secret
└── DataVault.MVC.csproj              ← .NET 8 project file
```

---

## 🏗️ Architecture

This is a **standalone ASP.NET Core 8 MVC application** — there is no separate API project. All encryption, file storage, authentication, and business logic live in the same process alongside the Razor views. Authentication uses **ASP.NET Core cookie sessions** — there is no JWT.

```
Browser
   │  HTTPS — Razor server-rendered pages
   ▼
DataVault.MVC
   ├── Cookie auth (ASP.NET Core cookie sessions)
   ├── EF Core ──→ SQL Server (users, file metadata, share links, audit logs)
   └── FileStorageService ──→ Disk (.enc blobs, AES-256 encrypted)
```

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
- SHA-256 hash of plaintext is computed at upload time and stored for display; **integrity verification on download is not yet implemented**

> ⚠️ **Password in session cookie:** To decrypt the master key on each request, the user's plaintext password is stored as a claim inside the auth cookie. This is a known design trade-off, flagged in the source code. For hardened production deployments, replace this with a short-lived encrypted server-side session token so the raw password never leaves the server process. See the Production Checklist below.

### Share Link Security
- Tokens are **cryptographically random** (32 bytes = 256-bit, URL-safe base64)
- Configurable expiry (time-based, in days)
- Configurable max-use count (auto-expires when exhausted)
- Instant revocation at any time
- Permission levels: **View** / **Download** (Comment is defined but not yet enforced)
- Every access attempt (including denied) is written to the audit log
- **Anonymous downloads require the file owner to be logged in.** Because decryption requires the owner's password from their active session, the server cannot serve a download to an anonymous recipient unless the owner is also currently authenticated. Recipients who arrive while the owner is logged out will see the `ShareDownloadLogin` page, which redirects to the owner login screen.

---

## 🚀 Getting Started

### Prerequisites

| Requirement | Version |
|---|---|
| .NET SDK | 8.0+ |
| SQL Server | Any edition (Express / LocalDB / full) |
| dotnet-ef tool | Latest |

Check what you have:

```bash
dotnet --version        # must be 8.x
dotnet ef --version     # install below if missing
```

Install the EF Core CLI tool if needed:

```bash
dotnet tool install --global dotnet-ef
```

Trust the dev HTTPS certificate (one-time):

```bash
dotnet dev-certs https --trust
```

---

### 1. Extract & Configure

Unzip `DataVault-5_0.zip` and open the extracted folder in a terminal.

Edit `appsettings.json`:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=(localdb)\\mssqllocaldb;Database=DataVaultDb;Trusted_Connection=True;MultipleActiveResultSets=true"
  },
  "Vault": {
    "StoragePath": "C:\\VaultStorage",
    "ServerSecret": "CHANGE_THIS_TO_A_RANDOM_SECRET_STRING",
    "BaseUrl": "https://localhost:7100"
  }
}
```

> **SQL Server Express?** Replace `(localdb)\\mssqllocaldb` with `localhost\\SQLEXPRESS`.

> **`ServerSecret`** is combined with the user's password to derive the Key Encryption Key (KEK). Change it to a long random string before any real use — if it leaks, an attacker with a copy of the database can brute-force master keys.

---

### 2. Apply Migrations

```bash
dotnet ef database update
```

This creates the `DataVaultDb` database and all tables. The app also auto-migrates on first run in Development mode.

---

### 3. Run the App

```bash
dotnet run
```

The terminal will show:

```
Now listening on: https://localhost:7100
Now listening on: http://localhost:5100
```

Open `https://localhost:7100` in your browser, register an account, and start uploading files.

---

### Running in Visual Studio

1. Open `DataVault.MVC.csproj` in Visual Studio 2022+
2. NuGet packages restore automatically
3. Open **Package Manager Console** → run `Update-Database`
4. Press **Ctrl+F5** (run without debugger) or **F5** (with debugger)

---

### Common Issues

| Problem | Fix |
|---|---|
| `dotnet ef` not found | `dotnet tool install --global dotnet-ef` |
| SSL certificate error in browser | `dotnet dev-certs https --trust` |
| Port already in use | Change ports in `Properties/launchSettings.json` |
| DB connection failed | Verify SQL Server is running; check connection string in `appsettings.json` |
| Migration errors | Delete the database and re-run `dotnet ef database update` |

---

## 🌗 Light / Dark Theme

The app ships with both a dark industrial theme (default) and a clean light theme. The preference persists across sessions via `localStorage`.

| Location | Toggle |
|---|---|
| Logged-in pages | Button in the top-right of the topbar (next to AES-256 badge) |
| Login / Register | Floating button pinned to the top-right corner of the screen |

**How it works:**

- Clicking the toggle sets `data-theme="light"` on `<html>` (or removes it for dark)
- An inline script in `<head>` reads `localStorage` and applies the theme before first paint — no flash on reload
- All colors are CSS custom properties (`--bg`, `--text`, `--cyan`, etc.) so the entire UI switches with a single attribute change
- Theme state stored under key `dvTheme` in `localStorage` (`"light"` or `"dark"`)

**Relevant files:**

```
wwwroot/css/vault.css          ← :root { } dark vars + [data-theme="light"] { } overrides
wwwroot/js/vault.js            ← toggleTheme(), _syncThemeButton()
Views/Shared/_Layout.cshtml    ← anti-flash script, toggle button in topbar
Views/Auth/Login.cshtml        ← anti-flash script, floating toggle button
Views/Auth/Register.cshtml     ← anti-flash script, floating toggle button
```

---

## 🖥️ Pages

| Page | URL | Description |
|---|---|---|
| Login | `/login` | Authenticate with email + passphrase |
| Register | `/register` | Create a new vault account |
| Dashboard | `/` | Stats, recent activity, encryption status panel |
| My Files | `/files` | Upload, download, share, and delete encrypted files |
| File Detail | `/files/{id}/detail` | Metadata, version history, per-file access logs |
| Shares | `/shares` | Manage share links and direct user permissions |
| Access Logs | `/logs` | Full audit trail; filterable by denied-only |
| Public Share | `/s/{token}` | View file info via share link |
| Public Download | `/s/{token}/download` | Download via share link (requires owner to be logged in) |

---

## 🛡️ Security Headers

Every response includes:

```
X-Content-Type-Options:   nosniff
X-Frame-Options:          DENY
X-XSS-Protection:         1; mode=block
Referrer-Policy:          strict-origin-when-cross-origin
X-Request-ID:             <uuid>   (unique per request for tracing)
```

---

## 🌐 Security Design Highlights

| Concept | Implementation |
|---|---|
| **Data Sovereignty** | User passphrase drives KEK derivation — server cannot decrypt without the active session cookie |
| **Immutable Audit Trail** | Every access event (including denied) logged with IP, timestamp, actor |
| **Integrity Hashing** | SHA-256 of plaintext computed and stored at upload time; displayed on the file detail page |
| **Revocable Access** | Share links and direct permissions instantly revocable |
| **Version Snapshots** | Automatic versioning on re-upload; full point-in-time restore; each version stores its own independent key and IV |
| **Zero-Trust Storage** | Files stored as opaque `.enc` blobs; disk filenames are random GUIDs |
| **Per-File Keys** | Each file encrypted with a unique AES-256 key — no key reuse |

---

## 🔧 Production Checklist

- [ ] Replace `Vault:ServerSecret` with a strong random value (64+ chars)
- [ ] **Replace raw-password cookie claim** with a short-lived encrypted server-side session token (see `AuthController.SignInAsync` — the `vault_password` claim)
- [ ] Use Azure Key Vault / AWS KMS for storing the server-side KEK
- [ ] Enforce HTTPS only — disable HTTP in `launchSettings.json`
- [ ] Move `StoragePath` to durable block storage (Azure Blob Storage, AWS S3)
- [ ] Enable SQL Server TDE or Always Encrypted for data-at-rest protection
- [ ] Add rate limiting on `/login` and `/register` to prevent brute force
- [ ] Set a short cookie sliding expiration window and regenerate session on login
- [ ] Add a background job to purge expired share links and soft-deleted files
- [ ] Enable structured logging (Serilog / Application Insights) for production audit trails
- [ ] Set up SMTP for share-link notification emails
- [ ] Implement hash verification on download (compare decrypted plaintext SHA-256 against stored `FileHash`)
- [ ] Decide on `Comment` permission behaviour and implement enforcement in share download logic