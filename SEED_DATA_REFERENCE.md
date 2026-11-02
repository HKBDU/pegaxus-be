# Pegaxus Seed Data Reference

Tài liệu này mô tả dữ liệu mẫu được tạo bởi `Pegaxus.API/AppDbContextSeedData.cs`.

Seed chạy tự động khi API khởi động, sau khi EF Core chạy migration. Dữ liệu được tìm theo email hoặc business code nên việc restart API không tạo bản ghi trùng.

Các trường `...Id` dùng GUID hợp lệ. Business code dùng định dạng của API: `REQ-yyyyMMdd-0000`, `TR-yyyyMMdd-0000`, `CD-yyyyMMdd-0000`, `VEH-yyyyMMdd-0000`, `PRV-yyyyMMdd-0000`. Bộ seed giữ ngày mã cố định `20261010` để chạy lại không sinh mã mới. Các mã, biển số và giấy tờ vẫn là dữ liệu mẫu, không xác nhận đăng ký thực tế.

## Tài khoản có sẵn

Mật khẩu mặc định của toàn bộ tài khoản: `pegaxusDemo@123`

### Customer

| Email | Tên | Horse được sở hữu |
|---|---|---|
| `trinhnganquan@gmail.com` | Trinh Ng An Quan | `HRS-20261010-0001` đến `HRS-20261010-0004` |
| `batquyquan@gmail.com` | Bat Quy Quan | `HRS-20261010-0005` đến `HRS-20261010-0008` |
| `kimvetuan@gmail.com` | Kim Ve Tuan | `HRS-20261010-0009` đến `HRS-20261010-0012` |
| `khuyenvkse200481@fpt.edu.vn` | Khuyen Vkse200481 | `HRS-20261010-0013` đến `HRS-20261010-0016` |

### Employee

| Email | Role | Employee code |
|---|---|---|
| `diechahien@gmail.com` | `LOGISTICS_MANAGER` | `EMP-20260927-0001` |
| `huyvog6226@gmail.com` | `TRANSPORT_SPECIALIST` | `EMP-20260927-0002` |
| `hoanguyen91011@gmail.com` | `FLEET_ROUTE_COORDINATOR` | `EMP-20260927-0003` |
| `duongngo2209@gmail.com` | `DRIVER` | `EMP-20260927-0004` |
| `quocbao.nguyen16102006@gmail.com` | `ESCORT` | `EMP-20260927-0005` |

## Horse

Tổng cộng 16 horse. Mỗi customer có đúng 4 horse riêng, với `microchipNumber` gồm 15 chữ số và `passportNumber` duy nhất.

Mã `HRS-...` là khóa tham chiếu trong seed, nằm trong `passportNumber` với tiền tố `PEGAXUS-`; `horseId` vẫn là GUID.

| Customer | Mã tham chiếu seed |
|---|---|
| `trinhnganquan@gmail.com` | `HRS-20261010-0001`, `HRS-20261010-0002`, `HRS-20261010-0003`, `HRS-20261010-0004` |
| `batquyquan@gmail.com` | `HRS-20261010-0005`, `HRS-20261010-0006`, `HRS-20261010-0007`, `HRS-20261010-0008` |
| `kimvetuan@gmail.com` | `HRS-20261010-0009`, `HRS-20261010-0010`, `HRS-20261010-0011`, `HRS-20261010-0012` |
| `khuyenvkse200481@fpt.edu.vn` | `HRS-20261010-0013`, `HRS-20261010-0014`, `HRS-20261010-0015`, `HRS-20261010-0016` |

## Booking

Tất cả booking đều được gắn horse thuộc đúng customer sở hữu. C1–C4 bên dưới chỉ bốn customer theo thứ tự trên.

| Booking code | Customer | Status | Horse liên kết | Mục đích |
|---|---|---|---|---|
| `REQ-20261010-0001` | Trinh Ng An Quan | `DRAFT` | C1-001, C1-002 | Test chỉnh sửa booking |
| `REQ-20261010-0002` | Bat Quy Quan | `PENDING` | C2-001, C2-002 | Test submit/approve/reject |
| `REQ-20261010-0003` | Kim Ve Tuan | `APPROVED` | C3-001, C3-002, C3-003 | Approved booking chưa có TransportPlan |
| `REQ-20261010-0004` | Khuyen Vkse200481 | `APPROVED` | C4-001, C4-002 | Approved booking, transport `PLANNING`, chưa có TransportPlan |
| `REQ-20261010-0005` | Trinh Ng An Quan | `APPROVED` | C1-003, C1-004 | Approved booking, transport `READY`, có compliance dossier (chưa có plan, assignment, route) |
| `REQ-20261010-0006` | Bat Quy Quan | `REJECTED` | C2-003 | Test negative path và rejection |

Booking có lịch sử trạng thái phù hợp cho các case `PENDING`, `APPROVED` và `REJECTED`.

## Transport

| Transport code | Booking | Status |
|---|---|---|
| `TR-20261010-0003` | `REQ-20261010-0003` | `CREATED` |
| `TR-20261010-0004` | `REQ-20261010-0004` | `PLANNING` |
| `TR-20261010-0005` | `REQ-20261010-0005` | `READY` |

## TransportPlan, EmployeeTransportPlan và RoutePlan

Seed không tạo `TransportPlan`, `EmployeeTransportPlan` hay `RoutePlan`. Tạo kế hoạch vận chuyển, phân công nhân sự và route plan qua API để test luồng này. Transport `TR-20261010-0003/0004/0005` vẫn được seed, nhưng chưa có plan.

