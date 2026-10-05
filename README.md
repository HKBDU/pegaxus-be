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

## Manual DossierRequirement management (D3, D6)

Both endpoints require a Bearer token and `TransportSpecialistPolicy`. IDs use the existing UUID schema, not numeric IDs.

POST `/api/v1/compliance-dossiers/{dossierId}/requirements` requires `complianceRequirementId` and `isMandatory`.
Optional fields: `horseId`, `dueDate` (ISO date), `instruction`. A null `horseId` applies to the whole trip.
An omitted `dueDate` remains null. Legacy `leadTimeDays` and `reason` are still accepted but are not required;
do not supply both `dueDate` and `leadTimeDays`. Success returns 201, a PENDING requirement, an audit entry and a Customer notification.

PATCH `/api/v1/compliance-dossiers/{dossierId}/requirements/{requirementId}/waive` accepts `waivedReason` (required, up to 2000 characters).
Success returns 200 with status WAIVED. The requirement stays in the checklist, retains `isMandatory`, and counts as fulfilled.
The trimmed reason, acting specialist, audit entry and Customer notification are saved without submitting the dossier automatically.

| HTTP | errorCode | Condition |
| :--- | :--- | :--- |
| 409 | `DR_DUPLICATE` | The dossier/catalog/horse combination already exists, including null horse IDs |
| 409 | `DOSSIER_LOCKED` | The dossier no longer allows requirement changes |
| 400 | `REQUIREMENT_INACTIVE` | The catalog requirement is missing, deleted or not ACTIVE |
| 400 | `HORSE_NOT_IN_BOOKING` | The horse does not belong to the dossier's booking |
| 400 | `WAIVE_REASON_REQUIRED` | The waiver reason is missing or blank |

## ComplianceDocument Upload (UC-21)

For each PDF/JPG/JPEG/PNG file (up to 10 MiB), call POST `/api/v1/uploads/signature` with
`entity: "attachment"`, `contentType`, and `sizeBytes`. Upload the binary directly to the returned Cloudinary URL
using every returned parameter, then call POST `/api/v1/uploads/confirm` with `publicId`.
Signatures enforce authenticated delivery and disable overwrites. Confirmation checks actual Cloudinary metadata
and returns `publicId`, `fileName`, `mimeType`, `sizeBytes`, and a signed `fileUrl` with `fileUrlExpiresAt`.

POST `/api/v1/compliance-dossiers/{dossierId}/requirements/{requirementId}/document` with a Customer Bearer token and JSON:

```json
{
  "files": [{ "publicId": "evidence/<accountId>/<assetId>" }],
  "documentNumber": "PASS-100",
  "issuingAuthority": "Passport Authority",
  "issuedDate": "2026-10-01",
  "expiryDate": "2027-10-01",
  "uploadNote": "Full color scan"
}
```

Submit 1–10 distinct asset references in the intended page order. Do not send binary files, file URLs, MIME types,
sizes, or filenames to UC21/UC22. The service reuses MediaService to reverify account ownership and existence
and resolve trusted metadata for every publicId; a previous frontend confirmation is not trusted as proof.
Actual file bytes are downloaded by the backend for format checks and virus scanning, not uploaded again by the frontend.

Only the booking owner can upload. The dossier must be IN_PREPARATION or REJECTED; the requirement must be PENDING, REQUESTED or REJECTED.
REQUESTED requirements need an OPEN MISSING_DOCUMENT request before their first upload.
UC-21 creates the first document/version only. An existing document returns 409 rather than overwriting its version history.
The first file is stored on DocumentVersion v1; additional pages are DocumentAttachments of that same version.
Success creates one ComplianceDocument, one DocumentVersion v1, an audit entry, and sets the requirement to PROVIDED.

Document number, issuing authority and issue date are required. Issue date cannot be in the future. Expiry is required when
the catalog marks validity as required or the requirement has `valid_until`; it must be strictly after departure and at least `valid_until`.

Configure `CloudinaryOptions` and the ClamAV settings in `DocumentStorageOptions` using `.env.example`.
New versions store stable authenticated Cloudinary URLs resolved by the backend; signed URLs are not persisted.
Each response `value.files[]` contains `readUrl` and `readUrlExpiresAt`, expiring after five minutes.
The version-history endpoint regenerates read URLs after authorization. A Cloudinary signed delivery URL without
an expiry is not used; the SDK's private-download API signs an explicit expiration.
Legacy S3 documents remain readable using the existing private-bucket configuration and AWS credential chain.

