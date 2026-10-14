# Refactor Log — `feat/refactor-system`

**Audience**: AI agents and maintainers who need to know *what changed, why, and what was deliberately left alone*.
**Companion file**: [CODING_CONVENTIONS.md](CODING_CONVENTIONS.md) — how new code must be written in the refactored structure.

## 1. Scope and ground rules

The refactor changed **structure, not behavior**. These constraints held for every behavior-neutral commit:

- API contract unchanged: routes, status codes, `errorCode`, messages, JSON field names, response envelope.
- Database behavior unchanged: isolation levels, lock order, and where `SaveChanges` / `Commit` / `FlushAsync` sit.
- No edits to already-applied EF migrations.
- No MediatR / CQRS / Clean-Architecture rewrite; no repository / unit-of-work layer (services use `AppDbContext` directly).
- Bugs found along the way were **not** hidden inside refactor commits. They were fixed in separate `fix:` commits (section 3).

Baseline = merge commit `9d9c293` (PR #152). Everything below is on top of it.

## 2. Commits

| Commit | Type | Summary |
|---|---|---|
| `2937eff` | refactor | Stage 1: shared helpers, controller base, DI extensions, `AppDbContext` split, service partials, build cleanup |
| `535fe3a` | test | Stabilize the PostgreSQL test tier |
| `94a3755` | refactor | Split large controllers and services into partial classes |
| `ad4f17e` | refactor | Rename `IService` → `I<Module>Service` (24 interfaces) |
| `791772f` | fix | Register `SetLocationCoordinatePrecision` migration with EF |
| `d8eb4d4` | fix | Return 400 instead of 500 for root-level validation failures |

### 2.1 `2937eff` — Stage 1

**Cross-cutting helpers (new)**

| File | What it replaces |
|---|---|
| `Pegaxus.Repository/Extensions/RowLockExtensions.cs` — `db.LockRowsAsync(rows, key, ct)` | ~50 hand-written "update key to itself" row-lock blocks. No-op on non-relational providers (InMemory). |
| `Pegaxus.Repository/Extensions/TransactionExtensions.cs` — `BeginSerializableAsync`, `BeginSerializableOrNoneAsync` | Every `IsRelational() ? Serializable : default` ternary and private `Begin*TransactionAsync` helper. |
| `Pegaxus.Repository/Extensions/PostgresErrors.cs` — `IsWriteConflict` | Duplicated predicates for concurrency / `23505` / `40001` / `40P01`. |
| `Pegaxus.Repository/Extensions/EmployeeQueryExtensions.cs` — `.Active()` | 48 copies of "not deleted, active, account not deleted, account ACTIVE". |
| `Pegaxus.Service/Common/DbTime.cs` | 40 duplicated time helpers (`UtcNow`, `Truncate`, `AsUtc`, ...). |
| `Pegaxus.Service/Common/Text.cs` — `EmptyToNull` | Duplicated string helpers. |
| `Pegaxus.Service/Common/Paging.cs` — `TotalPages` | 18 `Math.Ceiling(total / (double)pageSize)` copies. |
| `Pegaxus.Service/Common/Validation.cs` — `Validate<T>` | 4 identical `Validate<T>` methods (Vehicle keeps its own: it throws `FieldValidationException`). |

**API layer**

- `ApiControllerBase`: `AccountId`, `Success`, `SuccessCreated`. All controllers derive from it except `HealthController`.
- `ClaimsPrincipalExtensions.GetAccountId` replaces per-controller claim parsing.
- `ServiceCollectionExtensions` splits `Program.cs` into `AddApiControllers`, `AddFrontendCors`, `AddRealtime`, `AddPersistence`, `AddGoogleSignIn`, `AddRedisCache`, `AddErrorHandling` and the module groups `AddAuthModule`, `AddAccountsModule`, `AddBookingModule`, `AddInvoicingModule`, `AddTransportPlanningModule`, `AddFleetModule`, `AddComplianceModule`, `AddMessagingModule`. The 77 service registrations are identical to the old ones.
- `ApiResponse` / `ResponseBuilder` moved from `Pegaxus.Service.Models` to `Pegaxus.API/Models` (namespace `Pegaxus.API.Models`): the envelope is a presentation concern.

**Repository layer**

- `AppDbContext` shrank from 2,749 lines of inline fluent configuration to 135 lines. Each entity now has `Pegaxus.Repository/Configurations/<Entity>Configuration.cs` (`IEntityTypeConfiguration<T>`, 55 of them) applied with `ApplyConfigurationsFromAssembly`. Postgres enum registration lives in `PostgresEnumConfiguration.HasPostgresEnums()`.
- Removed unused `Pegaxus.Repository/Abstractions/` (`BaseEntity`, `IAuditableEntity`): no entity used them.

**Service layer**

- DI bypass removed: `BookingService` secondary constructor deleted; `TransportPlanService` and `RoutePlanService` require `NotificationService.INotificationService`; `RouteChangeService` receives `IRoutePlanService` through its primary constructor.
- `Console.Error.WriteLine` replaced by `ILogger` in document-request / missing-document code and `NotificationService`.
- Large services split into `partial class Service` files (pure moves, no logic edits):
  - `ComplianceDossierService`: `Dashboard`, `AuthoritySubmissions`, `QuarantineProofs`, `CustomsProofs`.
  - `BookingService`: `Queries`, `Horses`, `Lifecycle` (+ existing `Assignments`, `Negotiation`).
  - `InvoiceService`: `Payments`, `Commands`, `Queries`.
  - `TransportPlanService`: `Plans`, `Assignments`.
  - `RoutePlanService`: `Plans`, `Export`, `LegalReview`, `Validation`, `Mapping`.
  - Result (lines, baseline → now): `BookingService/Service.cs` 1,247 → 86; `InvoiceService/Service.cs` 977 → 27; `ComplianceDossierService/Service.cs` 1,936 → 104; `Program.cs` 305 → 97.

**Build / infra**

- `Directory.Build.props` holds `TargetFramework=net8.0`, `ImplicitUsings`, `Nullable`; removed from the four `.csproj` files.
- Removed unused package references (Cloudinary/DotNetEnv/JwtBearer/etc. where unused per project).
- `Dockerfile` copies `Directory.Build.props` and restores only `Pegaxus.API.csproj`.
- `.github/workflows/quality.yml`: added informational job `backend-postgres` (`continue-on-error: true`) running the suite against a Postgres 16 service with `PEGAXUS_TEST_CONNECTION_STRING`.
- Removed 247 unused `using` directives and sorted usings (System first, then alphabetical; `using static` and aliases in their own groups).

**Tests added**: `Common/RowLockExtensionsTests.cs` (non-relational no-op + a Postgres test proving the lock is held until the transaction ends).

### 2.2 `535fe3a` — Postgres test tier

Running the whole suite against PostgreSQL failed 18–19 tests (different ones each run). Two pre-existing causes, both test-only:

1. `ManyServiceProvidersCreatedWarning`: each test builds its own data source / interceptors, so EF creates one internal service provider per test and throws after 20. Fix: `Common/NpgsqlTestOptions.cs` → `UseTestNpgsql(source)` ignores that warning; used by all Postgres tests. Two `ReplaceService` InMemory tests ignore it too.
2. Tests that create a **schema** in the shared database ran in parallel and Npgsql saw several `account_type` enums. Fix: `[Collection("PostgresSharedDatabase")]` on `PersonnelAssignmentPostgresTests`, `RouteOutsourcingTests`, `TransportProviderServiceTests`, `VehiclePostgresTests`.

Result: full suite 1136/1136 against Postgres, repeated runs stable.

### 2.3 `94a3755` — Controllers and services split

Pure moves into partial classes:

- `ComplianceDossiersController` (838 lines at baseline, 752 after stage 1; now 55) → `.Reviews`, `.Documents`, `.Requirements`, `.Dashboard`, `.AuthoritySubmissions`, `.QuarantineProofs`, `.CustomsProofs`.
- `BookingController` (525 lines at baseline; now 23) → `.Invoices`, `.Assignments`, `.Horses`, `.Queries`, `.Lifecycle`.
- `Auth/Service.cs` (677 lines at baseline; now 77) → `Login`, `Tokens`, `Registration`, `PasswordReset`, `Otp`.
- `RouteChangeService/Service.cs` (459 lines at baseline; now 275) → `Locking`, `Notifications`, `Mapping`, `Comparison` (+ existing `Execution`).
- `BookingService/Queries.cs` (651 lines before the split, created in stage 1; now 242) → `ApprovedQueries`, `Sorting`, `Mapping`.

Verification: Swagger document identical when parsed as JSON (same paths, components, tags). Only the *order* of paths in `swagger.json` changed, because action discovery order follows partial-file order.

### 2.4 `ad4f17e` — Interface naming

All 24 module interfaces were called `IService`, distinguished only by namespace. Renamed to `I<Module>Service` (file `I<Module>Service.cs`, via `git mv` so history follows). Examples: `IBookingService`, `IAuthService`, `IInvoiceService`, `IMediaService` (implemented by `CloudinaryService.Service`). The implementation class is still called `Service` inside each module namespace. Controllers keep namespace aliases, e.g. `using Vehicles = Pegaxus.Service.VehicleService;`.

### 2.5 `791772f` — Migration fix (behavior change)

`20261011130000_SetLocationCoordinatePrecision` lacked `[DbContext]` and `[Migration]`, so EF never applied it; databases built from migrations kept `numeric(10,7)` while model and snapshot said `numeric(10,4)`. Added the attributes plus two guards:

- `MigrationDiscoveryTests`: every `Migration` subclass must carry both attributes.
- `Migration_UpDownUp_AndSnapshotMatchCurrentModel` now also asserts the column scale is 4.

**Operational note**: `Program.cs` calls `MigrateAsync()` on startup, so the migration runs on the next deploy and rounds existing `latitude` / `longitude` to 4 decimals. `Down` cannot restore the lost precision.

### 2.6 `d8eb4d4` — Validation helper (behavior change)

Three controllers had different private `ValidateRequestAsync` copies. They now share `ApiControllerBase.ValidateRequestAsync` / `FieldErrors`. `AuthController` keeps its `PASSWORD_MISMATCH` rule as `ValidateAuthRequestAsync`.

Bug fixed on the way: `PATCH /api/v1/bookings/{id}` with only `lastUpdatedAt` hit the root-level rule "At least one field must be updated." (property name `""`), `error.PropertyName[0]` threw `IndexOutOfRangeException` and the API returned **500**. It now returns **400 `VALIDATION_ERROR`** with `field: "$"`. Regression test: `Booking/BookingControllerValidationTests.cs`.

## 3. Verification used

- `dotnet build Pegaxus.sln -c Release`: 0 errors.
- `dotnet test`: 1105 passed / 33 skipped without Postgres; 1137 passed with `PEGAXUS_TEST_CONNECTION_STRING` set.
- Swagger and EF model compared against a pre-refactor baseline (temporary harness, deleted afterwards): EF model identical, Swagger identical apart from path order.
- A real PostgreSQL container was used for lock / transaction / migration tests.

## 4. Deliberately not done

| Item | Reason |
|---|---|
| Unify sort parsers (`ApplySorting` in Booking / TransportPlan / Employee / Vehicle / ...) | They differ in messages, supported fields, tie-breakers and single vs multi sort. A shared helper would need more parameters than the code it removes and risks changing the API contract. |
| Central package management (`Directory.Packages.props`) | Small value; not started. |
| Splitting `Employee`, `Horse`, `Account` services | Already small; no benefit. |
| Remove `continue-on-error` from `backend-postgres` | Wait for one green run on GitHub, then remove it (`quality.yml`). |

## 5. Known pre-existing issues left untouched (separate `fix:` work)

- Demo seed data (`AppDbContextSeedData.cs`) runs in every environment.
- Default employee password in the seed.
- Some workflow states are unreachable.
- Test helpers: `VehiclePostgresTests` still declares its own private `PostgresFactAttribute`; the shared one is in `Common/LinqConcurrencyTests.cs`.
- Validator folders are named inconsistently (`Validations/` vs `Validator/`); module folders are inconsistent (`Account`, `Auth`, `Employee`, `Horse` vs `*Service`).
- `README.md` lists folders approximately; this file and `CODING_CONVENTIONS.md` are more precise.
