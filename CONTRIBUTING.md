# ðŸ¤ Contributing to Pegaxus Backend (`pegaxus-be`)

Welcome to the **Pegaxus Backend** development team! This guide provides detailed guidelines on code style, architectural boundaries, commit conventions, and the Pull Request review workflow.

---

## ðŸ“‹ Table of Contents
1. [Quickstart Setup](#1-quickstart-setup)
2. [Branching & Commit Standards](#2-branching--commit-standards)
3. [Pull Request Workflow](#3-pull-request-workflow)
4. [Automated CI Validation](#4-automated-ci-validation)
5. [Backend Coding Conventions](#5-backend-coding-conventions)
6. [Architecture & Layering Rules](#6-architecture--layering-rules)

---

## 1. Quickstart Setup

```bash
# 1. Clone repository
git clone https://github.com/HKBDU/pegaxus-be.git
cd pegaxus-be

# 2. Configure environment variables
Copy-Item .env.example .env

# 3. Restore NuGet dependencies and build solution
dotnet restore Pegaxus.sln
dotnet build Pegaxus.sln

# 4. Run database via Docker (PostgreSQL 16)
docker compose up postgres -d

# 5. Apply migrations and start the Web API
dotnet ef database update --project Pegaxus.Repository --startup-project Pegaxus.API
dotnet run --project Pegaxus.API
```
- Swagger UI will be available at: `http://localhost:5000/swagger`
- Health check endpoint: `http://localhost:5000/health`

---

## 2. Branching & Commit Standards

### 2.1. Branch Naming
All branches must branch from `dev` and follow:
```text
feat/<lowercase-name>   # For new endpoints/services
fix/<lowercase-name>    # For bug fixes
refactor/<name>         # For code cleanups
```
*Examples:* `feat/horse-booking-api`, `fix/jwt-expiration-handler`.

### 2.2. Conventional Commits
```text
<type>(<scope>): <short description>
```
Types: `feat`, `fix`, `refactor`, `perf`, `docs`, `test`, `chore`.  
*Examples:*
- `feat(dossier): add veterinary health check validation`
- `fix(route): correct coordinates parsing in route planner`
- `refactor(repo): optimize generic repository query filter`

---

## 3. Pull Request Workflow

1. Always target **`dev`** as the base branch.
2. Ensure `dotnet build` succeeds with **0 warnings and 0 errors**.
3. Format code properly using `dotnet format`.
4. Open PR with the standard template (`.github/PULL_REQUEST_TEMPLATE.md`).
5. Ensure both CI checks (`Repository conventions` and `Backend build & test`) pass.
6. Obtain at least **1 approving review** before merging.

---

## 4. Automated CI Validation

The `.github/workflows/quality.yml` pipeline validates every commit and PR:
- **`conventions`**: Validates branch name and PR title against regex rules.
- **`backend`**: Restores NuGet packages and verifies compilation with `dotnet build -c Release`.

---

## 5. Backend Coding Conventions

### 5.1. C# & .NET Guidelines
- Follow official Microsoft C# coding conventions.
- Use explicit async/await pattern (`Task<TResult>`) with cancellation tokens when appropriate.
- Prefer record types for immutable DTOs / Request / Response payloads.
- Use dependency injection for all services and repositories.

### 5.2. Naming Standards
- **Classes, Methods, Properties**: `PascalCase` (`HorseService`, `GetTransportPlanAsync`).
- **Private Fields**: `_camelCase` with leading underscore (`_context`, `_logger`).
- **Interfaces**: `IPascalCase` (`ITransportService`, `IGenericRepository<T>`).
- **Constants**: `PascalCase` or `UPPER_SNAKE_CASE` (`DefaultPageSize`, `MaxUploadSizeBytes`).

---

## 6. Architecture & Layering Rules

```text
Pegaxus.API/         --> Presentation Layer (Controllers, Middlewares, DI Setup)
     â†“
Pegaxus.Service/     --> Business Logic Layer (Services, DTOs, FluentValidators)
     â†“
Pegaxus.Repository/  --> Data Access Layer (Entities, DbContext, Configurations)
```

- **Controllers** should be thin; business logic belongs in `Pegaxus.Service`.
- **Entities** should never be directly returned to API clients; always map to DTOs.
- Database access should go through repository abstractions or DbContext configurations in `Pegaxus.Repository`.