A reachable ClamAV clamd service is mandatory (private network, INSTREAM enabled, StreamMaxLength at least 10 MiB, current virus definitions).
No clean verdict, missing configuration, or scanner failure means document submission fails closed without DB changes.
Already-uploaded Cloudinary assets are not deleted on failed submissions or replacement: another version may reference them.
No orphan-asset cleanup worker or storage migration is added. Legacy superseded unreviewed S3 objects are deleted only after confirmed commit.
The DB write runs in a serializable transaction and revalidates ownership, status and expiry after storage I/O.
No migration is needed: existing unique indexes protect one document per requirement and one version number per document.
The 4-second/10-MB target needs measurement with real Cloudinary, ClamAV and PostgreSQL; unit tests do not establish throughput.

## DocumentVersion Replacement (UC-22)

POST `/api/v1/compliance-dossiers/{dossierId}/requirements/{requirementId}/document/versions` with a Customer token and the same JSON asset-reference contract.
`files` replaces the entire current file set (1–10 ordered publicIds; the UC-21 verification and scanning rules apply). Metadata fields and `uploadNote` are optional.
Omitted metadata retains its current value; supplied values use the same validation as the first upload.

An unreviewed PROVIDED version is updated in place: its ID and version number stay unchanged. A reviewed or rejected version creates the next number.
Reviewed versions, attachments and reviews remain immutable. Direct uploads cannot overwrite existing Cloudinary assets.
Failed or uncertain commits never delete submitted Cloudinary assets or previously committed files. Concurrent replacements return 409.

PATCH `/api/v1/compliance-dossiers/{dossierId}/requirements/{requirementId}/document/metadata` accepts a JSON object with any of
`DocumentNumber`, `IssuingAuthority`, `IssuedDate`, `ExpiryDate`. It changes document metadata only, preserving version records, files and review status.

GET `/api/v1/compliance-dossiers/{dossierId}/requirements/{requirementId}/document/versions` returns newest-first history, all file URLs and
reviews attached to their own versions. Only the booking owner, Transport Specialist or Logistics Manager can read it; URLs expire after five minutes.
Only the booking owner can submit or edit. SATISFIED requirements and dossiers sent to authorities are locked against both file and metadata edits.
Every edit records its actor and changes in the dossier audit trail. Real PostgreSQL/S3/ClamAV measurement is needed to verify the 2-second DB-write target.

## ComplianceDossier Viewing (UC-20)

All read endpoints require a Bearer token and the enum-backed `ComplianceDossierViewerPolicy` (Customer, Transport Specialist, Logistics Manager).
Customers can read only dossiers belonging to their own bookings; other employee roles receive 403.

| GET endpoint | Result |
| :--- | :--- |
| `/api/v1/bookings/{bookingId}/compliance-dossier` | Overview, or `isInitialized: false` with the initialization message |
| `/api/v1/compliance-dossiers/{dossierId}` | Overview by dossier ID |
| `/api/v1/compliance-dossiers/{dossierId}/requirements/{requirementId}` | Requirement, document, versions, attachments, review history |
| `/api/v1/compliance-dossiers/{dossierId}/requirements/{requirementId}/versions/{versionId}/preview` | Authenticated PDF/image bytes |
| `/api/v1/compliance-dossiers/{dossierId}/requirements/{requirementId}/versions/{versionId}/attachments/{attachmentId}/preview` | Authenticated attachment bytes |

Overview accepts `status` and `horseId` query filters. `tripRequirements` and `horseRequirements` contain filtered results;
`fulfilledCount`, `requirementCount` and `completionPercent` always describe the entire dossier, excluding deleted requirements.
SATISFIED and WAIVED count as fulfilled. Empty dossiers report 0%. `actions.canUpload` is Customer-only;
`actions.canReview` is Transport Specialist-only. These flags describe role permissions; mutation APIs must enforce their own lifecycle rules.

