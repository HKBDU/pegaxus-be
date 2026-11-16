# Hướng dẫn đọc source BE và tìm một tính năng

File này chỉ cách đi lại trong code. Nội dung gồm:

- nên bắt đầu đọc từ đâu,
- cách đi theo một request từ HTTP xuống tới database,
- cách tìm code của một tính năng bất kỳ,
- muốn sửa thì sửa ở đâu.

Muốn xem bức tranh tổng thể thì đọc [ARCHITECTURE.md](ARCHITECTURE.md). Muốn biết quy tắc code mới phải theo thì đọc [CODING_CONVENTIONS.md](CODING_CONVENTIONS.md).

## 1. Thứ tự đọc cho người mới

Đọc các file theo thứ tự dưới đây. Bước sau cần hiểu bước trước.

| # | File | Học được gì |
|---|---|---|
| 1 | `Pegaxus.API/Program.cs` | Khởi động app, thứ tự middleware, hub, worker |
| 2 | `Pegaxus.API/Extensions/ServiceCollectionExtensions.cs` | Mọi module, interface nào nối với class nào |
| 3 | `Pegaxus.API/Extensions/JwtExtensions.cs` | Mọi policy và role được phép của từng policy |
| 4 | `Pegaxus.API/Controllers/ApiControllerBase.cs`, `Models/ApiResponse.cs` | Khuôn JSON trả về và các hàm hỗ trợ |
| 5 | `Pegaxus.API/Middleware/GlobalExceptionHandlerMiddleware.cs`, `Pegaxus.Service/Exceptions/Exceptions.cs` | Exception biến thành mã HTTP thế nào |
| 6 | `Pegaxus.Repository/Enum/*.cs` | Mọi trạng thái và loại |
| 7 | `Pegaxus.Repository/Entities/Booking.cs` → `Transport.cs` → `TransportPlan.cs` → `RoutePlan.cs` → `RoutePlanRoute.cs` | Chuỗi dữ liệu chính |
| 8 | `Pegaxus.Repository/Extensions/*.cs` | Transaction, khóa dòng, phát hiện xung đột |
| 9 | Một module nhỏ từ đầu đến cuối, ví dụ `VehicleService/` với `VehiclesController.cs` | Khuôn module chuẩn |
| 10 | Một module lớn, ví dụ `HandoverService/` hoặc `InvoiceService/` | Module lớn được chia thành nhiều file partial thế nào |

## 2. Đi theo một request qua các tầng

Ví dụ: `POST /api/v1/handover-records/{handoverRecordId}/complete` (ký hoàn tất handover).

```text
1. Controller    Pegaxus.API/Controllers/HandoverRecordsController.cs
                 [Authorize(Policy = HandoverOperatorPolicy)]  → gọi IHandoverService.CompleteAsync
2. Interface     Pegaxus.Service/HandoverService/IHandoverService.cs
3. Use case      Pegaxus.Service/HandoverService/Completion.cs
                 validate → transaction serializable → khóa → reload → kiểm tra → sửa dữ liệu → save → commit → flush
4. Hiệu ứng phụ  Pegaxus.Service/HandoverService/CompletionEffects.cs
                 kiểm tài xế/escort còn bận chuyến khác, bắt đầu/kết thúc chặng, transport COMPLETED khi DELIVERY
5. Entity        Pegaxus.Repository/Entities/HandoverRecord.cs, RoutePlanRoute.cs, Transport.cs
6. Mapping DB    Pegaxus.Repository/Configurations/HandoverRecordConfiguration.cs
7. Thông báo     HandoverService/Notifications.cs, MailService/EmailBodies.Handover.cs
8. Test          Pegaxus.Service.Test/Handover/
```

Tính năng nào cũng có cùng khuôn này. Hiểu một lần là đọc được mọi module.

## 3. Cái gì nằm ở đâu

