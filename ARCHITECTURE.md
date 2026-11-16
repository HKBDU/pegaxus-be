# Kiến trúc Backend Pegaxus

File này mô tả backend được xây thế nào và các phần làm việc với nhau ra sao.
Muốn biết cách đi lại trong code thì đọc [SRC_GUIDE.md](SRC_GUIDE.md).
Muốn biết quy tắc code mới phải theo thì đọc [CODING_CONVENTIONS.md](CODING_CONVENTIONS.md).

## 1. Hệ thống làm gì

Pegaxus quản lý việc vận chuyển ngựa đua trong nước và qua biên giới. Khách đặt một chuyến cho một hoặc nhiều con ngựa. Sau đó nhân viên:

1. lập kế hoạch chuyến,
2. chuẩn bị giấy tờ pháp lý và thú y,
3. gán xe và nhân sự,
4. theo dõi chuyến theo thời gian thực,
5. ghi nhận mỗi lần bàn giao ngựa,
6. xử lý sự cố trên đường,
7. lập hóa đơn cho khách.

### Vai trò

| Vai trò | Mã | Việc chính |
|---|---|---|
| Khách hàng | `AccountType.CUSTOMER` | Đăng ký ngựa, tạo booking, tải giấy tờ, trả hóa đơn, gửi khiếu nại |
| Logistics Manager (LM) | `EmployeeRole.LOGISTICS_MANAGER` | Duyệt booking và kế hoạch, duyệt thay đổi khẩn cấp và chi phí, quản lý bảng giá, phát hành hóa đơn, xem báo cáo |
| Transport Specialist (TS) | `EmployeeRole.TRANSPORT_SPECIALIST` | Hồ sơ compliance, duyệt giấy tờ, duyệt pháp lý route plan, nộp hồ sơ cho cơ quan chức năng |
| Fleet Route Coordinator (RC) | `EmployeeRole.FLEET_ROUTE_COORDINATOR` | Route plan, chặng, xe, nhân sự, thuê ngoài, yêu cầu tuyến dự phòng |
| Tài xế | `EmployeeRole.DRIVER` | Lái một chặng, gửi vị trí, ký bàn giao, báo sự cố |
| Escort | `EmployeeRole.ESCORT` | Chăm ngựa trên một chặng, ghi sức khỏe ngựa, ký bàn giao |

Role tới API dưới dạng role claim trong JWT. `Pegaxus.API/Extensions/JwtExtensions.cs` định nghĩa các policy phân quyền, ví dụ `LogisticsManagerPolicy`, `HandoverOperatorPolicy`, `DriverOrEscortPolicy`.

## 2. Công nghệ

| Việc | Công nghệ |
|---|---|
| Runtime | .NET 8, ASP.NET Core Web API |
| Database | PostgreSQL 16 qua EF Core và Npgsql. Enum được map thành kiểu enum gốc của PostgreSQL |
| Cache, rate limit | Redis (`IDistributedCache`), rate limiter của ASP.NET |
| Realtime | SignalR hub |
| Validate | FluentValidation |
| Xác thực | JWT access token (15 phút) và refresh token, đăng nhập Google, OTP qua email |
| File | Cloudinary (ảnh, tài liệu) |
| Mail | Brevo, gửi qua transactional outbox |
| Thanh toán | SePay checkout và webhook |
| Trợ lý | Gemini API cho chatbot của khách |
| Test | xUnit, EF InMemory, tầng PostgreSQL thật (tùy chọn) |

## 3. Cấu trúc solution

```text
pegaxus-be/
├── Pegaxus.API/            Tầng HTTP và nơi đăng ký DI
├── Pegaxus.Service/        Logic nghiệp vụ, mỗi module một thư mục
├── Pegaxus.Repository/     Model EF Core, configuration, migration, hàm hỗ trợ DB
├── Pegaxus.Service.Test/   Test xUnit, mỗi module một thư mục
├── Pegaxus.Dbdiagram/      Sơ đồ DBML của schema (chỉ để tham khảo)
└── docs/                   Tài liệu cho agent và ghi chú kiểm tra
```