Use the returned API `previewUrl` with an Authorization header (fetch as a Blob for browser previews), not a token query parameter.
Preview validates ownership and all nested IDs. S3 documents use the configured private bucket and exact object version, with a 10 MiB limit.
Authenticated Cloudinary files use expiring private-download URLs with a 10 MiB limit. Legacy public Cloudinary files retain the 5 MB limit.
Both storage providers use only configured locations, refuse redirects and detect PDF/raster-image types
from bytes and return no-store responses. The 15-second timeout covers both headers and body.

**Storage limitation:** Legacy server-side Cloudinary upload methods use public delivery. The proxy protects API access but cannot revoke
previously known public storage URLs. Sensitive documents must be uploaded with authenticated/private delivery and existing public
assets migrated at the storage layer before claiming end-to-end file privacy. No storage migration is performed by this read-only feature.
The 1.5-second response target still requires measurement against a real PostgreSQL database.

## DocumentReview (UC-23)

All mutation endpoints require a Bearer token, the enum-backed `TransportSpecialistPolicy`, and an active Transport Specialist account.

| POST endpoint | Result |
| :--- | :--- |
| `/api/v1/compliance-dossiers/{dossierId}/requirements/{requirementId}/versions/{versionId}/approve` | Append APPROVED review; document APPROVED; requirement SATISFIED |
| `/api/v1/compliance-dossiers/{dossierId}/requirements/{requirementId}/versions/{versionId}/reviews/{reviewId}/revoke` | Append REVOKED review; preserve original approval |
| `/api/v1/compliance-dossiers/{dossierId}/ready-for-submission` | Explicitly mark dossier READY_FOR_SUBMISSION |

Approval requires `metadataConfirmed: true`, `versionUploadedAt` and `documentUpdatedAt` copied from the requirement-detail response.
Only the latest provided version is eligible. Changed file/metadata timestamps return 409; reload the detail and repeat verification.
`reviewNote` is optional. Expiry before departure returns 409 (`EXPIRY_EXCEPTION_REQUIRED`) until a nonblank
`expiryExceptionReason` is supplied. Both notes are limited to 2000 characters.

Revocation requires the current approval ID, `status` (PROVIDED or REJECTED), and a nonblank `reason` of at most 500 characters.
Revoking a mandatory requirement clears dossier readiness; optional revocation preserves readiness. Reviews are append-only,
with reviewer ID, UTC review time, version ID, and a dossier audit record. Approval never automatically readies or submits a dossier.
Readiness requires all nondeleted mandatory requirements to be SATISFIED or WAIVED, consistent with authority-submission rules.
Submitted, pending-authority, approved, closed, or uninitialized dossiers cannot be reviewed or marked ready.

Apply migration `20261004000000_AddRevokedDocumentReviewResult` before enabling revocation:
```bash
dotnet ef database update --project Pegaxus.Repository --startup-project Pegaxus.API
```

The migration adds the native PostgreSQL REVOKED enum value; rollback deliberately refuses to remove immutable review history.
The one-second API target still requires measurement against a real PostgreSQL database.

## Document rejection and re-upload requests (UC-24)

| Endpoint | Policy | Result |
| :--- | :--- | :--- |
| POST `/api/v1/compliance-dossiers/{dossierId}/requirements/{requirementId}/versions/{versionId}/reject` | TransportSpecialistPolicy | Immutable REJECTED review, OPEN REUPLOAD request, status updates, web notification and email attempt |
| POST `/api/v1/compliance-dossiers/{dossierId}/requirements/{requirementId}/document-requests/{requestId}/comments` | CustomerOrTransportSpecialistPolicy | Append comment and notify the other participant |
| GET `/api/v1/compliance-dossiers/{dossierId}/requirements/{requirementId}/document-requests/{requestId}/comments` | CustomerOrTransportSpecialistPolicy | Ordered conversation history |

All endpoints require a Bearer token and an active account. Customers may discuss only requests belonging to their own booking.
Logistics Managers can view dossier/request details but cannot reject documents or join request conversations.

Rejection requires `errorCategory` (UNCLEAR_IMAGE, EXPIRED, MISSING_STAMP, WRONG_PAGE, METADATA_MISMATCH or OTHER),
`rejectionNote` (15–2000 characters after trimming), and `revisionDeadline` (ISO date, today or later in UTC).
Copy `versionUploadedAt` and the document's `updatedAt` from the requirement-detail response into
`versionUploadedAt` and `documentUpdatedAt`. Stale snapshots or older versions return 409.
`requirementStatus` accepts REJECTED (default) or REQUESTED; `dossierStatus` accepts IN_PREPARATION (default) or REJECTED.
The latest document and requirement must both be PROVIDED. Submitted, pending-authority, approved, closed and uninitialized dossiers are blocked.
No authority submission is created automatically. Readiness and satisfaction timestamps are cleared by this explicit TS action.