| Muốn tìm | Xem ở |
|---|---|
| URL của một endpoint | `Pegaxus.API/Controllers/` (xem bảng ở mục 5) |
| Ai được gọi endpoint | `[Authorize(Policy = ...)]` trên action, rồi xem `JwtExtensions.cs` |
| Luật nghiệp vụ sau endpoint | `Pegaxus.Service/<Module>/<UseCase>.cs` |
| Trường đầu vào và giới hạn | `<Module>/Request.cs` và `<Module>/Validator/` |
| Khuôn JSON trả về | `<Module>/Response.cs` (và `Mapping.cs` nếu module có) |
| Câu báo lỗi hoặc mã lỗi | `grep` mã lỗi, ví dụ `"INVALID_TRANSPORT_STATUS"` |
| Trạng thái và giá trị hợp lệ | `Pegaxus.Repository/Enum/` |
| Bảng hoặc cột | `Pegaxus.Repository/Entities/` và `Configurations/` |
| Lịch sử schema | `Pegaxus.Repository/Migrations/` |
| Nội dung email | `Pegaxus.Service/MailService/EmailBodies.<Nhóm>.cs` |
| Nội dung và link thông báo trong app | `<Module>/Notifications.cs`, `NotificationService/UserFacingNotificationText.cs`, `Common/NotificationLinks.cs` |
| Luật dùng chung nhiều module | `Pegaxus.Service/Common/` |
| Việc chạy định kỳ hoặc theo hàng đợi | `Pegaxus.API/Services/*Worker.cs` |
| Đẩy realtime | `Pegaxus.API/Hubs/` |
| Dữ liệu seed (tài khoản demo, quốc gia, địa điểm) | `Pegaxus.API/AppDbContextSeedData.cs`, `SEED_DATA_REFERENCE.md` |
| Ví dụ một luật phải chạy thế nào | `Pegaxus.Service.Test/<Module>/` |

## 4. Cách tìm một tính năng

Chọn cách đầu tiên khớp với thứ bạn đang có trong tay.

### 4.1. Bạn có URL (từ FE, Swagger hoặc log)

1. Tìm controller theo tiền tố route (mục 5).
2. Tìm phần còn lại của đường dẫn:

   ```bash
   rg -n '/complete"' Pegaxus.API/Controllers/HandoverRecordsController.cs
   ```

   Một số route bắt đầu bằng `/`. Những route đó bỏ qua tiền tố của controller:

   ```bash
   rg -n 'Http(Get|Post|Put|Patch|Delete)\("/api/v1/route-plans' Pegaxus.API/Controllers
   ```

3. Mở hàm service mà action gọi, rồi nhảy tới phần cài đặt trong `Pegaxus.Service/<Module>/`.

### 4.2. Bạn có câu báo lỗi hoặc mã lỗi

Mã lỗi là chuỗi viết hoa có gạch dưới và không đổi. Tìm nguyên mã:

```bash
rg -n '"PERSONNEL_STILL_ON_ANOTHER_TRIP"' Pegaxus.Service
```

Nếu chỉ có câu báo lỗi, tìm một đoạn ngắn cố định trong câu. Câu báo lỗi hay chứa giá trị thay đổi, ví dụ mã chuyến, nên đừng tìm nguyên câu:

```bash
rg -n -F "can be created only after" Pegaxus.Service
```

### 4.3. Bạn biết từ nghiệp vụ

Tìm từ đó trong tên entity, enum và service trước. Sau đó tìm trong toàn bộ tầng Service:

```bash
rg -n -i "backup" Pegaxus.Repository/Enum Pegaxus.Repository/Entities
rg -l -i "backup" Pegaxus.Service
```

Từ nghiệp vụ hay gặp và tên trong code:

| Từ nghiệp vụ | Tên trong code |
|---|---|
| Chuyến đi | `Transport` |
| Kế hoạch chuyến | `TransportPlan` |
| Phương án tuyến (chính hoặc dự phòng) | `RoutePlan` (bản dự phòng có `PrimaryRoutePlanId`) |
| Chặng | `RoutePlanRoute` |
| Đoạn đường giữa hai điểm | `Route` (`FromLocation` → `ToLocation`) |
| Gán nhân sự | `PersonnelAssignmentService`, `RoutePlanRoute.DriverEmployeeId` / `EscortEmployeeId` |
| Nhà vận chuyển thuê ngoài | `TransportProvider`, `RouteOutsourcingService` |
| Bàn giao / ePOD | `HandoverRecord`, `HandoverService` (`Epod.cs`) |
| Hồ sơ giấy tờ qua biên giới | `ComplianceDossier`, `DossierRequirement`, `ComplianceDocument` |
| Duyệt pháp lý | `RoutePlanService/LegalReview.cs` |
| Kích hoạt tuyến dự phòng / đổi tuyến | `RouteChangeService` |
| Chi phí khẩn cấp | `EmergencyCostRequest`, `IncidentService/CostCommands.cs` |
| Tạm dừng tuyến | `IncidentService/Holds.cs` |
| Báo giá | `InvoiceService/Quotation.cs` |
| Bảng giá | `PriceList`, `PriceListService` |
| Hóa đơn cọc / quyết toán | `InvoiceType.DEPOSIT` / `FINAL` |

### 4.4. Bạn biết một lần đổi trạng thái

Tìm chỗ gán giá trị enum đó. Cách này ra mọi nơi đưa entity vào trạng thái đó:

```bash
rg -n "Status = TransportStatus.COMPLETED" Pegaxus.Service
rg -n "TransportStatus.COMPLETED" Pegaxus.Service
```

### 4.5. Bạn biết màn hình hoặc role

1. Tìm policy mà role đó dùng trong `JwtExtensions.cs`.
2. Tìm mọi endpoint dùng policy đó:

   ```bash
   rg -n "FleetRouteCoordinatorPolicy" Pegaxus.API/Controllers
   ```

### 4.6. Bạn muốn biết một luật phải chạy thế nào

Tên test mô tả luật. Tìm trong test trước khi đọc phần cài đặt:

```bash
rg -n "public (async Task|void) \w*Handover\w*" Pegaxus.Service.Test
```

### 4.7. Bạn cần biết ai gọi một hàm

```bash
rg -n "EnsureReceivingCrewFreeAsync\(" Pegaxus.Service
```

Module khác chỉ gọi một module qua interface của nó. Muốn tìm mọi chỗ gọi từ bên ngoài thì tìm tên interface:

```bash
rg -n "IInvoiceService" Pegaxus.Service Pegaxus.API
```

## 5. Tiền tố route → controller → service