Phụ thuộc chỉ đi một chiều:

```text
Pegaxus.API  ──►  Pegaxus.Service  ──►  Pegaxus.Repository  ──►  PostgreSQL
```

`Pegaxus.Service` không bao giờ tham chiếu `Pegaxus.API`. Việc gì chỉ tầng API làm được, ví dụ đẩy tin qua SignalR, thì nằm sau một interface ở tầng Service. Tầng API cài đặt interface đó. Ví dụ:

- `INotificationRealtimeService` do `SignalRNotificationRealtimeService` cài đặt.
- `IRoutePlanRealtimeService` do `SignalRRoutePlanRealtimeService` cài đặt.
- `ILocationTrackingRealtimeService` do `SignalRLocationTrackingService` cài đặt.

## 4. Các tầng

### 4.1. Pegaxus.API

| Thư mục | Nội dung |
|---|---|
| `Program.cs` | Khởi động. Nạp `.env`, đăng ký module, chạy migration và seed, dựng pipeline middleware, map controller và hub |
| `Extensions/ServiceCollectionExtensions.cs` | Đăng ký DI, mỗi nhóm tính năng một hàm `Add<Nhóm>Module` |
| `Extensions/JwtExtensions.cs` | Cấu hình JWT và mọi policy phân quyền |
| `Extensions/RateLimitingExtensions.cs` | Rate limit (hiện dùng cho bảng giá) |
| `Controllers/` | Mỗi resource một controller. Controller lớn chia thành file partial `<Tên>Controller.<Nhóm>.cs` |
| `Middleware/GlobalExceptionHandlerMiddleware.cs` | Biến mọi exception thành JSON lỗi chuẩn |
| `Models/ApiResponse.cs` | `ResponseBuilder` và khuôn JSON trả về |
| `Hubs/` | SignalR hub và phần cài đặt realtime |
| `Services/` | Background worker (`BackgroundService`) |
| `AppDbContextSeedData.cs` | Dữ liệu seed chạy lúc khởi động (xem `SEED_DATA_REFERENCE.md`) |

#### Pipeline của request

```text
ForwardedHeaders → Swagger → GlobalExceptionHandler → CORS ("Frontend")
→ Authentication → Authorization → RateLimiter → action của controller
```

`Program.cs` cũng map các endpoint sau:

- `GET /health`
- hub `/hubs/notifications`
- hub `/hubs/compliance-dossiers`
- hub `/hubs/transport-tracking`

#### Controller

Mọi controller kế thừa `ApiControllerBase`. Lớp cha cung cấp:

- `AccountId`, id tài khoản của người gọi lấy từ JWT;
- `Success(value, message)` cho 200 và `SuccessCreated(value, message)` cho 201;
- `ValidateRequestAsync(request, validator, ct)`, trả 400 kèm danh sách lỗi của từng trường.

Một action của controller chỉ map request, gọi một hàm service và bọc kết quả. Không có code EF và không có luật nghiệp vụ.

#### Khuôn JSON trả về

Mọi response, thành công hay lỗi, đều cùng một khuôn do `ResponseBuilder` tạo:

```json
{
  "isSuccess": false,
  "code": 409,
  "errorCode": "INVALID_TRANSPORT_STATUS",
  "message": "A final invoice can be created only after the horses are delivered to the customer. Transport TR-... is still on the way.",
  "value": { "transportStatus": "IN_TRANSIT" },
  "traceId": "...",
  "timestamp": "2026-11-16T08:00:00+00:00"
}
```

- `errorCode` và `errors` bị bỏ khỏi JSON khi bằng null. Response thành công không có `errorCode`.
- `message` đi qua `Models/UserFacingMessages.cs` trước khi gửi.
- `errorCode` không đổi. Client dựa vào nó để xử lý. Không bao giờ đổi tên một mã đã có.
- `message` là câu tiếng Anh dễ hiểu cho người dùng.
- `value` trong response lỗi chứa dữ liệu thêm. Chỉ `ConflictException` và `UnprocessableEntityException` đặt giá trị này.
- `errors` liệt kê các mục `{ field, message }` khi validate thất bại.

