# Pegaxus Swagger API Test Guide

This guide documents Swagger testing for the current Pegaxus source.

## Start API

~~~powershell
dotnet run --project .\Pegaxus.API --launch-profile http
~~~

- Swagger UI: http://localhost:5001/swagger/index.html
- OpenAPI JSON: http://localhost:5001/swagger/v1/swagger.json
- Health: http://localhost:5001/api/Health

## Authentication

1. Open Swagger UI.
2. Execute POST /api/v1/auth/login.
3. Copy value.accessToken.
4. Click Authorize.
5. Paste JWT only. Do not add Bearer.
6. Execute protected APIs.

Password for all seeded accounts: pegaxusDemo@123.

## Test accounts

| Role | Email |
|---|---|
| Customer | trinhnganquan@gmail.com |
| Customer | batquyquan@gmail.com |
| Customer | kimvetuan@gmail.com |
| Customer | khuyenvkse200481@fpt.edu.vn |
| Logistics Manager | diechahien@gmail.com |
| Transport Specialist | huyvog6226@gmail.com |
| Fleet Route Coordinator | hoanguyen91011@gmail.com |
| Driver | duongngo2209@gmail.com |
| Escort | quocbao.nguyen16102006@gmail.com |

## Detailed request guide

Use [SWAGGER_API_COPY_PASTE_REQUESTS.md](SWAGGER_API_COPY_PASTE_REQUESTS.md).

The request pack contains:

- Every controller endpoint grouped by business module.
- Purpose of each API.
- Role/account allowed to call the API.
- Complete JSON request bodies.
- Query parameters and path placeholders.
- Multipart form fields for uploads and proofs.
- End-to-end execution order.
- Positive and negative role tests.

## Main end-to-end order

1. Customer login.
2. Create horse.
3. Create booking.
4. Submit booking as PENDING.
5. LM approves booking.
6. LM creates Transport Plan.
7. Test Transport Plan list, detail and update.
8. TS/RC test approved booking visibility.
9. TS applies dossier template.
10. Customer uploads compliance document.
11. TS reviews document.
12. Driver/Escort submits quarantine proof.
13. Driver submits customs proof.
14. TS finalizes proof.
15. Check notifications using each recipient account.

## Common expected errors

- Missing/expired token: 401 UNAUTHORIZED.
- Wrong role: 403 FORBIDDEN.
- Unknown UUID: 404.
- Invalid status or business state: 400, 409 or 422 according to endpoint.
- Stale lastUpdatedAt: 409 TRANSPORT_PLAN_CONCURRENCY_CONFLICT.
- Duplicate Transport Plan: 409 TRANSPORT_PLAN_EXISTS.
- Empty list: 200 with items: [].

## Important current limitation

Assignment requires Transport Plan status APPROVED. TP1 creates DRAFT. Current source has no Transport Plan approve endpoint. Use an approved plan fixture or database test record before testing assignment and TS/RC visibility.