Overview requirement items expose `openDocumentRequest`; requirement detail exposes `documentRequests`.
Frontend should render the exact plain-text `message` prominently, show `dueDate`, and offer its Upload New DocumentVersion action.
Upload uses the existing DocumentVersion endpoint; frontend styling is not implemented here. Comment `message` accepts 1–2000 characters;
only OPEN requests accept new comments. Existing comments and reviews remain append-only.

Review, request, audit and web notification are saved atomically before delivery. Email content is HTML-encoded.
The API exposes `emailSent` and `deliveryWarning`: SMTP failure or a two-second delivery timeout never loses the committed request.
A timeout means delivery was not confirmed; SMTP may finish afterward. No automatic email retry worker is added.
The two-second end-to-end target still requires measurement with real PostgreSQL, SMTP and realtime delivery.

Apply migration `20261003000000_AddDocumentRequestRevisions` before using these endpoints:

```bash
dotnet ef database update --project Pegaxus.Repository --startup-project Pegaxus.API
```

## Missing-document reminders (UC-25)

Both endpoints require a Bearer token and the enum-backed `TransportSpecialistPolicy`; the service also requires an active TS account.

| Endpoint | Result |
| :--- | :--- |
| GET `/api/v1/compliance-dossiers/{dossierId}/missing-requirements` | Selectable missing requirements |
| POST `/api/v1/compliance-dossiers/{dossierId}/missing-document-reminders` | Update selected deadlines/statuses and send a customer reminder |

POST accepts `requirementIds` (1–100 distinct IDs belonging to this dossier), `dueDate` (ISO date),
and optional `note` (at most 2000 characters). The deadline must be today or later in UTC and no later than departure.
Only IN_PREPARATION dossiers are eligible; cancelled/rejected bookings are excluded.
Missing means a nondeleted PENDING or REQUESTED requirement with no uploaded file in any document version.
PROVIDED, SATISFIED, WAIVED and REJECTED requirements are excluded; rejected-document revision belongs to UC-24.
An invalid or already-uploaded selection rejects the entire operation, without partially changing other items.

The explicit TS action changes selected requirements to REQUESTED, sets their deadlines, creates or updates their OPEN
MISSING_DOCUMENT requests, and saves audit entries, a web notification and an email outbox record atomically.
The response contains `reminderId`, updated `requirements`, `emailSent` and any `deliveryWarning`.
The SMTP attempt has a two-second cancellation budget; failure keeps the durable email queued for retry instead of losing the reminder.

`MissingDocumentReminderWorker` runs on API startup and every minute. Automatic reminders are eligible within 21 days for
domestic trips (same normalized origin/destination country) or 60 days for cross-border trips; departed trips are excluded.
At most one automatic reminder per dossier per UTC day is persisted. A manual reminder that day or a pending unsent email
suppresses another automatic reminder. Existing requirement deadlines appear in the message; a missing deadline defaults to today.
Automatic reminders send email/web notifications but do not change requirements, deadlines or dossier submission status.

Email failures retry after five minutes, including after API restarts. Retries recheck dossier eligibility and selected missing items;
obsolete reminders are cancelled, uploaded items are omitted, and no new DocumentRequest or web notification is created for a retry.
Database uniqueness and row locks protect multi-instance scheduling/delivery. SMTP delivery is at-least-once: a process crash after
SMTP accepts email but before its database acknowledgement may produce a duplicate email. This does not duplicate the request or notification.
The under-three-second target still requires measurement with real PostgreSQL, SMTP and realtime delivery.

Apply migration `20261004100000_AddMissingDocumentReminders` before using the API/worker:

```bash
dotnet ef database update --project Pegaxus.Repository --startup-project Pegaxus.API
```

## 👥 8. License & Academic Attribution

Developed as a capstone software engineering project for **SWP391** (Software Development Project).  
Distributed under the [Apache-2.0 License](./LICENSE).
