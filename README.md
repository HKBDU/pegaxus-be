# 🐎 Pegaxus Backend API (`pegaxus-be`)

> **Racehorse Cross-Border Transport & Quarantine Logistics Management System**  
> *Course Project: SWP391 (Software Development Project)*

[![Quality CI](https://github.com/HKBDU/pegaxus-be/actions/workflows/quality.yml/badge.svg)](https://github.com/HKBDU/pegaxus-be/actions/workflows/quality.yml)
[![.NET Version](https://img.shields.io/badge/.NET-8.0%20LTS-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![C#](https://img.shields.io/badge/C%23-12.0-239120?logo=csharp&logoColor=white)](https://learn.microsoft.com/en-us/dotnet/csharp/)
[![PostgreSQL](https://img.shields.io/badge/PostgreSQL-16-4169E1?logo=postgresql&logoColor=white)](https://www.postgresql.org/)
[![Docker](https://img.shields.io/badge/Docker-Ready-2496ED?logo=docker&logoColor=white)](https://www.docker.com/)
[![License](https://img.shields.io/badge/License-Apache%202.0-blue.svg)](LICENSE)

---

## 📖 1. Project Overview

**Pegaxus Backend** is the core RESTful Web API powering the international racehorse transportation, veterinary compliance, and tracking platform. Built on **.NET 8** and **Clean Architecture**, it manages the end-to-end lifecycle of high-value equine cross-border transit:

- **Transport Planning & Multimodal Routing**: Calculation of overland, sea, and air legs with mandatory rest stops and quarantine border checkpoints.
- **Veterinary Compliance & Dossier System**: Verification and lifecycle management of veterinary health certificates, vaccination records, import/export permits, and customs declarations.
- **Real-Time Voyage Tracking & Incident Management**: Secure telemetry reporting from transit vehicles, drivers, and veterinary handlers.
- **Customer Portal, Quotations & Billing**: Role-based access control (RBAC), invoice generation, booking status transitions, and audit logs.

---

## 🛠️ 2. Technology Stack

| Category | Technology / Library | Version | Purpose |
| :--- | :--- | :--- | :--- |
| **Framework** | [ASP.NET Core Web API](https://dotnet.microsoft.com/) | `.NET 8.0 LTS` | High-performance, cross-platform RESTful API |
| **Language** | [C#](https://learn.microsoft.com/en-us/dotnet/csharp/) | `12.0` | Modern type-safe backend programming |
| **Database** | [PostgreSQL](https://www.postgresql.org/) | `16 Alpine` | Relational database management system |
| **ORM** | [Entity Framework Core](https://learn.microsoft.com/en-us/ef/core/) | `8.0` (Npgsql) | Data modeling, migrations, and database access |
| **Authentication** | JWT Bearer Tokens | System.IdentityModel | Secure stateless authentication and RBAC |
| **API Documentation**| [Swagger / OpenAPI](https://swagger.io/) | Swashbuckle | Interactive API testing and schema documentation |
| **CI / Automation** | [GitHub Actions](https://github.com/features/actions) | Workflow v4 | Automated branch/PR convention validation and build |
| **Containerization** | [Docker](https://www.docker.com/) & Docker Compose | Multi-stage | Containerized deployment with health checks |

---

## 📂 3. Solution Structure

```text
pegaxus-be/
├── .github/
│   ├── workflows/
│   │   └── quality.yml             # CI Pipeline: validates PR metadata & solution build
│   └── PULL_REQUEST_TEMPLATE.md    # Pull Request submission template
├── Pegaxus.API/                    # [Presentation Layer] Web API controllers & endpoints
│   ├── Constants/                  # System-wide constants & role definitions
│   ├── Controllers/                # RESTful API Controllers
│   ├── Extensions/                 # Service collection & middleware DI setup
│   ├── Filters/                    # Action and authorization filters
│   ├── Middleware/                 # Global exception handler, JWT, and logging
│   ├── appsettings.json            # Base configuration
│   ├── appsettings.Development.json# Local development configuration
│   └── Program.cs                  # Application bootstrap entry point
├── Pegaxus.Service/                # [Business Logic Layer] Application use cases
│   ├── <Module>/                   # Feature-based folders (Booking, Transport, Dossier)
│   │   ├── I<Module>Service.cs     # Service contract interface
│   │   ├── <Module>Service.cs      # Core business logic implementation
│   │   ├── Requests/               # Inbound DTO payloads
│   │   ├── Responses/              # Outbound DTO models
│   │   └── Validations/            # FluentValidation rules
├── Pegaxus.Repository/             # [Data Access Layer] Persistence & database context
│   ├── Abstractions/               # Generic Repository and Unit of Work interfaces
│   ├── Configurations/             # EF Core Fluent API entity relationships
│   ├── Context/                    # ApplicationDbContext
│   └── Entities/                   # Database POCO entities
├── .dockerignore                   # Docker build context exclusion rules
├── .env.example                    # Environment variables template
├── docker-compose.yml              # Local multi-container orchestration (API + PostgreSQL)
├── Dockerfile                      # Multi-stage production container build (.NET 8 + Alpine)
├── Pegaxus.sln                     # Visual Studio / .NET Solution manifest
└── RULESET.md                      # Branching strategy & GitHub branch protection rules
```

---

## 🚀 4. Getting Started

### 4.1. Prerequisites
- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- [Docker Desktop](https://www.docker.com/products/docker-desktop/) (or local PostgreSQL 16)
- Optional: [Postman](https://www.postman.com/) or REST Client

### 4.2. Local Environment Setup

1. **Clone the repository**:
   ```bash
   git clone https://github.com/HKBDU/pegaxus-be.git
   cd pegaxus-be
   ```

2. **Configure environment variables**:
   ```bash
   # Windows PowerShell:
   Copy-Item .env.example .env

   # Linux/macOS:
   cp .env.example .env
   ```

3. **Start PostgreSQL Database**:
   ```bash
   docker compose up postgres -d
   ```

4. **Restore & Build Solution**:
   ```bash
   dotnet restore Pegaxus.sln
   dotnet build Pegaxus.sln
   ```

5. **Apply Database Migrations & Start API**:
   ```bash
   dotnet ef database update --project Pegaxus.Repository --startup-project Pegaxus.API
   dotnet run --project Pegaxus.API
   ```
   - Swagger Documentation: [http://localhost:5000/swagger](http://localhost:5000/swagger)
   - Healthcheck Endpoint: [http://localhost:5000/health](http://localhost:5000/health)

---

## 🐳 5. Running with Docker Compose

To launch both **Pegaxus API** and **PostgreSQL 16** together in isolated containers:

```bash
# Start all containers in background
docker compose up --build -d

# View live API logs
docker compose logs -f api

# Stop all containers
docker compose down
```

---

## 🌿 6. Branching Strategy & CI Ruleset

To keep Git history clean and maintain software stability:
- 🛡️ **Branching Model & Ruleset**: Refer to [RULESET.md](./RULESET.md).
  - Core branches: **`main`** (production) and **`dev`** (integration).
  - Feature branches: **`feat/<lowercase-name>`** (must use `feat/`, not `feature/`).
  - Strict CI validation via [.github/workflows/quality.yml](./.github/workflows/quality.yml):
    - **Branch regex**: `^(dev|(feat|fix|docs|style|refactor|perf|test|build|ci|chore|revert)/[a-z0-9]+([._-][a-z0-9]+)*)$`
    - **PR Title regex**: `^(dev|(feat|fix|docs|style|refactor|perf|test|build|ci|chore|revert)(\([a-z0-9._-]+\))?!?: .+)`
- 🤝 **Contributing Guidelines**: Refer to [CONTRIBUTING.md](./CONTRIBUTING.md).
- 📋 **Pull Request Template**: Use [.github/PULL_REQUEST_TEMPLATE.md](./.github/PULL_REQUEST_TEMPLATE.md).

---

## 🔐 7. Environment Variables

| Variable Name | Default Value | Purpose |
| :--- | :--- | :--- |
| `POSTGRES_USER` | `pegaxus_user` | PostgreSQL database user |
| `POSTGRES_PASSWORD` | `pegaxus_secret_password` | PostgreSQL database password |
| `POSTGRES_DB` | `pegaxus_db` | Main database name |
| `POSTGRES_PORT` | `5432` | Host port for PostgreSQL |
| `API_PORT` | `5000` | Host port for Pegaxus Web API |
| `JWT_SECRET` | *32+ char secret* | Key used for signing JWT access tokens |
| `JWT_ISSUER` | `Pegaxus.API` | Token issuer identifier |
| `JWT_AUDIENCE` | `Pegaxus.Client` | Token audience identifier |
| `JWT_EXPIRY_MINUTES`| `1440` | Token expiration duration (in minutes) |

---

## ComplianceDocument Upload (UC-21)

POST `/api/v1/compliance-dossiers/{dossierId}/requirements/{requirementId}/document` with a Customer Bearer token and multipart form data:
`Files` (repeated file field), `DocumentNumber`, `IssuingAuthority`, `IssuedDate`, optional `ExpiryDate` and `UploadNote`.
Submit 1–10 files, each at most 10 MiB; only PDF, JPG, JPEG and PNG are accepted. Extension, MIME and file signature must agree.

Only the booking owner can upload. The dossier must be IN_PREPARATION or REJECTED; the requirement must be PENDING or REJECTED.
UC-21 creates the first document/version only. An existing document returns 409 rather than overwriting its version history.
The first file is stored on DocumentVersion v1; additional pages are DocumentAttachments of that same version.
Success creates one ComplianceDocument, one DocumentVersion v1, an audit entry, and sets the requirement to PROVIDED.

Document number, issuing authority and issue date are required. Issue date cannot be in the future. Expiry is required when
the catalog marks validity as required or the requirement has `valid_until`; it must be strictly after departure and at least `valid_until`.

Configure `DocumentStorageOptions` using the variables in `.env.example`. AWS credentials use the SDK's standard credential chain:
prefer an IAM role; local development may use AWS_ACCESS_KEY_ID, AWS_SECRET_ACCESS_KEY and optional AWS_SESSION_TOKEN.
Grant s3:PutObject, s3:GetObject, s3:DeleteObject and s3:GetBucketPublicAccessBlock on the configured bucket/prefix;
versioned buckets also need s3:GetObjectVersion and s3:DeleteObjectVersion. Keep all four bucket Public Access Block settings enabled.
The implementation verifies private access, requests and checks AES256 server-side encryption, and returns HTTPS GET URLs expiring in five minutes.
Stable `s3://` references, including object version IDs when available, are stored in the DB; signed URLs are not persisted or logged.

A reachable ClamAV clamd service is mandatory (private network, INSTREAM enabled, StreamMaxLength at least 10 MiB, current virus definitions).
No clean verdict, missing configuration, scanner failure or insecure bucket means the upload fails closed without DB changes.
Failures clean uncommitted objects; if the DB commit outcome is unknown, files are retained rather than risking deletion of committed documents.
The DB write runs in a serializable transaction and revalidates ownership, status and expiry after storage I/O.
No migration is needed: existing unique indexes protect one document per requirement and one version number per document.
The 4-second/10-MB target needs measurement with real S3, ClamAV and PostgreSQL; unit tests do not establish throughput.

## 👥 8. License & Academic Attribution

Developed as a capstone software engineering project for **SWP391** (Software Development Project).  
Distributed under the [Apache-2.0 License](./LICENSE).
