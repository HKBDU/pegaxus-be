# Coding Conventions — Pegaxus Backend

**Audience**: AI agents (and humans) adding or changing features.
**Rule of thumb**: copy the nearest existing module. Do not invent a new structure.
History of how the code got here: [REFACTOR_LOG.md](REFACTOR_LOG.md).

## 1. Architecture at a glance

```
Pegaxus.API         HTTP: controllers, auth policies, middleware, DI wiring, Swagger, SignalR hubs, background workers
Pegaxus.Service     Business logic: one folder (= namespace) per module, plus Common/ and Exceptions/
Pegaxus.Repository  EF Core: AppDbContext, entities, configurations, migrations, DB helper extensions
Pegaxus.Service.Test  xUnit tests (InMemory by default, PostgreSQL tier opt-in)
```

Dependencies point one way: `API → Service → Repository`. The Service layer never references `Pegaxus.API`.

- .NET 8, ASP.NET Core Web API, EF Core + PostgreSQL (Npgsql), Redis cache, SignalR, FluentValidation, JWT.
- **No repository / unit-of-work / MediatR / CQRS.** Services inject `AppDbContext` and query it directly.
- Shared build settings are in `Directory.Build.props` (`net8.0`, `ImplicitUsings`, `Nullable`). Do not repeat them in `.csproj` files.

## 2. Adding a feature module (Service layer)

Folder: `Pegaxus.Service/<Name>Service/`. Namespace: `Pegaxus.Service.<Name>Service`.

```
<Name>Service/
  I<Name>Service.cs   public interface I<Name>Service
  Service.cs          public sealed partial class Service : I<Name>Service   (ctor, fields, small shared helpers)
  <UseCase>.cs        partial class Service — one file per cohesive group of use cases
  Request.cs          public static class Request   { public class CreateRequest ... }
  Response.cs         public static class Response  { public class DetailResponse ... }
  Validator/          FluentValidation validators (existing modules also use `Validations/`; prefer `Validator/`)
```

Rules:

1. The implementation class is always named `Service`; the interface is `I<Name>Service`. Callers alias the namespace: `using Vehicles = Pegaxus.Service.VehicleService;` then `Vehicles.IVehicleService`, `Vehicles.Request.ListRequest`.
2. Keep `Service.cs` small. When it passes roughly 300 lines, move use cases into **partial class files named after the use case** (`Queries.cs`, `Lifecycle.cs`, `Payments.cs`, `Locking.cs`, `Notifications.cs`, `Mapping.cs`, `Sorting.cs`, ...). Splits are pure moves: do not change signatures or logic while moving.
3. Inject `AppDbContext db` plus collaborators (`TimeProvider clock`, `INotificationService`, `IMailService`, validators). Primary constructors and classic constructors both exist; match the file you edit. Never add a second "convenience" constructor that builds dependencies by hand — everything comes from DI.
4. Register the service in `Pegaxus.API/Extensions/ServiceCollectionExtensions.cs` inside the matching `Add<Group>Module` method (or add a new group and call it from `Program.cs`).
5. Throw domain exceptions from `Pegaxus.Service.Exceptions` (section 5); never return HTTP results from the Service layer.
6. Log with `ILogger<Service>`; never `Console.*`.

## 3. Database access rules

Use the shared helpers; never re-implement them.

| Need | Use |
|---|---|
| Serializable transaction | `await db.Database.BeginSerializableAsync(ct)` (or `BeginSerializableOrNoneAsync`) from `Pegaxus.Repository.Extensions` |
| Row lock (`SELECT ... FOR UPDATE` equivalent) | `await db.LockRowsAsync(db.X.Where(...), x => x.XId, ct)` |
| Write-conflict detection | `PostgresErrors.IsWriteConflict(ex)` |
| Active employees | `db.Employees.Active()` |
| UTC "now" for `timestamp without time zone` | `DbTime.UtcNow()` / `DbTime.UtcNowTruncated()` (`using static Pegaxus.Service.Common.DbTime;`) |
| Truncate a timestamp for optimistic-concurrency compare | `DbTime.Truncate(...)` (microsecond precision) |
| Stored value → `DateTimeOffset` | `DbTime.AsUtc(...)` |
| Blank string → null | `Text.EmptyToNull(value)` |
| Page count | `Paging.TotalPages(total, pageSize)` |
| Validate and throw 400 | `Validation.Validate(request, validator)` |