| Tiền tố route `api/v1/...` | Controller | Service chính |
|---|---|---|
| `auth` | `AuthController` | `Auth` |
| `accounts` | `AccountsController` | `Account` |
| `employees` | `EmployeesController` | `Employee` |
| `horses` | `HorsesController` | `Horse` |
| `bookings` | `BookingController` (+ `.Assignments`, `.Horses`, `.Invoices`, `.Lifecycle`, `.Queries`) | `BookingService` |
| `bookings/{bookingId}/invoices` | `BookingInvoicesController` | `InvoiceService` |
| `bookings/{bookingId}/transport-plans` | `TransportPlanController` | `TransportPlanService` |
| `route-plans/...` (route tuyệt đối) | `RoutePlansController` | `RoutePlanService` |
| `route-plans/{id}/segments/{segmentId}/outsourcing` | `RouteOutsourcingController` | `RouteOutsourcingService` |
| `route-change-requests` (+ `/route-plans/{id}/change-request`) | `RouteChangeRequestsController` | `RouteChangeService` |
| `routes` | `RoutesController` | `RouteService` |
| `locations` | `LocationsController` | `LocationService` |
| `vehicles` | `VehiclesController` | `VehicleService` |
| `transport-providers` | `TransportProvidersController` | `TransportProviderService` |
| `...` (route gán nhân sự) | `PersonnelAssignmentsController` | `PersonnelAssignmentService` |
| `transports` | `TransportsController` | `TransportService` |
| `transports/{transportId}` (vị trí) | `TransportLocationsController` | `LocationTrackingService` |
| `transports/{transportId}/horse-health-logs` | `TransportHorseHealthLogsController` | `HorseHealthLogService` |
| `transports/{transportId}/financial-report` | `FinancialReportsController` | `FinancialReportService` |
| `horse-health-logs` | `HorseHealthLogsController` | `HorseHealthLogService`, `HorseHealthService` |
| `handover-records` | `HandoverRecordsController` | `HandoverService` |
| `incidents` | `IncidentsController` | `IncidentService` |
| `emergency-cost-requests` | `EmergencyCostRequestsController` | `IncidentService` |
| `compliance-dossiers` | `ComplianceDossiersController` (+ `.AuthoritySubmissions`, `.CustomsProofs`, `.Dashboard`, `.Documents`, `.QuarantineProofs`, `.Requirements`, `.Reviews`) | `ComplianceDossierService` |
| `compliance-requirements` | `ComplianceRequirementsController` | `ComplianceRequirementService` |
| `dossier-templates` | `DossierTemplatesController` | `DossierTemplateService` |
| `invoices` | `InvoicesController` | `InvoiceService` |
| `payments` | `PaymentsController` | `PaymentService`, `PaymentGatewayService` |
| `price-lists` | `PriceListsController` | `PriceListService` |
| `notifications` | `NotificationsController` | `NotificationService` |
| `uploads` | `UploadsController` | `CloudinaryService` |
| `reports` | `ReportsController` | `ReportService` |
| `...` (route claim) | `ClaimsController` | `ClaimService` |
| `...` (route lưu trữ và audit) | `AuditController` | `AuditService` |
| `assistant` | `AssistantController` | `Assistant` |

Các controller ghi `...` dùng tiền tố `api/v1`, mỗi action tự thêm đường dẫn đầy đủ. Tìm `Http` trong controller để liệt kê.

Khi chạy API, Swagger liệt kê mọi endpoint kèm XML docs. Mở `/swagger`.

## 6. Module lớn được chia thế nào

Service lớn là một `partial class Service` chia ra nhiều file. Tên file cho biết file đó chứa use case nào. Ví dụ:

| Module | File và nội dung |
|---|---|
| `InvoiceService` | `Commands.cs` (tạo, sửa, phát hành, hủy), `Queries.cs` (danh sách, chi tiết, suggestion), `Payments.cs`, `Overdue.cs`, `DueDates.cs`, `Quotation.cs` + `QuotationCatalog.cs`, `PriceListSnapshot.cs` |
| `HandoverService` | `Creation.cs`, `Lifecycle.cs`, `Completion.cs`, `CompletionEffects.cs`, `Epod.cs`, `Mapping.cs`, `Notifications.cs` |
| `RouteChangeService` | `Service.cs` (tạo yêu cầu, đọc yêu cầu), `Comparison.cs` (so tuyến chính với dự phòng), `Execution.cs` (kích hoạt dự phòng), `ChosenCrew.cs`, `Anchors.cs`, `Locking.cs`, `Mapping.cs`, `Notifications.cs` |
| `IncidentService` | `Reporting.cs`, `Lifecycle.cs`, `Holds.cs`, `Rerouting.cs` + `RerouteCommands.cs`, `CostCommands.cs` + `CostQueries.cs`, `Horse*.cs`, `VehicleDetails.cs`, `CustomerResponse.cs` |
| `ComplianceDossierService` | `Initialization.cs`, `Requirements.cs`, `Document*.cs`, `AuthoritySubmissions.cs`, `CustomsProofs.cs`, `QuarantineProofs.cs`, `MissingDocuments.cs`, `ReturnTrip.cs`, `Dashboard.cs` |
| `BookingService` | `Lifecycle.cs`, `Negotiation.cs`, `Cancellation.cs`, `Assignments.cs`, `Horses.cs`, `ReturnTrip.cs`, `Queries.cs`, `ApprovedQueries.cs` |
| `RoutePlanService` | `Plans.cs`, `Vehicles.cs`, `Validation.cs`, `LegalReview.cs`, `ReturnTemplate.cs`, `Export.cs`, `WriteRetry.cs` |