#### Map exception sang mã HTTP

Code tầng Service ném exception trong `Pegaxus.Service/Exceptions/Exceptions.cs`. Middleware map chúng sang mã HTTP:

| Exception | HTTP |
|---|---|
| `BadRequestException`, `FieldValidationException` | 400 |
| `UnauthorizedException` | 401 |
| `ForbiddenException` | 403 |
| `NotFoundException` | 404 |
| `ConflictException` | 409 |
| `UnprocessableEntityException` | 422 |
| `TooManyRequestsException` | 429 |
| Lỗi serializable hoặc deadlock của PostgreSQL, `DbUpdateConcurrencyException` | 409 |
| Tràn số / chuỗi quá dài trong một cột | 422 / 400 |
| Lỗi khác | 500 với câu báo lỗi chung an toàn |

Chỉ câu chữ do code của mình viết mới tới được người dùng. Câu lỗi của framework và database không bao giờ gửi cho client.

#### Background worker (`Pegaxus.API/Services/`)

| Worker | Chu kỳ | Việc |
|---|---|---|
| `TransactionalOutboxWorker` | 1 giây | Gửi email đang chờ trong `OutboxMessages` |
| `MissingDocumentReminderWorker` | 1 phút | Nhắc khách về giấy tờ compliance còn thiếu |
| `IncidentHoldExpiryWorker` | 1 phút | Kết thúc các lần tạm dừng tuyến do sự cố đã hết hạn |
| `InvoiceOverdueWorker` | 1 giờ | Chuyển hóa đơn chưa trả đã qua hạn sang `OVERDUE` |
| `AuditExportWorker` | hàng đợi | Lần lượt tạo từng gói audit từ `AuditExportQueue` |

#### SignalR hub

| Hub | Đường dẫn | Dùng để |
|---|---|---|
| `NotificationHub` | `/hubs/notifications` | Đẩy thông báo mới trong app tới một tài khoản |
| `ComplianceDossierHub` | `/hubs/compliance-dossiers` | Cập nhật trực tiếp khi nhiều người cùng làm một hồ sơ |
| `TransportTrackingHub` | `/hubs/transport-tracking` | Vị trí xe và tiến độ chuyến theo thời gian thực |

`AccountUserIdProvider` gắn một kết nối với id tài khoản, nhờ đó server gửi được tin tới đúng một người.

### 4.2. Pegaxus.Service

Mỗi module nghiệp vụ là một thư mục và một namespace, `Pegaxus.Service.<Tên>Service`. Một module có khuôn sau:

```text
<Tên>Service/
  I<Tên>Service.cs     interface public (lối vào duy nhất cho code khác)
  Service.cs           sealed partial class Service: constructor và hàm hỗ trợ nhỏ dùng chung
  <UseCase>.cs         các phần khác của cùng partial class, mỗi file một nhóm use case
  Request.cs           static class Request  { DTO đầu vào }
  Response.cs          static class Response { DTO đầu ra }
  Validator/           validator FluentValidation (module cũ dùng tên Validations/)
```

Service inject `AppDbContext` và query trực tiếp. Không có tầng repository, không có unit of work, không có MediatR. Một module chỉ gọi module khác qua `I<Tên>Service` của nó. Logic thuần dùng chung nằm ở `Pegaxus.Service/Common/`.

#### Module theo mảng nghiệp vụ

| Mảng | Module |
|---|---|
| Danh tính | `Auth`, `JwtService`, `Account`, `Employee` |
| Dữ liệu của khách | `Horse`, `BookingService` |
| Lập kế hoạch | `TransportPlanService`, `RoutePlanService`, `RouteService`, `LocationService`, `PersonnelAssignmentService`, `RouteOutsourcingService`, `VehicleService`, `TransportProviderService` |
| Compliance | `ComplianceDossierService`, `DossierTemplateService`, `ComplianceRequirementService`, `DocumentStorageService` |
| Vận hành chuyến | `TransportService`, `LocationTrackingService`, `HandoverService`, `HorseHealthLogService`, `HorseHealthService` |
| Sự cố và đổi tuyến | `IncidentService`, `RouteChangeService` |
| Tiền | `PriceListService`, `InvoiceService`, `PaymentService`, `PaymentGatewayService`, `FinancialReportService` |
| Sau chuyến | `ClaimService`, `ReportService`, `AuditService` |
| Tin nhắn và file | `NotificationService`, `MailService`, `OutboxService`, `CloudinaryService`, `MediaService` |
| Trợ lý | `Assistant` (client Gemini và các tool chỉ đọc cho khách) |