Behavior that must be preserved when editing write paths:

- **Order**: validate → begin Serializable transaction → lock rows → reload tracked entities after the lock (`db.Entry(x).ReloadAsync`) → check version/status → mutate → `SaveChangesAsync` → `CommitAsync` → flush notifications. Do not reorder.
- **Lock order must be consistent** across code paths to avoid deadlocks (for route plans: Transport → TransportPlan → RoutePlan → segments ordered by id → dossier). When adding a new multi-row lock, lock in a stable order (ordered ids).
- **Optimistic concurrency**: compare the request's `LastUpdatedAt` with the entity's `UpdatedAt` after truncating both; a mismatch is a `ConflictException` with a module-specific code such as `VEHICLE_MODIFIED`.
- **Notifications / mail** are queued in the same transaction (`NotificationService`, `OutboxQueue.AddEmail`) and flushed after commit (`await notifications.FlushAsync(ct)`). Do not send mail inside the transaction.
- Helpers must stay safe for the InMemory provider used by unit tests (they are no-ops or fall back when `!IsRelational()`).

## 4. Repository layer

- Entities: `Pegaxus.Repository/Entities/<Entity>.cs`. Enums: `Pegaxus.Repository/Enum/` (also mapped as native PostgreSQL enums).
- Add a `DbSet` to `AppDbContext`. Put **all** mapping in `Pegaxus.Repository/Configurations/<Entity>Configuration.cs` implementing `IEntityTypeConfiguration<T>`. They are picked up automatically by `ApplyConfigurationsFromAssembly`. Do not configure entities inline in `AppDbContext`.
- New enum type: add it to `PostgresEnumConfiguration.HasPostgresEnums()` and (for default/index handling) to `AppDbContext.EnumColumns.cs` following the existing pattern.
- Column names are snake_case (`HasColumnName("created_at")`), tables PascalCase, timestamps `timestamp without time zone`.

### Migrations

- Create with `dotnet ef migrations add <Name> --project Pegaxus.Repository --startup-project Pegaxus.API`.
- A migration file **must** carry `[DbContext(typeof(AppDbContext))]` and `[Migration("<timestamp>_<Name>")]`. If you hand-write one, add both attributes and update `AppDbContextModelSnapshot.cs`. `MigrationDiscoveryTests` fails otherwise.
- Never edit a migration that has been merged/applied. Add a new one.
- The API runs `MigrateAsync()` at startup, so a merged migration reaches every environment on deploy. Call out data-lossy changes (precision, drops) in the PR.

## 5. Errors

Throw from the Service layer; `GlobalExceptionHandlerMiddleware` maps them to the response envelope.

| Exception | HTTP | Use |
|---|---|---|
| `BadRequestException` | 400 | Invalid input / state transitions requested wrongly |
| `FieldValidationException` | 400 | Per-field errors (carries `FieldError[]`) |
| `UnauthorizedException` | 401 | Missing/inactive account |
| `ForbiddenException` | 403 | Role or ownership check failed |
| `NotFoundException` | 404 | Resource missing or not visible to caller |
| `ConflictException` | 409 | Duplicate, stale version, concurrent change |
| `UnprocessableEntityException` | 422 | Business rule violation |
| `TooManyRequestsException` | 429 | Rate limits |

Always pass a stable **upper-snake `errorCode`** as the second argument (`"INVOICE_NOT_FOUND"`); clients depend on it. Messages are English sentences. Do not change an existing code or message in a refactor.

## 6. API layer

- Controller: `Pegaxus.API/Controllers/<Plural>Controller.cs`, `[ApiController]`, `[Route("api/v1/<kebab-plural>")]`, derive from **`ApiControllerBase`**.
- Authorization: `[Authorize(Policy = JwtExtensions.<X>Policy)]`; reuse an existing policy, add a new constant in `JwtExtensions` only when roles differ.
- Account id: `AccountId` (property on the base). Never parse claims in a controller.
- Responses: `Success(value, "message")` for 200, `SuccessCreated(value, "message")` for 201, or `CreatedAtAction(...)` with `ResponseBuilder.SuccessResponse(..., traceId: HttpContext.TraceIdentifier)`. Errors come from exceptions, not hand-built results.
- Request validation at the controller: inject `[FromServices] IValidator<T> validator` and call `ValidateRequestAsync(request, validator, ct)` from the base. Validation inside the service via `Validation.Validate` is also used; keep the style of the module you are in.
- Every action has XML docs: `<summary>`, `<remarks>`, `<param>`, `<returns>` (and `<exception>` where relevant). Swagger text comes from them.
- Controllers stay thin: map request → one service call → envelope. No EF, no business rules.
- A controller over ~300 lines is split into partial files named `<Controller>.<Group>.cs` (class attributes stay on the main file).
- Do not change routes, HTTP verbs, status codes or JSON property names of existing endpoints.