Muốn đọc một use case, mở interface trước. Sau đó tìm tên hàm để ra file partial chứa nó:

```bash
rg -n "Task<.*> CreateAsync\(" Pegaxus.Service/InvoiceService
```

## 7. Công thức: muốn sửa thì đụng file nào

| Thay đổi | File cần đụng |
|---|---|
| Thêm trường vào response | `Response.cs`, phần `Select` trong file query, schema bên FE, một test |
| Thêm trường đầu vào | `Request.cs`, validator, use case, một test. Nếu lưu xuống DB: entity, configuration, migration |
| Thêm luật nghiệp vụ cho một action | File use case. Ném domain exception với `errorCode` mới, không đổi, và câu báo lỗi tiếng Anh dễ hiểu. Thêm test |
| Thêm endpoint | Làm theo checklist ở `CODING_CONVENTIONS.md` mục 9 |
| Thêm bảng hoặc cột | Entity, configuration, `DbSet` nếu là bảng mới, rồi chạy `dotnet ef migrations add <Tên> --project Pegaxus.Repository --startup-project Pegaxus.API` |
| Thêm email | Template trong `MailService/EmailBodies.<Nhóm>.cs` kèm test trong `Pegaxus.Service.Test/Mail/EmailBodiesTests.cs`, rồi đưa vào hàng đợi bằng `OutboxQueue.AddEmail` |
| Thêm thông báo | `Notifications.cs` của module. Đưa vào hàng đợi của `INotificationService` trong transaction, flush sau khi commit |
| Thêm việc chạy định kỳ | Worker trong `Pegaxus.API/Services/`, đăng ký bằng `AddHostedService` |
| Thêm kiểm tra role | Dùng lại policy có sẵn trong `JwtExtensions.cs`. Chỉ thêm policy mới khi tập role là mới |

## 8. Mẹo debug

- **Tìm request trong log.** Mọi response đều có `traceId`. Middleware ghi log exception kèm method và đường dẫn.
- **Vì sao bị 409.** Xem `errorCode`. Dạng `*_MODIFIED` nghĩa là client gửi `LastUpdatedAt` cũ. Một lỗi 409 không có mã riêng của module thường là xung đột serializable của PostgreSQL. Gửi lại request.
- **Vì sao bị 403.** Xem policy trên action. Sau đó xem các kiểm tra quyền sở hữu trong service, ví dụ "RC này có được gán vào plan này không".
- **Vì sao bị 404.** Service trả 404 cho cả bản ghi không tồn tại lẫn bản ghi người gọi không được xem.
- **Tái hiện một luật nhanh.** Chép test gần nhất trong `Pegaxus.Service.Test/<Module>/`. Test đó đã seed sẵn dữ liệu cần thiết trên InMemory.
- **Xem dữ liệu.** Chạy database local bằng `docker compose up -d`, rồi kết nối PostgreSQL. Không đọc `.env` để lấy thông tin đăng nhập, hãy hỏi đồng đội.

## 9. Các lệnh tìm hay dùng

```bash
# endpoint theo một phần đường dẫn
rg -n 'Http(Get|Post|Put|Patch|Delete)\(".*handover' Pegaxus.API/Controllers

# mã lỗi
rg -n '"INVOICE_MODIFIED"' Pegaxus.Service

# chỗ gán một trạng thái
rg -n "Status = RoutePlanStatus\.\w+" Pegaxus.Service

# mọi use case của một module
rg -n "Task" Pegaxus.Service/HandoverService/IHandoverService.cs

# chỗ đăng ký DI của một service
rg -n "IHandoverService" Pegaxus.API/Extensions/ServiceCollectionExtensions.cs

# test của một luật
rg -n -i "final.*invoice|invoice.*final" Pegaxus.Service.Test

# migration đã đụng tới một bảng
rg -l "HandoverRecords" Pegaxus.Repository/Migrations
```
