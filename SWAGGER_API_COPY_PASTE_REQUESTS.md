# Pegaxus Swagger Copy-Paste Request Pack

Tài liệu hướng dẫn request copy-paste. Mở http://localhost:5001/swagger/index.html, bấm Try it out, thay placeholder rồi bấm Execute.

Password for every seeded account: pegaxusDemo@123.

## Ma trận account và mục đích API

| Tài khoản | Role | Mục đích kiểm thử |
|---|---|---|
| trinhnganquan@gmail.com | CUSTOMER | Quản lý ngựa, booking, upload hồ sơ, profile |
| diechahien@gmail.com | LOGISTICS_MANAGER | Duyệt booking, quản lý employee, Transport Plan |
| huyvog6226@gmail.com | TRANSPORT_SPECIALIST | Dossier, review document, compliance |
| hoanguyen91011@gmail.com | FLEET_ROUTE_COORDINATOR | Xem booking và plan được phân công |
| duongngo2209@gmail.com | DRIVER | Gửi quarantine proof và customs proof |
| quocbao.nguyen16102006@gmail.com | ESCORT | Gửi quarantine proof |

Use Authorize with JWT only. Do not type Bearer.

## Placeholder values

~~~text
<accessToken> <refreshToken> <bookingId> <horseId>
<transportPlanId> <employeeId> <dossierId> <requirementId>
<versionId> <reviewId> <requestId> <templateId>
<submissionId> <proofId> <notificationId> <updatedAt>
~~~

## 1. Auth API

Mục đích: kiểm tra đăng nhập, role trong JWT, refresh token, logout, đăng ký và OTP.


### Login

~~~http
POST /api/v1/auth/login
Content-Type: application/json

{
  "email": "diechahien@gmail.com",
  "password": "pegaxusDemo@123"
}
~~~

Replace email to test another role. Save accessToken and refreshToken.

### Refresh and logout

~~~json
{
  "refreshToken": "<refreshToken>"
}
~~~

**Use for POST /api/v1/auth/refresh-token. Use same body for POST /api/v1/auth/logout with current JWT.**

### Register

~~~json
{
  "fullName": "Swagger Customer",
  "email": "swagger.customer@example.com",
  "password": "pegaxusDemo@123",
  "confirmPassword": "pegaxusDemo@123"
}
~~~

**POST /api/v1/auth/register.**

### OTP requests

~~~json
{ "email": "swagger.customer@example.com" }
~~~

**POST /api/v1/auth/forgot-password.**

~~~json
{
  "email": "swagger.customer@example.com",
  "otp": "<otp>"
}
~~~

**POST /api/v1/auth/verify-email.**

~~~json
{
  "email": "swagger.customer@example.com",
  "purpose": "EMAIL_VERIFICATION"
}
~~~

**POST /api/v1/auth/resend-otp.**

~~~json
{
  "email": "swagger.customer@example.com",
  "otp": "<otp>",
  "newPassword": "pegaxusDemo@124",
  "confirmPassword": "pegaxusDemo@124"
}
~~~

**POST /api/v1/auth/reset-password.**

## 2. Account API

Mục đích: kiểm tra account chỉ đọc/sửa profile của chính mình và đổi mật khẩu.


~~~text
GET /api/v1/accounts/me
~~~

**PATCH /api/v1/accounts/me uses multipart form:**

~~~text
fullName       = Swagger User
dateOfBirth    = 1990-01-01
phone          = +33-000-000
address        = Paris, France
country        = FR
identityNumber = ID-001
avatar         = JPG/PNG file, max 2 MB
~~~

**PUT /api/v1/accounts/me/password body:**

~~~json
{
  "currentPassword": "pegaxusDemo@123",
  "newPassword": "pegaxusDemo@124",
  "confirmPassword": "pegaxusDemo@124"
}
~~~

## 3. Horse API

Mục đích: kiểm tra Customer tạo, xem, sửa, xóa horse và rule ownership.


~~~text
GET    /api/v1/horses?pageIndex=1&pageSize=10&search=Golden
GET    /api/v1/horses/<horseId>
DELETE /api/v1/horses/<horseId>
~~~

Create/update body:

~~~json
{
  "horseName": "Golden Thunder",
  "sex": "MALE",
  "breed": "Thoroughbred",
  "dateOfBirth": "2021-04-12",
  "color": "Bay",
  "countryOfOrigin": "FR",
  "weightKg": 520,
  "heightCm": 165,
  "microchipNumber": "985112003456781",
  "passportNumber": "FR-TB-2021-0457",
  "identificationNotes": "Hay only"
}
~~~

**Use body for POST /api/v1/horses and PUT /api/v1/horses/<horseId>.**

## 4. Booking API

Mục đích: kiểm tra vòng đời booking, quyền Customer, trạng thái và chống ghi đè khi update.


Create body:

~~~json
{
  "originAddress": "Haras de Chantilly, Paris",
  "originCountry": "FR",
  "destinationAddress": "Newmarket Stables, London",
  "destinationCountry": "GB",
  "requestedDepartureDate": "2026-10-27",
  "requestedArrivalDate": "2026-11-01",
  "horses": [
    {
      "horseId": "<horseId>",
      "specialHandlingNotes": "Hay only"
    }
  ],
  "specialRequirements": "Temperature monitoring",
  "customerNote": "Call before loading"
}
~~~

**Customer: POST /api/v1/bookings. Save bookingId and updatedAt.**

Read:

~~~text
GET /api/v1/bookings?pageIndex=1&pageSize=10&search=REQ-2026&status=DRAFT&sortBy=createdAt:desc
GET /api/v1/bookings/<bookingId>
GET /api/v1/bookings/<bookingId>/histories
~~~

Update body:

~~~json
{
  "lastUpdatedAt": "<updatedAt>",
  "originAddress": "Updated origin",
  "originCountry": "FR",
  "destinationAddress": "Updated destination",
  "destinationCountry": "GB",
  "requestedDepartureDate": "2026-10-29",
  "requestedArrivalDate": "2026-11-03",
  "specialRequirements": "Temperature monitoring",
  "customerNote": "Updated note"
}
~~~

**Customer: PATCH /api/v1/bookings/<bookingId>.**

Horse add body:

~~~json
{
  "horseId": "<horseId>",
  "specialHandlingNotes": "Hay only"
}
~~~

**POST /api/v1/bookings/<bookingId>/horses.**

Horse note update body:

~~~json
{ "specialHandlingNotes": "Temperature controlled handling" }
~~~

**PATCH /api/v1/bookings/<bookingId>/horses/<horseId>. Remove uses DELETE on same route.**

Status bodies:

~~~json
{ "status": "PENDING" }
~~~

**Customer: PATCH /api/v1/bookings/<bookingId>/status.**

~~~json
{ "reason": "Customer cancelled request" }
~~~

**Customer: POST /api/v1/bookings/<bookingId>/cancel. LM uses same body for POST /reject.**

~~~text
POST /api/v1/bookings/<bookingId>/approve
~~~

LM approve has no body.

~~~json
{
  "status": "CANCELLED",
  "reason": "Capacity unavailable"
}
~~~

**LM: PATCH /api/v1/bookings/<bookingId>/negotiated-status.**

Approved list/detail purpose: test only APPROVED records and UC-32 filters.

~~~text
GET /api/v1/bookings/approved?pageIndex=1&pageSize=20&search=REQ-2026&isInternational=true&transportPlanStatus=APPROVED&routePlanStatus=DRAFT&hasRoutePlan=false&hasTransportPlan=true&hasComplianceDossier=false&complianceDossierStatus=DRAFT&departureFrom=2026-10-20&departureTo=2026-11-30&sortBy=requestedDepartureDate:asc
GET /api/v1/bookings/approved/<bookingId>
~~~

LM, TS, RC. TS/RC need assignment for related plans.

Assignment purpose: test LM assignment stored in EmployeeTransportPlan and TS/RC visibility.

~~~json
{
  "employeeId": "<employeeId>",
  "role": "TRANSPORT_SPECIALIST",
  "note": "Compliance dossier owner"
}
~~~

~~~text
GET    /api/v1/bookings/<bookingId>/assignments
PUT    /api/v1/bookings/<bookingId>/assignments
DELETE /api/v1/bookings/<bookingId>/assignments/<employeeId>
~~~

