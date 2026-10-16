# Pegaxus Seed Data Reference

Tài liệu này mô tả dữ liệu demo được tạo bởi `Pegaxus.API/AppDbContextSeedData.cs`.

Seed chạy tự động khi API khởi động, sau khi EF Core chạy migration. Dữ liệu demo được tìm theo email hoặc business code nên việc restart API không tạo bản ghi trùng.

## Tài khoản có sẵn

Mật khẩu mặc định của toàn bộ tài khoản: `pegaxusDemo@123`

### Customer

| Email | Tên | Horse được sở hữu |
|---|---|---|
| `trinhnganquan@gmail.com` | Trinh Ng An Quan | `DEMO-C1-HORSE-001` đến `DEMO-C1-HORSE-004` |
| `batquyquan@gmail.com` | Bat Quy Quan | `DEMO-C2-HORSE-001` đến `DEMO-C2-HORSE-004` |
| `kimvetuan@gmail.com` | Kim Ve Tuan | `DEMO-C3-HORSE-001` đến `DEMO-C3-HORSE-004` |
| `khuyenvkse200481@fpt.edu.vn` | Khuyen Vkse200481 | `DEMO-C4-HORSE-001` đến `DEMO-C4-HORSE-004` |

### Employee

| Email | Role | Employee code |
|---|---|---|
| `diechahien@gmail.com` | `LOGISTICS_MANAGER` | `EMP-20260927-0001` |
| `huyvog6226@gmail.com` | `TRANSPORT_SPECIALIST` | `EMP-20260927-0002` |
| `hoanguyen91011@gmail.com` | `FLEET_ROUTE_COORDINATOR` | `EMP-20260927-0003` |
| `duongngo2209@gmail.com` | `DRIVER` | `EMP-20260927-0004` |
| `quocbao.nguyen16102006@gmail.com` | `ESCORT` | `EMP-20260927-0005` |

## Horse

Tổng cộng 16 horse. Mỗi customer có đúng 4 horse riêng, với `microchipNumber` và `passportNumber` duy nhất.

| Customer | Horse codes |
|---|---|
| `trinhnganquan@gmail.com` | `DEMO-C1-HORSE-001`, `DEMO-C1-HORSE-002`, `DEMO-C1-HORSE-003`, `DEMO-C1-HORSE-004` |
| `batquyquan@gmail.com` | `DEMO-C2-HORSE-001`, `DEMO-C2-HORSE-002`, `DEMO-C2-HORSE-003`, `DEMO-C2-HORSE-004` |
| `kimvetuan@gmail.com` | `DEMO-C3-HORSE-001`, `DEMO-C3-HORSE-002`, `DEMO-C3-HORSE-003`, `DEMO-C3-HORSE-004` |
| `khuyenvkse200481@fpt.edu.vn` | `DEMO-C4-HORSE-001`, `DEMO-C4-HORSE-002`, `DEMO-C4-HORSE-003`, `DEMO-C4-HORSE-004` |

## Booking

Tất cả booking demo đều được gắn horse thuộc đúng customer sở hữu.

| Booking code | Customer | Status | Horse liên kết | Mục đích |
|---|---|---|---|---|
| `REQ-20261010-DEMO-0001` | Trinh Ng An Quan | `DRAFT` | C1-001, C1-002 | Test chỉnh sửa booking |
| `REQ-20261010-DEMO-0002` | Bat Quy Quan | `PENDING` | C2-001, C2-002 | Test submit/approve/reject |
| `REQ-20261010-DEMO-0003` | Kim Ve Tuan | `APPROVED` | C3-001, C3-002, C3-003 | Approved booking chưa có TransportPlan |
| `REQ-20261010-DEMO-0004` | Khuyen Vkse200481 | `APPROVED` | C4-001, C4-002 | Approved booking, transport `PLANNING`, chưa có TransportPlan |
| `REQ-20261010-DEMO-0005` | Trinh Ng An Quan | `APPROVED` | C1-003, C1-004 | Approved booking, transport `READY`, có compliance dossier (chưa có plan, assignment, route) |
| `REQ-20261010-DEMO-0006` | Bat Quy Quan | `REJECTED` | C2-003 | Test negative path và rejection |