## Location và Route

Seed nạp catalog location từ `Pegaxus.API/Data/locations-camelcase.json`, dùng GUID ổn định theo ID của catalog. Seed không tạo route; tạo qua API khi lập kế hoạch vận chuyển.

`locationId` số trong JSON là định danh cố định để tạo GUID, không phải số thứ tự: không đánh lại số hoặc dùng lại ID cho địa điểm khác. Catalog hiện có 300 địa điểm; 73 địa điểm bổ sung dùng ID `301`–`373` để tránh các ID đã được dùng. Khi mở rộng, cấp ID mới lớn hơn ID lớn nhất từng dùng, kể cả ID của địa điểm không còn trong catalog. Seed giữ nguyên địa điểm cũ và liên kết đang dùng, chỉ thêm địa điểm thiếu; vì vậy tổng số location trong DB có thể lớn hơn catalog.

[Báo cáo đối chiếu từng địa điểm ngày 02/11/2026](docs/location-verification-2026-11-02.md) ghi rõ sai lệch địa chỉ/tọa độ, phạm vi chỉ định BCP và các mục chưa đủ bằng chứng. Đã sửa 51 mục trong catalog theo nguồn công bố, giữ nguyên ID. 7 cặp tọa độ lấy từ điểm do đơn vị công bố; 35 cặp chưa xác nhận được để `null`; 9 điểm đại diện khu/cơ sở hiện có được giữ, chưa xác minh là cổng. Địa điểm thiếu tọa độ vẫn tra theo tên được, nhưng không xuất hiện trong tìm kiếm quanh vị trí hoặc tự nhận diện đến điểm bằng geofence. Đây là kiểm chứng từ xa; giờ tiếp nhận, cổng xe ngựa và quyền nhập lô hàng cụ thể cần xác nhận riêng. DB hiện hữu chưa được cập nhật; seeder tiếp tục giữ bản ghi đã có, kể cả bản ghi đã chỉnh sửa hoặc xóa mềm.

## Vehicle và TransportProvider

Seed tạo 10 vehicle và 10 provider khi API khởi động:

| Danh mục | Business code | Trạng thái | Chi tiết |
|---|---|---|---|
| Vehicle | `VEH-20261010-0001` đến `VEH-20261010-0010` | `AVAILABLE` | 6 `ROAD`, 2 `AIR`, 2 `SEA`; sức chứa 2–9 ngựa; quốc gia `FR` |
| TransportProvider | `PRV-20261010-0001` đến `PRV-20261010-0010` | `ACTIVE` | Hỗ trợ `AIR`, `ROAD`, `SEA`; email mẫu thuộc `example.com`; mã số thuế `FR00000000001` đến `FR00000000010` |

Vehicle đường bộ có biển số `GX-001-FR` đến `GX-006-FR`. Mã đăng ký dùng `FR-REG-2026-0001` đến `FR-REG-2026-0010`, mã bảo hiểm dùng `FR-INS-2026-0001` đến `FR-INS-2026-0010`. Ngày hết hạn đăng ký và bảo hiểm được đặt sau ngày tạo hai năm.

ID được tạo ổn định từ business code. Chạy lại chỉ bổ sung bản ghi thiếu, không ghi đè bản ghi đã chỉnh sửa hoặc khôi phục bản ghi đã xoá mềm. Seed bỏ qua bản ghi trùng ID, business code, biển số vehicle hoặc mã số thuế provider. Không tự gán vehicle/provider vào kế hoạch hay chặng vận chuyển.

## Compliance

### ComplianceRequirement

Seed hiện có 10 requirement master:

`EQUINE_PASSPORT`, `EHC_GB_TO_EU_8438`, `EHC_EU_TO_GB`, `LAB_TEST_EIA_COGGINS`, `CHED_A_DOCUMENT`, `IPAFFS_PRE_NOTIF`, `ATA_CARNET_EXPORT`, `ATA_CARNET_IMPORT`, `JOURNEY_LOG_EC1_2005`, `DRIVER_COMPETENCE_CERT`.

### DossierTemplate

| Template code | Status | Requirement chính |
|---|---|---|
| `TPL-EU-GB` | `ACTIVE` | Passport, EHC EU-GB, IPAFFS, ATA Carnet |
| `TPL-EQUINE-IDENTITY` | `DRAFT` | Passport, Journey Log |

### ComplianceDossier

| Dossier code | Transport | Booking | Template | Status |
|---|---|---|---|---|
| `CD-20261010-0001` | `TR-20261010-0005` | `REQ-20261010-0005` | `TPL-EU-GB` | `IN_PREPARATION` |

Dossier có 3 `DossierRequirement`:

| Requirement | Horse | Status |
|---|---|---|
| `EQUINE_PASSPORT` | C1-003 | `PROVIDED` |
| `EHC_EU_TO_GB` | Không gắn horse | `PENDING` |
| `IPAFFS_PRE_NOTIF` | Không gắn horse | `REQUESTED` |

## Chưa seed trong bộ dữ liệu này

Các phần sau chưa tạo dữ liệu mẫu:

- Compliance document và document version thực tế
- Document request/comment/review
- Notification và AccountNotification
- RoutePlanRouteVehicle
- Authority submission, QuarantineProof, CustomsClearanceProof
- Invoice, Receipt, Claim, Handover và Incident

Các dữ liệu trên vẫn có thể tạo tiếp qua API tương ứng sau khi dùng các bản ghi seed hiện tại.