Valid roles: TRANSPORT_SPECIALIST, FLEET_ROUTE_COORDINATOR.

## 5. Transport Plan API

Mục đích: kiểm tra TP1 tạo, TP2 danh sách, TP3 chi tiết, TP4 sửa, phân quyền và notification.

Purpose: test TP1 create, TP2 list, TP3 detail, TP4 update, access rules and notifications.

TP1 create body:

~~~json
{
  "plannedStartAt": "2026-10-27T06:00:00Z",
  "plannedEndAt": "2026-11-01T18:00:00Z",
  "estimatedDistanceKm": 920.5,
  "estimatedDurationMinutes": 1260,
  "planningNote": "Customer requests temperature monitoring"
}
~~~

**LM: POST /api/v1/bookings/<bookingId>/transport-plans. Booking must be APPROVED. Expected: 201, plan DRAFT, Transport PLANNING.**

TP2 list:

~~~text
GET /api/v1/transport-plans?pageIndex=1&pageSize=20&status=APPROVED&search=TP-2026&routePlanStatus=DRAFT&sortBy=createdAt:desc
GET /api/v1/transport-plans?pageIndex=1&pageSize=20&sortBy=plannedStartAt:asc
~~~

LM sees approved-booking plans. TS/RC see assigned plans.

TP3 detail:

~~~text
GET /api/v1/transport-plans/<transportPlanId>
~~~

Test booking context, Transport, assignedEmployees and approvedBy.

TP4 update body:

~~~json
{
  "lastUpdatedAt": "<updatedAt from TP3>",
  "updateReason": "Customer moved departure by 2 days",
  "plannedStartAt": "2026-10-29T06:00:00Z",
  "plannedEndAt": "2026-11-03T18:00:00Z",
  "estimatedDistanceKm": 950.5,
  "estimatedDurationMinutes": 1320,
  "planningNote": "Updated schedule"
}
~~~

**LM: PUT /api/v1/transport-plans/<transportPlanId>. Expected: 200, new updatedAt and notifications.**

## 6. Employee API

Mục đích: kiểm tra LM quản lý employee, filter, role/status và export XLSX.


~~~text
GET /api/v1/employees?pageIndex=1&pageSize=10&search=huy&role=TRANSPORT_SPECIALIST&accountStatus=ACTIVE&workingStatus=AVAILABLE&sortBy=createdAt:desc
GET /api/v1/employees/<employeeId>
GET /api/v1/employees/export?pageIndex=1&pageSize=100&role=TRANSPORT_SPECIALIST&accountStatus=ACTIVE&sortBy=createdAt:desc
~~~

Create body:

~~~json
{
  "role": "TRANSPORT_SPECIALIST",
  "email": "new.ts@example.com",
  "phone": "+33-000-000",
  "fullName": "New Transport Specialist"
}
~~~

**POST /api/v1/employees.**

~~~json
{
  "role": "FLEET_ROUTE_COORDINATOR",
  "workingStatus": "AVAILABLE"
}
~~~

**PATCH /api/v1/employees/<employeeId>.**

~~~json
{ "status": "ACTIVE" }
~~~

**PATCH /api/v1/employees/<employeeId>/status.**

## 7. Notifications API

Mục đích: kiểm tra notification chỉ thuộc account nhận và trạng thái đã đọc.


~~~text
GET /api/v1/notifications?pageIndex=1&pageSize=20&unreadOnly=true
GET /api/v1/notifications/unread-count
PATCH /api/v1/notifications/<notificationId>/read
PATCH /api/v1/notifications/read-all
~~~

Use each account token. One account must not read another account notification.

## 8. Upload API

Mục đích: kiểm tra upload signature, validate file và metadata tin cậy.


~~~json
{
  "entity": "ComplianceDocument",
  "contentType": "application/pdf",
  "sizeBytes": 1048576
}
~~~

**POST /api/v1/uploads/signature.**

~~~json
{ "publicId": "<publicId after Cloudinary upload>" }
~~~

**POST /api/v1/uploads/confirm.**

## 9. Dossier Template API