#### Hàm hỗ trợ dùng chung (`Pegaxus.Service/Common/`)

| Hàm hỗ trợ | Dùng để |
|---|---|
| `DbTime` | Giờ UTC hiện tại, cắt bớt độ chính xác để so phiên bản, `AsUtc` |
| `Validation` | Chạy validator và ném lỗi 400 |
| `Paging`, `Text`, `EnumParser`, `Decimals`, `Vnd` | Hàm nhỏ xử lý giá trị |
| `CodeGeneration`, `DailyCodes` | Mã dễ đọc như mã booking, mã chuyến |
| `HandoverRules`, `RoutePersonnel`, `RouteRestRules`, `VehicleSchedule`, `TransportReadiness`, `DepartureLeadTime` | Luật nghiệp vụ nhiều module cùng dùng |
| `Geo`, `Coordinates`, `CountryCodes`, `CountryUtils`, `CompetentAuthorities` | Địa lý và cơ quan chức năng của từng nước |
| `NotificationLinks`, `IncidentLinks`, `UserFacingText`, `RoleLabel` | Câu chữ và link người dùng nhìn thấy |

### 4.3. Pegaxus.Repository

| Thư mục | Nội dung |
|---|---|
| `AppDbContext*.cs` | Các `DbSet`, cấu hình cột enum, phần model của đổi tuyến |
| `Entities/` | 58 class entity |
| `Configurations/` | Mỗi entity một `IEntityTypeConfiguration<T>`. `ApplyConfigurationsFromAssembly` nạp tất cả |
| `Enum/` | Enum nghiệp vụ, cũng được map thành kiểu enum của PostgreSQL |
| `Migrations/` | Migration EF Core. API tự chạy chúng lúc khởi động |
| `Extensions/` | `TransactionExtensions` (transaction serializable), `RowLockExtensions` (`LockRowsAsync`), `PostgresErrors`, `EmployeeQueryExtensions` (`Active()`) |

Quy ước tên: bảng dùng PascalCase, cột dùng snake_case, timestamp là `timestamp without time zone` và chứa giờ UTC.

## 5. Mô hình dữ liệu

### 5.1. Chuỗi chính

```text
Customer ─┬─ Horse
          └─ Booking ── BookingHorse ── Horse
               │
               ├── Invoice ── InvoiceItem, InvoiceStatusHistory, Receipt
               │
               └── Transport (mỗi booking một transport)
                     ├── TransportPlan ── EmployeeTransportPlan (nhân viên trong plan)
                     │     │            └─ TransportPlanProvider (nhà vận chuyển ngoài)
                     │     └── RoutePlan (tuyến chính, cùng các tuyến dự phòng trỏ về nó qua PrimaryRoutePlanId)
                     │           └── RoutePlanRoute  = một "chặng"
                     │                 ├─ Route (FromLocation → ToLocation)
                     │                 ├─ DriverEmployee, EscortEmployee  hoặc  TransportProvider
                     │                 └─ RoutePlanRouteVehicle ── Vehicle
                     ├── ComplianceDossier ── DossierRequirement ── ComplianceDocument ── DocumentVersion
                     │                     └─ AuthoritySubmission
                     ├── HandoverRecord (từ chặng/bên giao → tới chặng/bên nhận)
                     ├── HorseHealthLog
                     ├── IncidentReport ── EmergencyCostRequest, IncidentPhoto, các bảng chi tiết
                     ├── FinancialReport
                     └── Notification

PriceList ── PriceListItem   (bảng giá; chỉ một bảng active; InvoiceItem lưu price_list_item_id)
```