Booking có lịch sử trạng thái phù hợp cho các case `PENDING`, `APPROVED` và `REJECTED`.

## Transport

| Transport code | Booking | Status |
|---|---|---|
| `TR-20261010-DEMO-0003` | `REQ-20261010-DEMO-0003` | `CREATED` |
| `TR-20261010-DEMO-0004` | `REQ-20261010-DEMO-0004` | `PLANNING` |
| `TR-20261010-DEMO-0005` | `REQ-20261010-DEMO-0005` | `READY` |

## TransportPlan, EmployeeTransportPlan và RoutePlan

Seed không tạo `TransportPlan`, `EmployeeTransportPlan` hay `RoutePlan`. Tạo kế hoạch vận chuyển, phân công nhân sự và route plan qua API để test luồng này. Transport `TR-20261010-DEMO-0003/0004/0005` vẫn được seed, nhưng chưa có plan.

## Location và Route

### Location

| Code | Tên | Quốc gia |
|---|---|---|
| `LOC-DEMO-PARIS` | Paris Stable | `FR` |
| `LOC-DEMO-LONDON` | London Stable | `GB` |
| `LOC-DEMO-BERLIN` | Berlin Stable | `DE` |

### Route

| Route code | From | To | Distance | Duration |
|---|---|---|---:|---:|
| `ROUTE-DEMO-FR-GB` | Paris Stable | London Stable | 920.5 km | 1260 phút |
| `ROUTE-DEMO-DE-FR` | Berlin Stable | Paris Stable | 1050 km | 1440 phút |

## Compliance

### ComplianceRequirement

Seed hiện có 10 requirement master:

`EQUINE_PASSPORT`, `EHC_GB_TO_EU_8438`, `EHC_EU_TO_GB`, `LAB_TEST_EIA_COGGINS`, `CHED_A_DOCUMENT`, `IPAFFS_PRE_NOTIF`, `ATA_CARNET_EXPORT`, `ATA_CARNET_IMPORT`, `JOURNEY_LOG_EC1_2005`, `DRIVER_COMPETENCE_CERT`.

### DossierTemplate

| Template code | Status | Requirement chính |
|---|---|---|
| `TPL-DEMO-EU-GB` | `ACTIVE` | Passport, EHC EU-GB, IPAFFS, ATA Carnet |
| `TPL-DEMO-DRAFT` | `DRAFT` | Passport, Journey Log |

### ComplianceDossier

| Dossier code | Transport | Booking | Template | Status |
|---|---|---|---|---|
| `CDOS-20261010-DEMO-0001` | `TR-20261010-DEMO-0005` | `REQ-20261010-DEMO-0005` | `TPL-DEMO-EU-GB` | `IN_PREPARATION` |

Dossier có 3 `DossierRequirement`:

| Requirement | Horse | Status |
|---|---|---|
| `EQUINE_PASSPORT` | C1-003 | `PROVIDED` |
| `EHC_EU_TO_GB` | Không gắn horse | `PENDING` |
| `IPAFFS_PRE_NOTIF` | Không gắn horse | `REQUESTED` |

## Chưa seed trong bộ demo này

Các phần sau chưa tạo dữ liệu mẫu:

- Compliance document và document version thực tế
- Document request/comment/review
- Notification và AccountNotification
- Vehicle, TransportProvider và RoutePlanRouteVehicle
- Authority submission, QuarantineProof, CustomsClearanceProof
- Invoice, Receipt, Claim, Handover và Incident

Các dữ liệu trên vẫn có thể tạo tiếp qua API tương ứng sau khi dùng các bản ghi seed hiện tại.