Mục đích: kiểm tra TS tạo/sửa template, thứ tự checklist và kích hoạt template.


~~~text
GET /api/v1/dossier-templates?pageIndex=1&pageSize=10&search=EU&status=ACTIVE
GET /api/v1/dossier-templates/<templateId>
DELETE /api/v1/dossier-templates/<templateId>
~~~

Create/update body:

~~~json
{
  "templateCode": "TPL-EU-HORSE-2026",
  "templateName": "EU Horse Transport",
  "description": "Base EU transport dossier",
  "status": "DRAFT",
  "effectiveFrom": "2026-01-01",
  "effectiveTo": "2026-12-31",
  "items": [
    {
      "complianceRequirementId": "<requirementId>",
      "sequenceNo": 1,
      "isMandatory": true,
      "appliesPerHorse": true,
      "instruction": "Upload valid horse passport"
    }
  ]
}
~~~

**POST /api/v1/dossier-templates and PUT /api/v1/dossier-templates/<templateId>.**

~~~json
{ "status": "ACTIVE" }
~~~

**PATCH /api/v1/dossier-templates/<templateId>/status.**

## 10. Compliance Requirement API

Mục đích: kiểm tra catalog requirement, duplicate code, reference impact và status.


~~~text
GET /api/v1/compliance-requirements?pageIndex=1&pageSize=10&search=passport&country=FR&bcpCode=BCP-FR-01&category=HorseIdentity&status=ACTIVE&sortBy=updatedAt:desc
GET /api/v1/compliance-requirements/<requirementId>
DELETE /api/v1/compliance-requirements/<requirementId>
~~~

Create/update body:

~~~json
{
  "requirementCode": "REQ-HORSE-PASSPORT",
  "requirementName": "Horse passport",
  "country": "FR",
  "category": "HorseIdentity",
  "status": "ACTIVE",
  "bcpCode": "BCP-FR-01",
  "issuingAuthority": "French Authority",
  "description": "Valid horse passport",
  "legalBasis": "EU transport rule",
  "validityPeriodDays": 365,
  "validityRequired": true,
  "acknowledgeReferenceImpact": false
}
~~~

**POST /api/v1/compliance-requirements and PUT /api/v1/compliance-requirements/<requirementId>.**

~~~json
{ "status": "INACTIVE" }
~~~

**PATCH /api/v1/compliance-requirements/<requirementId>/status.**

## 11. Compliance Dossier API

Mục đích: kiểm tra Apply Template, checklist, document, TS review, dashboard và audit.


View:

~~~text
GET /api/v1/bookings/<bookingId>/compliance-dossier?status=IN_PREPARATION&horseId=<horseId>
GET /api/v1/compliance-dossiers/<dossierId>?status=IN_PREPARATION&horseId=<horseId>
GET /api/v1/compliance-dossiers/<dossierId>/requirements/<requirementId>
GET /api/v1/compliance-dossiers/<dossierId>/requirements/<requirementId>/document/versions
GET /api/v1/compliance-dossiers/<dossierId>/requirements/<requirementId>/versions/<versionId>/preview
GET /api/v1/compliance-dossiers/<dossierId>/requirements/<requirementId>/versions/<versionId>/attachments/<attachmentId>/preview
GET /api/v1/compliance-dossiers/<dossierId>/authority-submissions
~~~

Template body:

~~~json
{
  "dossierTemplateId": "<templateId>",
  "bookingUpdatedAt": "<booking.updatedAt>",
  "items": [
    {
      "complianceRequirementId": "<requirementId>",
      "isMandatory": true,
      "appliesPerHorse": true,
      "instruction": "Upload valid passport"
    }
  ],
  "note": "Initial dossier setup"
}
~~~

**TS uses body for POST /api/v1/bookings/<bookingId>/compliance-dossier/preview and POST /api/v1/bookings/<bookingId>/compliance-dossier.**

Add requirement:

~~~json
{
  "complianceRequirementId": "<requirementId>",
  "horseId": "<horseId>",
  "isMandatory": true,
  "dueDate": "2026-10-20",
  "leadTimeDays": 14,
  "instruction": "Upload original document",
  "reason": "Additional route requirement"
}
~~~