Chuyến về là một `Booking` mới có `ReturnOfTransportId` trỏ về transport của chuyến đi.

`CatalogAuditLog` là bảng audit chung. Một số module dùng nó để lưu bản ghi nhỏ không cần bảng riêng, ví dụ yêu cầu tuyến dự phòng mới nhất và lịch sử thay đổi bảng giá.

### 5.2. Các trạng thái

| Entity | Trạng thái |
|---|---|
| `Booking` | `DRAFT → PENDING → APPROVED`, hoặc `REJECTED` / `CANCELLED` |
| `TransportPlan` | `DRAFT → PENDING → APPROVED → IN_PROGRESS → COMPLETED`, hoặc `CANCELLED` |
| `RoutePlan` | `DRAFT → PENDING_LEGAL_REVIEW → PENDING_MANAGER_APPROVAL → APPROVED / CONFIRMED → ACTIVE → COMPLETED`, hoặc `REJECTED_BY_MANAGER` / `CANCELLED` |
| `Transport` | `CREATED → PLANNING → READY → IN_TRANSIT → COMPLETED`, hoặc `CANCELLED` |
| `HandoverRecord` | `PLANNED → IN_PROGRESS → COMPLETED`, hoặc `CANCELLED`. Loại: `PICKUP`, `TRANSFER`, `BORDER_HANDOVER`, `CARRIER_HANDOVER`, `DELIVERY`, `OTHER` |
| `Invoice` | `DRAFT → ISSUED → PARTIALLY_PAID → PAID`, cùng `OVERDUE` và `CANCELLED`. Loại: `QUOTATION`, `DEPOSIT`, `FINAL`, `SUPPLEMENTARY`, `CREDIT_NOTE` |
| `IncidentReport` | `REPORTED → PENDING → RESOLVED`, hoặc `CANCELLED`. Mức độ `LOW < MEDIUM < HIGH < CRITICAL` chỉ được tăng |

Các enum nằm ở `Pegaxus.Repository/Enum/`. Phần kiểm tra cho phép hay từ chối một lần chuyển trạng thái nằm trong module sở hữu entity đó.

## 6. Luồng từ đầu đến cuối

```text
1. Khách            đăng ký ngựa, tạo booking                       BookingService
2. LM               xem, thương lượng, duyệt booking                 BookingService
3. LM               lập báo giá từ bảng giá đang dùng               InvoiceService (Quotation), PriceListService
4. LM               tạo transport plan                               TransportPlanService
5. LM / khách       hóa đơn cọc; khách trả cọc → plan APPROVED       InvoiceService, PaymentService
6. LM / RC          gán nhân sự phase 1; dựng route plan và chặng,   PersonnelAssignmentService, RoutePlanService,
                    chọn xe, nhân sự hoặc nhà vận chuyển ngoài       VehicleService, RouteOutsourcingService
7. TS               chuẩn bị hồ sơ compliance, duyệt pháp lý         ComplianceDossierService, RoutePlanService (LegalReview)
                    (một lần duyệt áp cho cả tuyến chính và dự phòng)
8. LM               duyệt route plan                                  RoutePlanService
9. Nhân sự          ký bàn giao PICKUP để bắt đầu chuyến             HandoverService
10. Nhân sự         gửi vị trí, ghi sức khỏe ngựa,                   LocationTrackingService, HorseHealthLogService,
                    bàn giao giữa các chặng                          HandoverService
11. Nhân sự/RC/LM   sự cố, tạm dừng tuyến, kích hoạt tuyến dự phòng, IncidentService, RouteChangeService
                    chi phí khẩn cấp
12. Nhân sự         ký bàn giao DELIVERY; transport thành COMPLETED  HandoverService (CompletionEffects)
13. LM              hóa đơn quyết toán, thanh toán, báo cáo tài chính InvoiceService, PaymentService, FinancialReportService
14. Khách / LM      khiếu nại, báo cáo, gói audit                    ClaimService, ReportService, AuditService
```