## 7. Style rules

- Namespaces are file-scoped (`namespace X;`). Nullable reference types are enabled: do not suppress warnings with `!` unless a check just proved non-null.
- Usings: `System.*` first, then other namespaces alphabetically, blank line, then `using static ...`, blank line, then aliases. Remove unused usings.
- `async` methods take a `CancellationToken` as the last parameter and pass it through; suffix `Async`.
- Prefer LINQ with `AsNoTracking()` for reads and projection (`Select`) to DTOs over loading graphs.
- Prefer the shared helpers in section 3 over local copies. If you must add a new helper, put it in `Pegaxus.Service/Common/` (pure logic) or `Pegaxus.Repository/Extensions/` (EF/DB logic).
- Keep comments for *why* (concurrency, lock order, rounding). No restating the code.
- One module's types must not depend on another module's private members; cross-module calls go through the other module's `I<Name>Service`.
- Code and identifiers are English. Commit/PR titles follow Conventional Commits (CI enforces `type(scope): subject` and branch names like `feat/<kebab-case>`).

## 8. Tests

Location: `Pegaxus.Service.Test/<Module>/`. Framework: xUnit.

| Tier | How | When to use |
|---|---|---|
| Unit / service | `TestSupport.CreateDb()` (EF InMemory) + helpers `AddCustomerAsync`, `AddLogisticsManagerAsync`, `AddPendingBookingAsync`, `CreateNotifications` | Default for business rules |
| HTTP contract | Test host with the real controller (`*HttpTests`) | Status codes, JSON shape, auth |
| PostgreSQL | `[PostgresFact]` and `UseTestNpgsql(source)` | Locks, serializable conflicts, migrations, SQL translation |

PostgreSQL tests run only when `PEGAXUS_TEST_CONNECTION_STRING` is set (CI job `backend-postgres` sets it). Rules for them:

- Use `NpgsqlTestOptions.UseTestNpgsql(source)` instead of `UseNpgsql(source)` so EF's "many service providers" warning does not fail the suite.
- Each test creates its own database or schema and drops it in `finally`.
- Tests that create a schema in the shared database must carry `[Collection("PostgresSharedDatabase")]`.
- Use the shared `PostgresFactAttribute` (defined in `Common/LinqConcurrencyTests.cs`) for new tests.
- A new migration needs no new test, but `MigrationDiscoveryTests` and `Migration_UpDownUp_AndSnapshotMatchCurrentModel` must stay green.

Before opening a PR:

```bash
dotnet build Pegaxus.sln -c Release
dotnet test  Pegaxus.sln -c Release
# optional but recommended for DB-touching changes
PEGAXUS_TEST_CONNECTION_STRING="Host=localhost;Port=5432;Username=postgres;Password=postgres;Database=postgres" dotnet test Pegaxus.sln -c Release
```

## 9. Checklist for a new endpoint

1. DTOs in the module's `Request.cs` / `Response.cs`; validator in `Validator/`.
2. Method on `I<Name>Service` and implementation in the right partial file.
3. DB writes: serializable transaction, lock, reload, version check, save, commit, flush (section 3).
4. Domain exceptions with stable `errorCode`s.
5. Controller action with policy, XML docs, `Success(...)`.
6. Register new services in `ServiceCollectionExtensions`.
7. Tests: rule tests (InMemory), HTTP contract test, and a PostgreSQL test if you added locking/concurrency/SQL.
8. Build and test pass; no unrelated formatting churn.

## 10. Things not to do

- Do not add repository classes, a unit-of-work, MediatR, or generic base services.
- Do not call `new Service(...)` or construct dependencies manually outside tests.
- Do not copy-paste time/paging/lock/transaction helpers.
- Do not edit applied migrations or hand-edit the snapshot without a matching migration.
- Do not mix refactor and bug-fix in one commit; fixes get their own `fix:` commit with a regression test.
- Do not read or print `.env` (secrets).