**POST /api/v1/compliance-dossiers/<dossierId>/requirements.**

Update requirement:

~~~json
{
  "isMandatory": true,
  "leadTimeDays": 7,
  "instruction": "Upload renewed document",
  "reason": "Requirement changed"
}
~~~

**PATCH /api/v1/compliance-dossiers/<dossierId>/requirements/<requirementId>.**

Waive/delete:

~~~json
{ "waivedReason": "Not applicable for this route" }
~~~

**PATCH /api/v1/compliance-dossiers/<dossierId>/requirements/<requirementId>/waive.**

~~~json
{ "reason": "Duplicate requirement" }
~~~

**DELETE /api/v1/compliance-dossiers/<dossierId>/requirements/<requirementId>.**

~~~text
GET  /api/v1/compliance-dossiers/<dossierId>/missing-requirements
POST /api/v1/compliance-dossiers/<dossierId>/ready-for-submission
~~~

Missing reminder:

~~~json
{
  "requirementIds": ["<requirementId>"],
  "dueDate": "2026-10-20",
  "note": "Please upload missing documents"
}
~~~

**POST /api/v1/compliance-dossiers/<dossierId>/missing-document-reminders.**

Document upload/version:

~~~json
{
  "documentNumber": "FR-PASSPORT-001",
  "issuingAuthority": "French Authority",
  "issuedDate": "2026-01-01",
  "expiryDate": "2027-01-01",
  "files": [{ "publicId": "<confirmedPublicId>" }],
  "uploadNote": "Original passport scan"
}
~~~

**Customer uses body for POST /api/v1/compliance-dossiers/<dossierId>/requirements/<requirementId>/document and /document/versions.**

Metadata update:

~~~json
{
  "documentNumber": "FR-PASSPORT-001-UPDATED",
  "issuingAuthority": "French Authority",
  "issuedDate": "2026-01-01",
  "expiryDate": "2027-01-01"
}
~~~

**PATCH /api/v1/compliance-dossiers/<dossierId>/requirements/<requirementId>/document/metadata.**

Document approve:

~~~json
{
  "metadataConfirmed": true,
  "versionUploadedAt": "2026-10-06T08:00:00Z",
  "documentUpdatedAt": "2026-10-06T08:00:00Z",
  "reviewNote": "Document valid",
  "expiryExceptionReason": null
}
~~~

Document reject:

~~~json
{
  "errorCategory": "UNCLEAR_IMAGE",
  "rejectionNote": "Upload a clearer scan",
  "revisionDeadline": "2026-10-20",
  "requirementStatus": "REJECTED",
  "dossierStatus": "IN_PREPARATION",
  "versionUploadedAt": "2026-10-06T08:00:00Z",
  "documentUpdatedAt": "2026-10-06T08:00:00Z"
}
~~~

Revoke review:

~~~json
{
  "status": "PROVIDED",
  "reason": "Approval needs correction"
}
~~~

~~~text
POST /api/v1/compliance-dossiers/<dossierId>/requirements/<requirementId>/versions/<versionId>/approve
POST /api/v1/compliance-dossiers/<dossierId>/requirements/<requirementId>/versions/<versionId>/reject
POST /api/v1/compliance-dossiers/<dossierId>/requirements/<requirementId>/versions/<versionId>/reviews/<reviewId>/revoke
~~~

Comments:

~~~json
{ "message": "Please upload a clearer document." }
~~~

**POST and GET /api/v1/compliance-dossiers/<dossierId>/requirements/<requirementId>/document-requests/<requestId>/comments.**

Dashboard/audit:

~~~text
GET /api/v1/compliance-dossiers/dashboard?pageIndex=1&pageSize=20&status=IN_PREPARATION&risk=HIGH&routeType=INTERNATIONAL&sortBy=requestedDepartureDate:asc&search=REQ
GET /api/v1/compliance-dossiers/dashboard/export?format=XLSX&status=IN_PREPARATION
GET /api/v1/compliance-dossiers/<dossierId>/audit-history?actionType=DOCUMENT_REJECTED&search=passport&from=2026-01-01T00:00:00Z&to=2026-12-31T23:59:59Z&pageIndex=1&pageSize=20&sortBy=occurredAt:desc
~~~