Thứ tự các bước 4–8 trong thực tế có thể xen nhau. Ví dụ khách có thể trả cọc trước khi có plan, khi đó plan tạo sau được duyệt ngay.

Một số luật đi qua nhiều module:

- Transport chỉ thành `COMPLETED` khi một handover `DELIVERY` hoàn tất. Hóa đơn `FINAL` cần transport đã hoàn tất.
- Gán nhân sự phase 1 cần transport plan đã `APPROVED`, tức là cọc đã được trả.
- Không ký được handover khi tài xế hoặc escort nhận bàn giao vẫn đang ở một chuyến khác đã chạy mà chưa tới nơi.
- Yêu cầu tuyến dự phòng luôn được gửi cho LM, kèm một đoạn tóm tắt dễ hiểu về mức độ thay đổi.
- Dòng phí của báo giá, cọc và quyết toán do hệ thống tính từ bảng giá đang dùng. LM không nhập tay.

## 7. Các cơ chế dùng chung

### 7.1. Đường ghi dữ liệu và tranh chấp

Mọi thao tác ghi có thể đụng nhau đều đi theo cùng một thứ tự:

```text
validate
→ BeginSerializableAsync
→ LockRowsAsync (thứ tự khóa cố định)
→ ReloadAsync các entity đang được track
→ kiểm phiên bản (UpdatedAt so với LastUpdatedAt của request) và trạng thái
→ sửa dữ liệu
→ SaveChangesAsync → CommitAsync
→ notifications.FlushAsync
```

Với route plan, thứ tự khóa là: Transport, rồi TransportPlan, rồi RoutePlan, rồi các chặng theo id, rồi hồ sơ. Phiên bản cũ trả về lỗi 409 có mã riêng của module, ví dụ `INVOICE_MODIFIED`. Lỗi serializable của PostgreSQL cũng thành 409.

### 7.2. Thông báo và mail

- Thông báo trong app được đưa vào hàng đợi của `INotificationService` trong lúc transaction đang chạy. Sau khi commit, `FlushAsync` lưu chúng và đẩy qua SignalR.
- Email được ghi thành dòng `OutboxMessage` trong cùng transaction, bằng `OutboxQueue.AddEmail`. `TransactionalOutboxWorker` gửi sau. Vì vậy transaction bị rollback thì không bao giờ gửi mail.
- HTML của email chỉ lấy từ template trong `MailService/EmailBodies.*.cs`.

### 7.3. Thời gian

`TimeProvider` được inject, nên test điều khiển được đồng hồ. Timestamp lưu ở dạng UTC, không có múi giờ. `DbTime` lo việc chuyển đổi và cắt bớt độ chính xác.

### 7.4. File

Client tải file qua `UploadsController` và Cloudinary. Entity chỉ lưu URL hoặc public id. Giới hạn body của API là 1 MB, vì file không đi qua JSON API.

## 8. Cấu hình và chạy

- Thông tin bí mật lấy từ file `.env`. `Program.cs` tìm file này ở thư mục hiện tại và từng thư mục cha. **Không bao giờ commit hay in `.env`.**
- `appsettings.json` chứa giá trị mặc định không bí mật.
- `docker-compose.yml` chạy PostgreSQL và Redis cho máy local. `Dockerfile` build image của API.
- Lúc khởi động, API chạy mọi migration và dữ liệu seed.

```bash
docker compose up -d
dotnet run --project Pegaxus.API
dotnet test Pegaxus.sln
```

## 9. Test

`Pegaxus.Service.Test/<Module>/` có cấu trúc giống các module service.

| Tầng | Cách làm |
|---|---|
| Luật của service | `TestSupport.CreateDb()` (EF InMemory) và các hàm seed |
| Hợp đồng HTTP | `*HttpTests` với test host và controller thật |
| PostgreSQL | `[PostgresFact]`. Chỉ chạy khi có biến `PEGAXUS_TEST_CONNECTION_STRING` |

Các hàm hỗ trợ trong `Repository/Extensions` không làm gì, hoặc dùng cách thay thế, khi chạy trên InMemory. Nhờ vậy cùng một code service chạy được trong unit test.