## 12. Authority submission

Mục đích: kiểm tra TS ghi nhận hồ sơ gửi cơ quan, file bằng chứng và decision.


Create multipart fields:

~~~text
POST /api/v1/compliance-dossiers/<dossierId>/authority-submissions

authorityName       = French Veterinary Authority
submissionReference = SUB-2026-001
submissionMethod    = PORTAL
submittedAt         = 2026-10-06T08:00:00Z
isRequired          = true
proofFile           = choose PDF/JPG/PNG
decisionNote        = Submitted for review
~~~

Decision multipart fields:

~~~text
PATCH /api/v1/authority-submissions/<submissionId>/decision

decision          = APPROVED
permitNumber      = PERMIT-001
permitIssuedDate  = 2026-10-07
permitExpiryDate  = 2027-10-07
decisionFile      = choose PDF/JPG/PNG
decisionNote      = Approved by authority
rejectionReason   = blank for APPROVED
revisionDueDate   = blank for APPROVED
~~~

## 13. Quarantine proof

Mục đích: kiểm tra Driver/Escort gửi proof và TS verify hoặc reject proof.


~~~text
POST /api/v1/compliance-dossiers/<dossierId>/quarantine-proofs

bcpName        = Calais BCP
locationId     = <locationId> or blank
inspectionAt   = 2026-10-27T10:00:00Z
isHoldback     = false
quickNote      = Microchip checked
files          = choose one or more PDF/JPG/PNG
~~~

~~~text
GET /api/v1/quarantine-proofs/<proofId>
PATCH /api/v1/quarantine-proofs/<proofId>/finalize

action             = VERIFY
officialVetName    = Dr. Example
bcpReceiptNo       = BCP-001
status             = PASSED
conditions         = Keep temperature log
microchipChecklist = ALL_MATCHED
rejectionReason    = blank for VERIFY
~~~

Reject test: set action to REJECT_PROOF and fill rejectionReason.

## 14. Customs proof

Mục đích: kiểm tra Driver gửi MRN/customs proof và TS xác minh kết quả.


~~~text
POST /api/v1/compliance-dossiers/<dossierId>/customs-clearance-proofs

customsPortName      = Calais Customs Port
locationId            = <locationId> or blank
mrnNumber            = MRN-2026-001
customsLane           = GreenLane
isHeld                = false
quickNote             = Customs check completed
files                 = choose one or more PDF/JPG/PNG
~~~

~~~text
GET /api/v1/customs-clearance-proofs/<proofId>
PATCH /api/v1/customs-clearance-proofs/<proofId>/finalize

action              = VERIFY
mrnNumber           = MRN-2026-001
customsLane         = GreenLane
ataCarnetExpiryDate = 2027-01-01
status              = CLEARED
verificationNote    = MRN and channel verified
rejectionReason     = blank for VERIFY
~~~

## 15. Execution order

~~~text
1. Login Customer
2. Create horse
3. Create booking
4. Submit booking as PENDING
5. Login LM
6. Approve booking
7. Create Transport Plan
8. List/detail/update Transport Plan
9. Login TS or RC; test approved booking and plan visibility
10. Create and apply dossier template
11. Customer uploads document
12. TS reviews document
13. Driver/Escort submits quarantine proof
14. Driver submits customs proof
15. TS finalizes proofs
16. Check notifications using each recipient account
~~~

Assignment requires Transport Plan APPROVED, while TP1 creates DRAFT. Source currently has no Transport Plan approve endpoint. If assignment returns INVALID_TRANSPORT_PLAN_STATUS, use an approved plan fixture/DB record.

## 16. Negative tests

- No token: 401 UNAUTHORIZED.
- Wrong role: 403 FORBIDDEN.
- Unknown UUID: 404.
- Stale lastUpdatedAt: 409 TRANSPORT_PLAN_CONCURRENCY_CONFLICT.
- Duplicate Transport Plan: 409 TRANSPORT_PLAN_EXISTS.
- End time before start: 400 VALIDATION_ERROR.
- Empty list: 200 with items: [].
