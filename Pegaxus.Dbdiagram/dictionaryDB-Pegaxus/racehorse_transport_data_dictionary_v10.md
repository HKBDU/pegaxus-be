# Racehorse Cross-Border Transport System
## Data Dictionary V10 — Ý nghĩa từng bảng và từng field

> Tài liệu này được đối chiếu với `racehorse_transport_physical_erd_final_v10.dbml`.
> Mục tiêu: giải thích **từng field**, kiểu dữ liệu, PK/FK, bắt buộc/nullable và ý nghĩa nghiệp vụ.

### Quy ước

- **PK**: Primary Key.
- **FK**: Foreign Key.
- **Required = Yes**: field có `not null` trong DBML.
- Field nullable không có nghĩa luôn được phép bỏ trống trong mọi trạng thái; một số business rule được backend kiểm tra.
- Các field `created_at` / `updated_at` là metadata thời gian của bản ghi.
- V8 dùng **soft delete** cho 11 bảng master/profile/config: `Account`, `Customer`, `Employee`, `Horse`, `Location`, `Route`, `Vehicle`, `TransportProvider`, `ComplianceRequirement`, `DossierTemplate`, `DossierRequirement` (chỉ áp dụng cho optional requirement).
- Với các bảng này: `is_deleted` cho biết đã xóa logic hay chưa; `deleted_at` là thời điểm xóa; `deleted_by_account_id` là Account thực hiện xóa.
- `DossierRequirement.horse_id = NULL` nghĩa là requirement áp dụng cho toàn chuyến; có giá trị nghĩa là requirement áp dụng riêng cho Horse đó.
- `ComplianceRequirement.validity_period_days` là số ngày hiệu lực mặc định của requirement. Khi áp dụng vào dossier, backend tính và lưu `DossierRequirement.valid_until`.
- `DossierTemplateItem.applies_per_horse = false` tạo một `DossierRequirement` chung; `true` tạo một requirement cho từng Horse trong booking.
- Các field actor như `created_by_account_id`, `uploaded_by_account_id`, `reviewed_by_account_id`, `requested_by_account_id` và `submitted_by_account_id` lấy từ Account hiện tại trong access token.
- `AuthoritySubmission.is_required = true` dùng để xác định các authority bắt buộc phải APPROVED trước khi `ComplianceDossier` được APPROVED.
- `QuarantineProof` và `CustomsClearanceProof` là hai entity nghiệp vụ riêng; các file vật lý được lưu trong bảng `ProofAttachment`.
- `ComplianceDossier` là bộ hồ sơ do TS chuẩn bị trước khi xuất phát. Mọi kết quả kiểm dịch/hải quan tại cửa khẩu, kể cả `FAILED` hoặc `REJECTED`, chỉ cập nhật proof tương ứng và không thay đổi `ComplianceDossier.status`.
- `AuditDossier` là bảng append-only; `target_entity + target_id` là polymorphic reference được backend kiểm tra.
- `CatalogAuditLog` là bảng append-only dành cho thay đổi catalog/master, ví dụ `ComplianceRequirement`; không dùng thay thế `AuditDossier`.

# 1. `Account`

**Ý nghĩa bảng:** Tài khoản đăng nhập dùng chung cho Customer hoặc Employee.

**Soft delete:** Bảng này thuộc scope soft delete của V6. Record không bị xóa vật lý khi user thực hiện thao tác xóa.

| Field | Type | Key | Required | Ý nghĩa |
|---|---|---|---|---|
| `account_id` | `uuid` | PK | No | Khóa chính định danh duy nhất bản ghi `Account`. |
| `email` | `varchar(255)` |  | Yes | Địa chỉ email dùng cho liên hệ/đăng nhập tùy ngữ cảnh của bảng. |
| `password_hash` | `varchar(255)` |  | Yes | Mật khẩu đã băm; không lưu mật khẩu thô. |
| `account_type` | `account_type` |  | Yes | Loại tài khoản: Customer hoặc Employee. Enum `account_type`: CUSTOMER, EMPLOYEE. |
| `status` | `account_status` |  | Yes | Trạng thái hiện tại của bản ghi theo enum tương ứng của bảng. Enum `account_status`: ACTIVE, INACTIVE, SUSPENDED. |
| `last_login_at` | `timestamp` |  | No | Thời điểm đăng nhập gần nhất. |
| `email_verified_at` | `timestamp` |  | No | Thời điểm email được xác thực; NULL nếu chưa xác thực. |
| `avatar_url` | `varchar(500)` |  | No | URL ảnh đại diện của tài khoản. |
| `is_deleted` | `boolean` |  | Yes | Cờ soft delete. `false` = record đang hoạt động/tồn tại về mặt logic; `true` = đã bị xóa logic nhưng vẫn giữ trong database để bảo toàn lịch sử và các FK. |
| `deleted_at` | `timestamp` |  | No | Thời điểm record bị soft delete; NULL nếu record chưa bị xóa logic. |
| `deleted_by_account_id` | `uuid` | FK | No | Account thực hiện thao tác soft delete; NULL nếu record chưa bị xóa hoặc việc xóa không có actor được ghi nhận. |
| `created_at` | `timestamp` |  | Yes | Thời điểm bản ghi được tạo. |
| `updated_at` | `timestamp` |  | Yes | Thời điểm bản ghi được cập nhật gần nhất. |

# 2. `RefreshToken`

**Ý nghĩa bảng:** RefreshToken: Token dùng để cấp lại Access Token cho một Account. Một Account có thể có nhiều RefreshToken cho nhiều thiết bị/phiên đăng nhập. Database chỉ lưu token_hash, không lưu raw token.

| Field | Type | Key | Required | Ý nghĩa |
|---|---|---|---|---|
| `refresh_token_id` | `uuid` | PK | No | Khóa chính của refresh token/session record. |
| `account_id` | `uuid` | FK | Yes | Account sở hữu refresh token này. |
| `token_hash` | `varchar(255)` |  | Yes | Giá trị refresh token đã được băm; không lưu raw token. |
| `expires_at` | `timestamp` |  | Yes | Thời điểm token/notification hết hạn tùy bảng. |
| `created_at` | `timestamp` |  | Yes | Thời điểm bản ghi được tạo. |
| `revoked_at` | `timestamp` |  | No | Thời điểm refresh token bị thu hồi. |
| `replaced_by_token_id` | `uuid` | FK | No | RefreshToken mới thay thế token hiện tại khi sử dụng token rotation; NULL nếu chưa bị thay thế. |
| `created_by_ip` | `varchar(64)` |  | No | Địa chỉ IP tại thời điểm refresh token được tạo. |
| `revoked_by_ip` | `varchar(64)` |  | No | Địa chỉ IP tại thời điểm refresh token bị thu hồi. |
| `device_info` | `varchar(500)` |  | No | Thông tin thiết bị/browser gắn với refresh token. |

# 3. `Customer`

**Cập nhật BE:** bổ sung `customer_code varchar(20) NOT NULL UNIQUE`, format sinh mã `CUS-YYYYMMDD-XXXX`.

**Ý nghĩa bảng:** Hồ sơ nghiệp vụ của khách hàng.

**Soft delete:** Bảng này thuộc scope soft delete của V6. Record không bị xóa vật lý khi user thực hiện thao tác xóa.

| Field | Type | Key | Required | Ý nghĩa |
|---|---|---|---|---|
| `customer_id` | `uuid` | PK | No | Khóa chính định danh duy nhất bản ghi `Customer`. |
| `account_id` | `uuid` | FK | Yes | Khóa ngoại tham chiếu `Account.account_id`. |
| `full_name` | `varchar(150)` |  | Yes | Họ và tên đầy đủ. |
| `phone` | `varchar(30)` |  | No | Số điện thoại liên hệ. |
| `date_of_birth` | `date` |  | No | Ngày sinh. |
| `address` | `varchar(500)` |  | No | Địa chỉ chi tiết. |
| `country` | `varchar(100)` |  | No | Quốc gia. |
| `identity_number` | `varchar(100)` |  | No | Số giấy tờ định danh của khách hàng. |
| `is_deleted` | `boolean` |  | Yes | Cờ soft delete. `false` = record đang hoạt động/tồn tại về mặt logic; `true` = đã bị xóa logic nhưng vẫn giữ trong database để bảo toàn lịch sử và các FK. |
| `deleted_at` | `timestamp` |  | No | Thời điểm record bị soft delete; NULL nếu record chưa bị xóa logic. |
| `deleted_by_account_id` | `uuid` | FK | No | Account thực hiện thao tác soft delete; NULL nếu record chưa bị xóa hoặc việc xóa không có actor được ghi nhận. |
| `created_at` | `timestamp` |  | Yes | Thời điểm bản ghi được tạo. |
| `updated_at` | `timestamp` |  | Yes | Thời điểm bản ghi được cập nhật gần nhất. |

# 4. `Employee`

**Cập nhật BE:** bổ sung `date_of_birth date`, `address varchar(500)` và `country varchar(100)`.

**Ý nghĩa bảng:** Hồ sơ nghiệp vụ của nhân viên và vai trò trong hệ thống.

**Soft delete:** Bảng này thuộc scope soft delete của V6. Record không bị xóa vật lý khi user thực hiện thao tác xóa.

| Field | Type | Key | Required | Ý nghĩa |
|---|---|---|---|---|
| `employee_id` | `uuid` | PK | No | Khóa chính định danh duy nhất bản ghi `Employee`. |
| `account_id` | `uuid` | FK | Yes | Khóa ngoại tham chiếu `Account.account_id`. |
| `employee_code` | `varchar(50)` |  | Yes | Mã nhân viên duy nhất. |
| `full_name` | `varchar(150)` |  | Yes | Họ và tên đầy đủ. |
| `phone` | `varchar(30)` |  | No | Số điện thoại liên hệ. |
| `date_of_birth` | `date` |  | No | Ngày sinh. |
| `role` | `employee_role` |  | Yes | Vai trò nghiệp vụ của Employee. Enum `employee_role`: LOGISTICS_MANAGER, TRANSPORT_SPECIALIST, FLEET_ROUTE_COORDINATOR, DRIVER, ESCORT. |
| `is_active` | `boolean` |  | Yes | Cho biết bản ghi nhân viên còn đang hoạt động hay không. |
| `is_deleted` | `boolean` |  | Yes | Cờ soft delete. `false` = record đang hoạt động/tồn tại về mặt logic; `true` = đã bị xóa logic nhưng vẫn giữ trong database để bảo toàn lịch sử và các FK. |
| `deleted_at` | `timestamp` |  | No | Thời điểm record bị soft delete; NULL nếu record chưa bị xóa logic. |
| `deleted_by_account_id` | `uuid` | FK | No | Account thực hiện thao tác soft delete; NULL nếu record chưa bị xóa hoặc việc xóa không có actor được ghi nhận. |
| `created_at` | `timestamp` |  | Yes | Thời điểm bản ghi được tạo. |
| `updated_at` | `timestamp` |  | Yes | Thời điểm bản ghi được cập nhật gần nhất. |

# 5. `Horse`

**Ý nghĩa bảng:** Hồ sơ nhận dạng và thông tin cơ bản hiện tại của một con ngựa.

**Lưu ý:** `weight_kg` và `height_cm` là **giá trị hiện tại** của Horse; V6 vẫn chưa lưu lịch sử thay đổi cân nặng/chiều cao.

**Soft delete:** Bảng này thuộc scope soft delete của V6. Record không bị xóa vật lý khi user thực hiện thao tác xóa.

| Field | Type | Key | Required | Ý nghĩa |
|---|---|---|---|---|
| `horse_id` | `uuid` | PK | No | Khóa chính định danh duy nhất bản ghi `Horse`. |
| `customer_id` | `uuid` | FK | Yes | Khóa ngoại tham chiếu `Customer.customer_id`. |
| `horse_name` | `varchar(150)` |  | Yes | Tên của ngựa. |
| `passport_number` | `varchar(100)` |  | No | Số hộ chiếu ngựa, nếu có. |
| `microchip_number` | `varchar(100)` |  | No | Mã microchip nhận dạng ngựa, nếu có. |
| `breed` | `varchar(100)` |  | No | Giống ngựa. |
| `sex` | `horse_sex` |  | No | Giới tính/trạng thái giới tính của ngựa. Enum `horse_sex`: MALE, FEMALE, GELDING. |
| `date_of_birth` | `date` |  | No | Ngày sinh. |
| `color` | `varchar(100)` |  | No | Màu lông/đặc điểm màu sắc của ngựa. |
| `country_of_origin` | `varchar(100)` |  | No | Quốc gia xuất xứ của ngựa. |
| `weight_kg` | `decimal(6,2)` |  | No | Cân nặng hiện tại của ngựa, đơn vị kg. |
| `height_cm` | `decimal(5,2)` |  | No | Chiều cao hiện tại của ngựa, đơn vị cm. |
| `identification_notes` | `text` |  | No | Ghi chú bổ sung để nhận dạng ngựa. |
| `is_deleted` | `boolean` |  | Yes | Cờ soft delete. `false` = record đang hoạt động/tồn tại về mặt logic; `true` = đã bị xóa logic nhưng vẫn giữ trong database để bảo toàn lịch sử và các FK. |
| `deleted_at` | `timestamp` |  | No | Thời điểm record bị soft delete; NULL nếu record chưa bị xóa logic. |
| `deleted_by_account_id` | `uuid` | FK | No | Account thực hiện thao tác soft delete; NULL nếu record chưa bị xóa hoặc việc xóa không có actor được ghi nhận. |
| `created_at` | `timestamp` |  | Yes | Thời điểm bản ghi được tạo. |
| `updated_at` | `timestamp` |  | Yes | Thời điểm bản ghi được cập nhật gần nhất. |

# 6. `Booking`

**Ý nghĩa bảng:** Yêu cầu vận chuyển do Customer tạo trước khi hình thành chuyến Transport thực tế.

| Field | Type | Key | Required | Ý nghĩa |
|---|---|---|---|---|
| `booking_id` | `uuid` | PK | No | Khóa chính định danh duy nhất bản ghi `Booking`. |
| `booking_code` | `varchar(50)` |  | Yes | Mã nghiệp vụ duy nhất của `Booking`. |
| `customer_id` | `uuid` | FK | Yes | Khóa ngoại tham chiếu `Customer.customer_id`. |
| `assigned_employee_id` | `uuid` | FK | No | Employee đang được phân công phụ trách Booking; có thể NULL trước khi được assign. |
| `status` | `booking_status` |  | Yes | Trạng thái hiện tại của bản ghi theo enum tương ứng của bảng. Enum `booking_status`: PENDING, APPROVED, REJECTED, CANCELLED. |
| `origin_address` | `varchar(500)` |  | Yes | Địa chỉ xuất phát theo yêu cầu Booking. |
| `origin_country` | `varchar(100)` |  | Yes | Quốc gia xuất phát theo Booking. |
| `destination_address` | `varchar(500)` |  | Yes | Địa chỉ đích theo Booking. |
| `destination_country` | `varchar(100)` |  | Yes | Quốc gia đích theo Booking. |
| `origin_latitude` | `decimal(10,4)` |  | No | Vĩ độ điểm xuất phát Customer chọn trên bản đồ (V9). NULL với Booking tạo trước V9. |
| `origin_longitude` | `decimal(10,4)` |  | No | Kinh độ điểm xuất phát; luôn đi cùng `origin_latitude`. |
| `destination_latitude` | `decimal(10,4)` |  | No | Vĩ độ điểm đích Customer chọn trên bản đồ (V9). |
| `destination_longitude` | `decimal(10,4)` |  | No | Kinh độ điểm đích; luôn đi cùng `destination_latitude`. |
| `requested_departure_date` | `date` |  | Yes | Ngày Customer mong muốn khởi hành. |
| `requested_arrival_date` | `date` |  | No | Ngày Customer mong muốn đến nơi. |
| `special_requirements` | `text` |  | No | Các yêu cầu vận chuyển đặc biệt của Booking. |
| `customer_note` | `text` |  | No | Ghi chú do Customer cung cấp. |
| `submitted_at` | `timestamp` |  | No | Thời điểm Customer gửi Booking để hệ thống xử lý. |
| `approved_at` | `timestamp` |  | No | Thời điểm được phê duyệt. |
| `rejected_at` | `timestamp` |  | No | Thời điểm bị từ chối. |
| `cancelled_at` | `timestamp` |  | No | Thời điểm nghiệp vụ/bản ghi bị hủy. |
| `rejection_reason` | `text` |  | No | Lý do bị từ chối. |
| `cancellation_reason` | `text` |  | No | Lý do hủy. |
| `created_at` | `timestamp` |  | Yes | Thời điểm bản ghi được tạo. |
| `updated_at` | `timestamp` |  | Yes | Thời điểm bản ghi được cập nhật gần nhất. |

# 7. `BookingHorse`

**Ý nghĩa bảng:** Bảng trung gian N-N gắn các Horse vào Booking.

| Field | Type | Key | Required | Ý nghĩa |
|---|---|---|---|---|
| `booking_id` | `uuid` | FK | Yes | Khóa ngoại tham chiếu `Booking.booking_id`. |
| `horse_id` | `uuid` | FK | Yes | Khóa ngoại tham chiếu `Horse.horse_id`. |
| `special_handling_notes` | `text` |  | No | Hướng dẫn xử lý/chăm sóc đặc biệt cho Horse trong Booking. |
| `created_at` | `timestamp` |  | Yes | Thời điểm bản ghi được tạo. |

# 8. `BookingStatusHistory`

**Ý nghĩa bảng:** BookingStatusHistory: Lịch sử thay đổi trạng thái Booking. type cho biết hành động nghiệp vụ nào đã gây ra thay đổi status.

**Lưu ý:** đây là business history/audit cho thay đổi trạng thái. Hệ thống không có centralized `AuditLog`; riêng compliance dossier có `AuditDossier` append-only.

| Field | Type | Key | Required | Ý nghĩa |
|---|---|---|---|---|
| `booking_status_history_id` | `uuid` | PK | No | Khóa chính định danh duy nhất bản ghi `BookingStatusHistory`. |
| `booking_id` | `uuid` | FK | Yes | Khóa ngoại tham chiếu `Booking.booking_id`. |
| `type` | `booking_history_type` |  | Yes | Loại hành động gây ra thay đổi Booking: SUBMIT, APPROVE, REJECT hoặc CANCEL. Enum `booking_history_type`: SUBMIT, APPROVE, REJECT, CANCEL. |
| `previous_status` | `booking_status` |  | No | Trạng thái trước khi hành động xảy ra. Enum `booking_status`: PENDING, APPROVED, REJECTED, CANCELLED. |
| `new_status` | `booking_status` |  | Yes | Trạng thái sau khi hành động xảy ra. Enum `booking_status`: PENDING, APPROVED, REJECTED, CANCELLED. |
| `changed_by_account_id` | `uuid` | FK | Yes | Account thực hiện hành động thay đổi trạng thái Booking. |
| `reason` | `text` |  | No | Lý do của hành động/thay đổi. |
| `changed_at` | `timestamp` |  | Yes | Thời điểm thay đổi trạng thái. |

# 9. `Transport`

**Ý nghĩa bảng:** Chuyến vận chuyển thực tế được tạo từ một Booking đã được chấp nhận.

| Field | Type | Key | Required | Ý nghĩa |
|---|---|---|---|---|
| `transport_id` | `uuid` | PK | No | Khóa chính định danh duy nhất bản ghi `Transport`. |
| `transport_code` | `varchar(50)` |  | Yes | Mã nghiệp vụ duy nhất của `Transport`. |
| `booking_id` | `uuid` | FK | Yes | Khóa ngoại tham chiếu `Booking.booking_id`. |
| `status` | `transport_status` |  | Yes | Trạng thái hiện tại của bản ghi theo enum tương ứng của bảng. Enum `transport_status`: CREATED, PLANNING, READY, IN_TRANSIT, COMPLETED, CANCELLED. |
| `actual_departure_at` | `timestamp` |  | No | Thời điểm khởi hành thực tế. |
| `actual_arrival_at` | `timestamp` |  | No | Thời điểm đến thực tế. |
| `completed_at` | `timestamp` |  | No | Thời điểm hoàn thành. |
| `cancelled_at` | `timestamp` |  | No | Thời điểm nghiệp vụ/bản ghi bị hủy. |
| `cancellation_reason` | `text` |  | No | Lý do hủy. |
| `created_at` | `timestamp` |  | Yes | Thời điểm bản ghi được tạo. |
| `updated_at` | `timestamp` |  | Yes | Thời điểm bản ghi được cập nhật gần nhất. |

# 10. `TransportPlan`

**Ý nghĩa bảng:** Kế hoạch vận hành của một Transport; có thể có nhiều phiên kế hoạch theo thời gian.

| Field | Type | Key | Required | Ý nghĩa |
|---|---|---|---|---|
| `transport_plan_id` | `uuid` | PK | No | Khóa chính định danh duy nhất bản ghi `TransportPlan`. |
| `transport_id` | `uuid` | FK | Yes | Khóa ngoại tham chiếu `Transport.transport_id`. |
| `plan_code` | `varchar(50)` |  | Yes | Mã duy nhất của TransportPlan. |
| `status` | `transport_plan_status` |  | Yes | Trạng thái hiện tại của bản ghi theo enum tương ứng của bảng. Enum `transport_plan_status`: DRAFT, PENDING, APPROVED, IN_PROGRESS, COMPLETED, CANCELLED. |
| `planned_start_at` | `timestamp` |  | No | Thời điểm dự kiến bắt đầu TransportPlan. |
| `planned_end_at` | `timestamp` |  | No | Thời điểm dự kiến kết thúc TransportPlan. |
| `planning_note` | `text` |  | No | Ghi chú trong quá trình lập TransportPlan. |
| `estimated_duration_minutes` | `int` |  | No | Thời lượng ước tính, tính bằng phút. |
| `estimated_distance_km` | `decimal(10,2)` |  | No | Tổng khoảng cách ước tính của TransportPlan, đơn vị km. |
| `origin_location_id` | `uuid` | FK | No | Location điểm đi của plan (V9); khớp từ toạ độ Booking hoặc LM chọn. NULL nếu chưa xác định. |
| `destination_location_id` | `uuid` | FK | No | Location điểm đến của plan (V9). |
| `approved_by_employee_id` | `uuid` | FK | No | Employee đã phê duyệt TransportPlan. |
| `approved_at` | `timestamp` |  | No | Thời điểm được phê duyệt. |
| `created_at` | `timestamp` |  | Yes | Thời điểm bản ghi được tạo. |
| `updated_at` | `timestamp` |  | Yes | Thời điểm bản ghi được cập nhật gần nhất. |

# 11. `RoutePlan`

**Cập nhật BE:** bổ sung `primary_route_plan_id` (FK tự tham chiếu) và các trạng thái `PENDING_LEGAL_REVIEW`, `PENDING_MANAGER_APPROVAL`, `REJECTED_BY_MANAGER`, `APPROVED`.

**Ý nghĩa bảng:** RoutePlan: Một phương án kế hoạch tuyến đường thuộc một TransportPlan. Một TransportPlan có thể có nhiều RoutePlan, ví dụ phương án PRIMARY, BACKUP hoặc RETURN. Mỗi RoutePlan là tập hợp các RoutePlanRoute được sắp xếp nối tiếp từ điểm đầu đến điểm cuối.

**Business rule quan trọng:** `RoutePlan.transport_plan_id` không unique; quan hệ là `TransportPlan 1-N RoutePlan`. Việc giới hạn số lượng PRIMARY/BACKUP/RETURN đang hoạt động được kiểm tra ở business layer hoặc bằng constraint/index phù hợp, không bằng unique đơn trên `transport_plan_id`.

| Field | Type | Key | Required | Ý nghĩa |
|---|---|---|---|---|
| `route_plan_id` | `uuid` | PK | No | Khóa chính định danh duy nhất bản ghi `RoutePlan`. |
| `transport_plan_id` | `uuid` | FK | Yes | Khóa ngoại tham chiếu `TransportPlan.transport_plan_id`. Một TransportPlan có thể được tham chiếu bởi nhiều RoutePlan. |
| `route_plan_code` | `varchar(50)` |  | Yes | Mã nghiệp vụ duy nhất của `RoutePlan`. |
| `status` | `route_plan_status` |  | Yes | Trạng thái hiện tại của bản ghi theo enum tương ứng của bảng. Enum `route_plan_status`: DRAFT, CONFIRMED, ACTIVE, COMPLETED, CANCELLED. |
| `total_distance_km` | `decimal(10,2)` |  | No | Tổng khoảng cách của RoutePlan, đơn vị km. |
| `estimated_duration_minutes` | `int` |  | No | Thời lượng ước tính, tính bằng phút. |
| `route_note` | `text` |  | No | Ghi chú về Route/RoutePlan. |
| `created_at` | `timestamp` |  | Yes | Thời điểm bản ghi được tạo. |
| `updated_at` | `timestamp` |  | Yes | Thời điểm bản ghi được cập nhật gần nhất. |

# 12. `Location`

**Ý nghĩa bảng:** Location: Địa điểm master trên bản đồ. Một địa điểm chỉ tạo một lần và có thể được nhiều Route tái sử dụng, ví dụ Thành phố Hồ Chí Minh.

**Soft delete:** Bảng này thuộc scope soft delete của V6. Record không bị xóa vật lý khi user thực hiện thao tác xóa.

| Field | Type | Key | Required | Ý nghĩa |
|---|---|---|---|---|
| `location_id` | `uuid` | PK | No | Khóa chính định danh duy nhất bản ghi `Location`. |
| `location_name` | `varchar(200)` |  | Yes | Tên địa điểm. |
| `location_type` | `location_type` |  | No | Loại địa điểm trên bản đồ. Enum `location_type`: CITY, BORDER_GATE, AIRPORT, SEAPORT, STABLE, REST_STOP, OTHER. |
| `address` | `varchar(500)` |  | No | Địa chỉ chi tiết. |
| `city` | `varchar(100)` |  | No | Thành phố. |
| `state_province` | `varchar(100)` |  | No | Bang/tỉnh/thành hoặc đơn vị hành chính tương đương. |
| `country` | `varchar(100)` |  | Yes | Quốc gia. |
| `latitude` | `decimal(10,4)` |  | No | Vĩ độ của Location. |
| `longitude` | `decimal(10,4)` |  | No | Kinh độ của Location. |
| `location_note` | `text` |  | No | Ghi chú bổ sung về Location. |
| `is_deleted` | `boolean` |  | Yes | Cờ soft delete. `false` = record đang hoạt động/tồn tại về mặt logic; `true` = đã bị xóa logic nhưng vẫn giữ trong database để bảo toàn lịch sử và các FK. |
| `deleted_at` | `timestamp` |  | No | Thời điểm record bị soft delete; NULL nếu record chưa bị xóa logic. |
| `deleted_by_account_id` | `uuid` | FK | No | Account thực hiện thao tác soft delete; NULL nếu record chưa bị xóa hoặc việc xóa không có actor được ghi nhận. |
| `created_at` | `timestamp` |  | Yes | Thời điểm bản ghi được tạo. |
| `updated_at` | `timestamp` |  | Yes | Thời điểm bản ghi được cập nhật gần nhất. |

# 13. `Route`

**Ý nghĩa bảng:** Route: Một tuyến đường có hướng nối từ một Location bắt đầu đến một Location kết thúc. Route có thể được tái sử dụng trong nhiều RoutePlan.

**Soft delete:** Bảng này thuộc scope soft delete của V6. Record không bị xóa vật lý khi user thực hiện thao tác xóa.

| Field | Type | Key | Required | Ý nghĩa |
|---|---|---|---|---|
| `route_id` | `uuid` | PK | No | Khóa chính định danh duy nhất bản ghi `Route`. |
| `route_code` | `varchar(50)` |  | Yes | Mã nghiệp vụ duy nhất của `Route`. |
| `from_location_id` | `uuid` | FK | Yes | Khóa ngoại tham chiếu `Location.location_id`. |
| `to_location_id` | `uuid` | FK | Yes | Khóa ngoại tham chiếu `Location.location_id`. |
| `route_name` | `varchar(200)` |  | No | Tên mô tả của Route. |
| `transport_mode` | `transport_mode` |  | Yes | Phương thức vận chuyển: ROAD, SEA hoặc AIR. Enum `transport_mode`: ROAD, SEA, AIR. |
| `distance_km` | `decimal(10,2)` |  | No | Khoảng cách của tuyến, tính bằng km. |
| `estimated_duration_minutes` | `int` |  | No | Thời lượng ước tính, tính bằng phút. |
| `route_note` | `text` |  | No | Ghi chú về Route/RoutePlan. |
| `is_deleted` | `boolean` |  | Yes | Cờ soft delete. `false` = record đang hoạt động/tồn tại về mặt logic; `true` = đã bị xóa logic nhưng vẫn giữ trong database để bảo toàn lịch sử và các FK. |
| `deleted_at` | `timestamp` |  | No | Thời điểm record bị soft delete; NULL nếu record chưa bị xóa logic. |
| `deleted_by_account_id` | `uuid` | FK | No | Account thực hiện thao tác soft delete; NULL nếu record chưa bị xóa hoặc việc xóa không có actor được ghi nhận. |
| `created_at` | `timestamp` |  | Yes | Thời điểm bản ghi được tạo. |
| `updated_at` | `timestamp` |  | Yes | Thời điểm bản ghi được cập nhật gần nhất. |

# 14. `RoutePlanRoute`

**Cập nhật BE:** bổ sung `service_type`, `provider_reference`, `outsourcing_cost_amount`, `outsourcing_currency` và `superseded_at` để lưu outsource và route change. **V9:** bổ sung `status` (`route_segment_status`); dữ liệu cũ được backfill từ `actual_departure_at`/`actual_arrival_at`.

**Ý nghĩa bảng:** RoutePlanRoute: Bảng trung gian biểu diễn một Route được dùng trong một RoutePlan, gồm thứ tự đi, Driver/Escort phụ trách, provider thuê ngoài và thời gian dự kiến/thực tế của chính lần sử dụng Route đó.

**Business rule quan trọng:**

- `driver_employee_id` và `escort_employee_id` có thể NULL khi plan còn `DRAFT`.
- Chặng nội bộ khi được phân công phải có Driver và Escort đúng role.
- `transport_provider_id` có thể NULL với chặng nội bộ; nếu có giá trị thì chặng là outsource và không được gán vehicle/driver/escort nội bộ.
- Thiết kế hiện tại hỗ trợ một Driver, một Escort và một Provider cho mỗi chặng. Nếu nghiệp vụ cần nhiều người/provider trên cùng chặng, cần tách thành các bảng assignment N-N.

| Field | Type | Key | Required | Ý nghĩa |
|---|---|---|---|---|
| `route_plan_route_id` | `uuid` | PK | No | Khóa chính định danh duy nhất bản ghi `RoutePlanRoute`. |
| `route_plan_id` | `uuid` | FK | Yes | Khóa ngoại tham chiếu `RoutePlan.route_plan_id`. |
| `route_id` | `uuid` | FK | Yes | Khóa ngoại tham chiếu `Route.route_id`. |
| `sequence_no` | `int` |  | Yes | Thứ tự chặng trong RoutePlan; duy nhất trong cùng một RoutePlan. |
| `route_type` | `route_type` |  | Yes | Vai trò của Route trong RoutePlanRoute. Enum `route_type`: PRIMARY, BACKUP, RETURN. `RETURN` dùng cho chặng quay về. |
| `status` | `route_segment_status` |  | Yes | Tiến độ chặng (V9). Enum `route_segment_status`: PLANNED, IN_TRANSIT, COMPLETED. Mặc định PLANNED; ghi cùng lúc với `actual_departure_at` (IN_TRANSIT) và `actual_arrival_at` (COMPLETED). |
| `driver_employee_id` | `uuid` | FK | No | Driver phụ trách chặng này. Có thể NULL khi RoutePlan còn DRAFT; phải là Employee có role=DRIVER trước CONFIRMED/ACTIVE. |
| `escort_employee_id` | `uuid` | FK | No | Escort phụ trách chặng này. Phải là Employee có role=ESCORT khi chặng nội bộ được phân công. |
| `transport_provider_id` | `uuid` | FK | No | Provider thực hiện chặng thuê ngoài. NULL nghĩa là chặng nội bộ; có giá trị thì không gán vehicle/driver/escort nội bộ. |
| `planned_departure_at` | `timestamp` |  | No | Thời điểm dự kiến khởi hành của chặng. |
| `planned_arrival_at` | `timestamp` |  | No | Thời điểm dự kiến đến của chặng. |
| `actual_departure_at` | `timestamp` |  | No | Thời điểm khởi hành thực tế. |
| `actual_arrival_at` | `timestamp` |  | No | Thời điểm đến thực tế. |
| `note` | `text` |  | No | Ghi chú bổ sung. |
| `created_at` | `timestamp` |  | Yes | Thời điểm bản ghi được tạo. |
| `updated_at` | `timestamp` |  | Yes | Thời điểm bản ghi được cập nhật gần nhất. |

# 15. `Vehicle`

**Cập nhật BE:** enum `vehicle_status` bổ sung `SUSPENDED` và `OUT_OF_SERVICE`.

**Ý nghĩa bảng:** Vehicle: Phương tiện vận chuyển master dùng chung cho ROAD, AIR hoặc SEA, ví dụ xe tải chở ngựa, máy bay hoặc tàu. Vehicle không thuộc cố định một TransportPlan; phương tiện được gán vào từng chặng RoutePlanRoute.

**Soft delete:** Bảng này thuộc scope soft delete của V6. Record không bị xóa vật lý khi user thực hiện thao tác xóa.

| Field | Type | Key | Required | Ý nghĩa |
|---|---|---|---|---|
| `vehicle_id` | `uuid` | PK | No | Khóa chính định danh duy nhất bản ghi `Vehicle`. |
| `vehicle_code` | `varchar(50)` |  | Yes | Mã phương tiện duy nhất. |
| `vehicle_name` | `varchar(200)` |  | No | Tên/nhãn hiển thị của phương tiện. |
| `transport_mode` | `transport_mode` |  | Yes | Phương thức vận chuyển: ROAD, SEA hoặc AIR. Enum `transport_mode`: ROAD, SEA, AIR. |
| `license_plate` | `varchar(50)` |  | No | Biển số/định danh dạng biển số nếu phương tiện có. |
| `vehicle_type` | `varchar(100)` |  | No | Loại phương tiện cụ thể trong từng mode. |
| `manufacturer` | `varchar(100)` |  | No | Nhà sản xuất phương tiện. |
| `model` | `varchar(100)` |  | No | Model/dòng phương tiện. |
| `capacity_horses` | `int` |  | No | Số lượng Horse tối đa phương tiện có thể chở theo cấu hình hệ thống. |
| `status` | `vehicle_status` |  | Yes | Trạng thái hiện tại của bản ghi theo enum tương ứng của bảng. Enum `vehicle_status`: AVAILABLE, ASSIGNED, IN_USE, MAINTENANCE, INACTIVE. |
| `registration_number` | `varchar(100)` |  | No | Số đăng ký của phương tiện. |
| `registration_expiry_date` | `date` |  | No | Ngày hết hạn đăng ký phương tiện. |
| `insurance_number` | `varchar(100)` |  | No | Số hợp đồng/chứng nhận bảo hiểm phương tiện. |
| `insurance_expiry_date` | `date` |  | No | Ngày hết hạn bảo hiểm phương tiện. |
| `special_equipment` | `text` |  | No | Mô tả thiết bị đặc biệt có trên phương tiện. |
| `country` | `varchar(100)` |  | No | Mã quốc gia ISO-2 (ví dụ `FR`, `GB`) nơi phương tiện đang hoạt động; dùng để lọc phương tiện theo quốc gia xuất phát của chặng khi phân công. NULL nếu chưa xác định. |
| `is_deleted` | `boolean` |  | Yes | Cờ soft delete. `false` = record đang hoạt động/tồn tại về mặt logic; `true` = đã bị xóa logic nhưng vẫn giữ trong database để bảo toàn lịch sử và các FK. |
| `deleted_at` | `timestamp` |  | No | Thời điểm record bị soft delete; NULL nếu record chưa bị xóa logic. |
| `deleted_by_account_id` | `uuid` | FK | No | Account thực hiện thao tác soft delete; NULL nếu record chưa bị xóa hoặc việc xóa không có actor được ghi nhận. |
| `created_at` | `timestamp` |  | Yes | Thời điểm bản ghi được tạo. |
| `updated_at` | `timestamp` |  | Yes | Thời điểm bản ghi được cập nhật gần nhất. |

# 16. `RoutePlanRouteVehicle`

**Ý nghĩa bảng:** RoutePlanRouteVehicle: Bảng trung gian N-N gán Vehicle vào một chặng RoutePlanRoute cụ thể. Nhờ vậy một Transport có thể đi xe -> máy bay -> xe, và cùng một Vehicle có thể được tái sử dụng ở nhiều chuyến/chặng khác nhau.

| Field | Type | Key | Required | Ý nghĩa |
|---|---|---|---|---|
| `route_plan_route_id` | `uuid` | FK | Yes | Chặng cụ thể được gán phương tiện. |
| `vehicle_id` | `uuid` | FK | Yes | Vehicle được sử dụng cho chặng. |
| `assigned_at` | `timestamp` |  | Yes | Thời điểm Vehicle được gán vào chặng. |
| `note` | `text` |  | No | Ghi chú bổ sung. |

# 17. `TransportProvider`

**Ý nghĩa bảng:** Master đơn vị/carrier/provider cung cấp dịch vụ vận chuyển.

**Soft delete:** Bảng này thuộc scope soft delete của V6. Record không bị xóa vật lý khi user thực hiện thao tác xóa.

| Field | Type | Key | Required | Ý nghĩa |
|---|---|---|---|---|
| `transport_provider_id` | `uuid` | PK | No | Khóa chính định danh duy nhất bản ghi `TransportProvider`. |
| `provider_code` | `varchar(50)` |  | Yes | Mã TransportProvider duy nhất. |
| `provider_name` | `varchar(200)` |  | Yes | Tên đơn vị vận chuyển/provider. |
| `status` | `provider_status` |  | Yes | Trạng thái hiện tại của bản ghi theo enum tương ứng của bảng. Enum `provider_status`: ACTIVE, INACTIVE, SUSPENDED. |
| `contact_name` | `varchar(150)` |  | No | Tên người liên hệ. |
| `phone` | `varchar(30)` |  | No | Số điện thoại liên hệ. |
| `email` | `varchar(255)` |  | No | Địa chỉ email dùng cho liên hệ/đăng nhập tùy ngữ cảnh của bảng. |
| `address` | `varchar(500)` |  | No | Địa chỉ chi tiết. |
| `country` | `varchar(100)` |  | No | Quốc gia. |
| `tax_code` | `varchar(100)` |  | No | Mã số thuế. |
| `supported_modes` | `varchar(100)` |  | No | Các phương thức vận chuyển provider hỗ trợ. |
| `note` | `text` |  | No | Ghi chú bổ sung. |
| `is_deleted` | `boolean` |  | Yes | Cờ soft delete. `false` = record đang hoạt động/tồn tại về mặt logic; `true` = đã bị xóa logic nhưng vẫn giữ trong database để bảo toàn lịch sử và các FK. |
| `deleted_at` | `timestamp` |  | No | Thời điểm record bị soft delete; NULL nếu record chưa bị xóa logic. |
| `deleted_by_account_id` | `uuid` | FK | No | Account thực hiện thao tác soft delete; NULL nếu record chưa bị xóa hoặc việc xóa không có actor được ghi nhận. |
| `created_at` | `timestamp` |  | Yes | Thời điểm bản ghi được tạo. |
| `updated_at` | `timestamp` |  | Yes | Thời điểm bản ghi được cập nhật gần nhất. |

# 18. `TransportPlanProvider`

**Ý nghĩa bảng:** Bảng N-N gán TransportProvider vào TransportPlan.

| Field | Type | Key | Required | Ý nghĩa |
|---|---|---|---|---|
| `transport_plan_id` | `uuid` | FK | Yes | Khóa ngoại tham chiếu `TransportPlan.transport_plan_id`. |
| `transport_provider_id` | `uuid` | FK | Yes | Khóa ngoại tham chiếu `TransportProvider.transport_provider_id`. |
| `service_type` | `varchar(100)` |  | No | Loại dịch vụ provider cung cấp trong plan này. |
| `assigned_at` | `timestamp` |  | Yes | Thời điểm được phân công/gán vào nghiệp vụ. |
| `note` | `text` |  | No | Ghi chú bổ sung. |

# 19. `EmployeeTransportPlan`

**Ý nghĩa bảng:** Bảng N-N phân công Employee tham gia một TransportPlan.

| Field | Type | Key | Required | Ý nghĩa |
|---|---|---|---|---|
| `employee_id` | `uuid` | FK | Yes | Khóa ngoại tham chiếu `Employee.employee_id`. |
| `transport_plan_id` | `uuid` | FK | Yes | Khóa ngoại tham chiếu `TransportPlan.transport_plan_id`. |
| `responsibility` | `varchar(150)` |  | No | Trách nhiệm/vai trò cụ thể của Employee trong TransportPlan. |
| `assigned_at` | `timestamp` |  | Yes | Thời điểm được phân công/gán vào nghiệp vụ. |
| `is_primary` | `boolean` |  | Yes | Cho biết đây có phải nhân sự chính của phân công hay không. |
| `note` | `text` |  | No | Ghi chú bổ sung. |

# 20. `HandoverRecord`

**Ý nghĩa bảng:** HandoverRecord: Một lần bàn giao trong Transport. from_type/to_type xác định bên giao/nhận là CUSTOMER, EMPLOYEE hay PROVIDER (đối tác vận chuyển); from/to_route_plan_route_id xác định chặng vừa kết thúc và chặng tiếp theo.

**Business rule quan trọng:**

- `from_type = CUSTOMER` ⇒ `from_customer_id` có giá trị và `from_employee_id = NULL`; nếu `EMPLOYEE` thì ngược lại.
- `to_type = CUSTOMER` ⇒ `to_customer_id` có giá trị và `to_employee_id = NULL`; nếu `EMPLOYEE` thì ngược lại.
- **V9:** `from_type`/`to_type = PROVIDER` ⇒ chỉ `from_transport_provider_id`/`to_transport_provider_id` có giá trị (mỗi bên đúng một trong ba id: customer, employee, provider). Quy tắc XOR do backend kiểm tra.
- Nếu cả `from_route_plan_route_id` và `to_route_plan_route_id` đều có giá trị: hai chặng phải thuộc cùng `RoutePlan` và `to.sequence_no = from.sequence_no + 1`.
- Với bàn giao Employee → Employee, Employee bên giao/nhận phải khớp Driver của chặng from/to tương ứng.

| Field | Type | Key | Required | Ý nghĩa |
|---|---|---|---|---|
| `handover_record_id` | `uuid` | PK | No | Khóa chính định danh duy nhất bản ghi `HandoverRecord`. |
| `transport_id` | `uuid` | FK | Yes | Khóa ngoại tham chiếu `Transport.transport_id`. |
| `handover_code` | `varchar(50)` |  | Yes | Mã nghiệp vụ duy nhất của `HandoverRecord`. |
| `sequence_no` | `int` |  | Yes | Thứ tự lần bàn giao trong cùng Transport. |
| `handover_type` | `handover_type` |  | Yes | Loại bàn giao: pickup, transfer, border handover, carrier handover, delivery hoặc khác. Enum `handover_type`: PICKUP, TRANSFER, BORDER_HANDOVER, CARRIER_HANDOVER, DELIVERY, OTHER. |
| `status` | `handover_status` |  | Yes | Trạng thái hiện tại của bản ghi theo enum tương ứng của bảng. Enum `handover_status`: PLANNED, IN_PROGRESS, COMPLETED, CANCELLED. |
| `planned_handover_at` | `timestamp` |  | No | Thời điểm dự kiến thực hiện bàn giao. |
| `actual_handover_at` | `timestamp` |  | No | Thời điểm bàn giao thực tế. |
| `from_type` | `handover_party_type` |  | Yes | Loại bên giao: CUSTOMER, EMPLOYEE hoặc PROVIDER (V9). Enum `handover_party_type`: CUSTOMER, EMPLOYEE, PROVIDER. |
| `to_type` | `handover_party_type` |  | Yes | Loại bên nhận: CUSTOMER, EMPLOYEE hoặc PROVIDER (V9). Enum `handover_party_type`: CUSTOMER, EMPLOYEE, PROVIDER. |
| `from_customer_id` | `uuid` | FK | No | Customer bên giao; chỉ dùng khi from_type=CUSTOMER. |
| `from_employee_id` | `uuid` | FK | No | Employee bên giao; chỉ dùng khi from_type=EMPLOYEE. |
| `to_customer_id` | `uuid` | FK | No | Customer bên nhận; chỉ dùng khi to_type=CUSTOMER. |
| `to_employee_id` | `uuid` | FK | No | Employee bên nhận; chỉ dùng khi to_type=EMPLOYEE. |
| `from_transport_provider_id` | `uuid` | FK | No | TransportProvider bên giao; chỉ dùng khi from_type=PROVIDER (V9). |
| `to_transport_provider_id` | `uuid` | FK | No | TransportProvider bên nhận; chỉ dùng khi to_type=PROVIDER (V9). |
| `from_route_plan_route_id` | `uuid` | FK | No | Chặng vừa kết thúc trước bàn giao. NULL ở pickup đầu chuyến. |
| `to_route_plan_route_id` | `uuid` | FK | No | Chặng tiếp theo sau bàn giao. NULL ở delivery cuối chuyến. |
| `from_party_name` | `varchar(200)` |  | No | Tên hiển thị/snapshot của bên giao tại thời điểm bàn giao. |
| `from_contact_name` | `varchar(150)` |  | No | Tên người liên hệ phía giao. |
| `from_contact_phone` | `varchar(30)` |  | No | Số điện thoại liên hệ phía giao. |
| `to_party_name` | `varchar(200)` |  | No | Tên hiển thị/snapshot của bên nhận tại thời điểm bàn giao. |
| `to_contact_name` | `varchar(150)` |  | No | Tên người liên hệ phía nhận. |
| `to_contact_phone` | `varchar(30)` |  | No | Số điện thoại liên hệ phía nhận. |
| `location_name` | `varchar(200)` |  | No | Tên địa điểm. |
| `address` | `varchar(500)` |  | No | Địa chỉ chi tiết. |
| `country` | `varchar(100)` |  | No | Quốc gia. |
| `horse_condition_note` | `text` |  | No | Ghi chú tình trạng Horse tại thời điểm bàn giao. |
| `document_handover_note` | `text` |  | No | Ghi chú về giấy tờ được bàn giao. |
| `equipment_handover_note` | `text` |  | No | Ghi chú về thiết bị/vật dụng được bàn giao. |
| `discrepancy_note` | `text` |  | No | Ghi nhận sai lệch/bất thường phát hiện khi bàn giao. |
| `sender_signature_url` | `varchar(500)` |  | No | URL chữ ký/hình ảnh xác nhận của bên giao. |
| `receiver_signature_url` | `varchar(500)` |  | No | URL chữ ký/hình ảnh xác nhận của bên nhận. |
| `completed_at` | `timestamp` |  | No | Thời điểm hoàn thành. |
| `cancelled_at` | `timestamp` |  | No | Thời điểm nghiệp vụ/bản ghi bị hủy. |
| `cancellation_reason` | `text` |  | No | Lý do hủy. |
| `created_at` | `timestamp` |  | Yes | Thời điểm bản ghi được tạo. |
| `updated_at` | `timestamp` |  | Yes | Thời điểm bản ghi được cập nhật gần nhất. |

# 21. `HorseHealthLog`

**Ý nghĩa bảng:** Nhật ký kiểm tra sức khỏe Horse trong quá trình vận chuyển.

| Field | Type | Key | Required | Ý nghĩa |
|---|---|---|---|---|
| `horse_health_log_id` | `uuid` | PK | No | Khóa chính định danh duy nhất bản ghi `HorseHealthLog`. |
| `transport_id` | `uuid` | FK | Yes | Transport mà health log này phát sinh trong đó. |
| `horse_id` | `uuid` | FK | Yes | Horse được kiểm tra. |
| `employee_id` | `uuid` | FK | Yes | Employee thực hiện/ghi nhận lần kiểm tra sức khỏe. |
| `log_type` | `horse_health_log_type` |  | Yes | Loại health check/log theo thời điểm nghiệp vụ. Enum `horse_health_log_type`: PRE_TRIP_CHECK, ROUTINE_CHECK, BORDER_CHECK, REST_STOP_CHECK, INCIDENT_FOLLOWUP, POST_TRIP_CHECK, OTHER. |
| `health_status` | `horse_health_status` |  | Yes | Đánh giá tổng quát sức khỏe Horse tại lần kiểm tra. Enum `horse_health_status`: NORMAL, OBSERVATION_REQUIRED, AT_RISK, CRITICAL. |
| `checked_at` | `timestamp` |  | Yes | Thời điểm thực hiện kiểm tra sức khỏe. |
| `temperature_celsius` | `decimal(4,1)` |  | No | Nhiệt độ cơ thể Horse, đơn vị °C. |
| `heart_rate_bpm` | `int` |  | No | Nhịp tim, số nhịp mỗi phút. |
| `respiratory_rate_bpm` | `int` |  | No | Nhịp thở, số nhịp mỗi phút. |
| `hydration_status` | `hydration_status` |  | No | Mức độ mất nước của Horse. Enum `hydration_status`: NORMAL, MILD_DEHYDRATION, MODERATE_DEHYDRATION, SEVERE_DEHYDRATION. |
| `behavior_note` | `text` |  | No | Ghi chú hành vi của Horse. |
| `symptom_note` | `text` |  | No | Ghi chú triệu chứng. |
| `injury_note` | `text` |  | No | Ghi chú chấn thương. |
| `feeding_note` | `text` |  | No | Ghi chú việc cho ăn. |
| `hydration_note` | `text` |  | No | Ghi chú bổ sung về tình trạng/cách bù nước. |
| `treatment_provided` | `text` |  | No | Biện pháp điều trị/chăm sóc đã thực hiện. |
| `medication_given` | `text` |  | No | Thuốc đã sử dụng. |
| `veterinarian_required` | `boolean` |  | Yes | Cho biết có cần bác sĩ thú y hay không. |
| `veterinarian_note` | `text` |  | No | Ghi chú của/liên quan tới bác sĩ thú y. |
| `location_description` | `varchar(500)` |  | No | Mô tả vị trí nơi thực hiện health check. |
| `photo_urls` | `varchar(500)[]` |  | Yes | Danh sách tối đa 5 URL ảnh bằng chứng/minh họa của lần kiểm tra; mặc định mảng rỗng, không có phần tử null. |
| `general_note` | `text` |  | No | Ghi chú chung. |
| `created_at` | `timestamp` |  | Yes | Thời điểm bản ghi được tạo. |
| `updated_at` | `timestamp` |  | Yes | Thời điểm bản ghi được cập nhật gần nhất. |

# 22. `IncidentReport`

**Cập nhật BE:** bổ sung `transport_id`, `emergency_settlement_required`; `incident_category` bổ sung `ROUTE_CHANGE`.

**Ý nghĩa bảng:** IncidentReport: Báo cáo sự cố phát sinh trong quá trình vận chuyển. category gồm VEHICLE, HORSE_HEALTH hoặc BOTH.

| Field | Type | Key | Required | Ý nghĩa |
|---|---|---|---|---|
| `incident_report_id` | `uuid` | PK | No | Khóa chính định danh duy nhất bản ghi `IncidentReport`. |
| `incident_code` | `varchar(50)` |  | Yes | Mã nghiệp vụ duy nhất của `IncidentReport`. |
| `employee_id` | `uuid` | FK | Yes | Employee báo cáo sự cố. |
| `location_id` | `uuid` | FK | Yes | Location nơi sự cố xảy ra/được ghi nhận. |
| `category` | `incident_category` |  | Yes | Phạm vi sự cố: VEHICLE, HORSE_HEALTH hoặc BOTH. Enum `incident_category`: VEHICLE, HORSE_HEALTH, BOTH. |
| `severity` | `incident_severity` |  | Yes | Mức độ nghiêm trọng của sự cố. Enum `incident_severity`: LOW, MEDIUM, HIGH, CRITICAL. |
| `status` | `incident_status` |  | Yes | Trạng thái hiện tại của bản ghi theo enum tương ứng của bảng. Enum `incident_status`: REPORTED, PENDING, RESOLVED, CANCELLED. |
| `title` | `varchar(200)` |  | Yes | Tiêu đề ngắn của bản ghi/nghiệp vụ. |
| `description` | `text` |  | Yes | Mô tả chi tiết. |
| `occurred_at` | `timestamp` |  | Yes | Thời điểm sự cố xảy ra. |
| `reported_at` | `timestamp` |  | Yes | Thời điểm sự cố được báo cáo. |
| `immediate_action` | `text` |  | No | Hành động xử lý tức thời sau khi phát hiện sự cố. |
| `resolution_summary` | `text` |  | No | Tóm tắt cách sự cố/claim được giải quyết. |
| `resolved_at` | `timestamp` |  | No | Thời điểm nghiệp vụ được xử lý xong. |
| `cancelled_at` | `timestamp` |  | No | Thời điểm nghiệp vụ/bản ghi bị hủy. |
| `cancellation_reason` | `text` |  | No | Lý do hủy. |
| `created_at` | `timestamp` |  | Yes | Thời điểm bản ghi được tạo. |
| `updated_at` | `timestamp` |  | Yes | Thời điểm bản ghi được cập nhật gần nhất. |

# 23. `VehicleIncidentDetail`

**Ý nghĩa bảng:** Chi tiết phần Vehicle của một IncidentReport.

| Field | Type | Key | Required | Ý nghĩa |
|---|---|---|---|---|
| `vehicle_incident_detail_id` | `uuid` | PK | No | Khóa chính định danh duy nhất bản ghi `VehicleIncidentDetail`. |
| `incident_report_id` | `uuid` | FK | Yes | IncidentReport mà chi tiết Vehicle này thuộc về; tối đa một VehicleIncidentDetail cho một IncidentReport. |
| `vehicle_id` | `uuid` | FK | Yes | Vehicle bị ảnh hưởng trong sự cố. |
| `incident_type` | `vehicle_incident_type` |  | Yes | Loại sự cố chi tiết của Vehicle hoặc Horse tùy bảng. Enum `vehicle_incident_type`: BREAKDOWN, ACCIDENT, TIRE_DAMAGE, ENGINE_FAILURE, TEMPERATURE_CONTROL_FAILURE, EQUIPMENT_FAILURE, OTHER. |
| `vehicle_condition` | `text` |  | No | Mô tả tình trạng phương tiện sau sự cố. |
| `damage_description` | `text` |  | No | Mô tả thiệt hại/hư hỏng phương tiện. |
| `vehicle_operable` | `boolean` |  | Yes | Cho biết phương tiện còn có thể vận hành hay không. |
| `repair_required` | `boolean` |  | Yes | Cho biết có cần sửa chữa phương tiện hay không. |
| `towing_required` | `boolean` |  | Yes | Cho biết có cần cứu hộ/kéo phương tiện hay không. |
| `replacement_vehicle_required` | `boolean` |  | Yes | Cho biết có cần phương tiện thay thế hay không. |
| `estimated_repair_cost` | `decimal(18,2)` |  | No | Chi phí sửa chữa ước tính. |
| `estimated_repair_hours` | `decimal(4,1)` |  | No | Số giờ sửa chữa ước tính. |
| `driver_safety_status` | `text` |  | No | Tình trạng an toàn của tài xế/phụ xe. |
| `cargo_condition_note` | `text` |  | No | Ghi chú tình trạng an toàn khoang chở ngựa. |
| `repair_note` | `text` |  | No | Ghi chú về việc sửa chữa. |
| `created_at` | `timestamp` |  | Yes | Thời điểm bản ghi được tạo. |
| `updated_at` | `timestamp` |  | Yes | Thời điểm bản ghi được cập nhật gần nhất. |

# 24. `HorseHealthIncidentDetail`

**Ý nghĩa bảng:** Chi tiết ảnh hưởng sức khỏe lên từng Horse trong một IncidentReport.

| Field | Type | Key | Required | Ý nghĩa |
|---|---|---|---|---|
| `horse_health_incident_detail_id` | `uuid` | PK | No | Khóa chính định danh duy nhất bản ghi `HorseHealthIncidentDetail`. |
| `incident_report_id` | `uuid` | FK | Yes | IncidentReport chứa sự cố sức khỏe này. |
| `horse_id` | `uuid` | FK | Yes | Horse bị ảnh hưởng. |
| `incident_type` | `horse_incident_type` |  | Yes | Loại sự cố chi tiết của Vehicle hoặc Horse tùy bảng. Enum `horse_incident_type`: INJURY, DEHYDRATION, RESPIRATORY_ISSUE, FEVER, STRESS, COLIC, FATIGUE, OTHER. |
| `condition_description` | `text` |  | Yes | Mô tả tình trạng Horse trong sự cố. |
| `symptoms` | `text` |  | No | Các triệu chứng quan sát được. |
| `body_temperature_celsius` | `decimal(4,1)` |  | No | Nhiệt độ cơ thể Horse tại sự cố, đơn vị °C. |
| `heart_rate_bpm` | `int` |  | No | Nhịp tim, số nhịp mỗi phút. |
| `respiratory_rate_bpm` | `int` |  | No | Nhịp thở, số nhịp mỗi phút. |
| `first_aid_provided` | `text` |  | No | Sơ cứu đã thực hiện. |
| `veterinarian_required` | `boolean` |  | Yes | Cho biết có cần bác sĩ thú y hay không. |
| `veterinarian_contacted_at` | `timestamp` |  | No | Thời điểm liên hệ bác sĩ thú y. |
| `veterinarian_instruction` | `text` |  | No | Chỉ dẫn của bác sĩ thú y. |
| `medication_given` | `text` |  | No | Thuốc đã sử dụng. |
| `horse_transportable` | `boolean` |  | No | Cho biết Horse còn đủ điều kiện tiếp tục vận chuyển hay không. |
| `isolation_required` | `boolean` |  | Yes | Có cần cách ly ngựa hay không. Mặc định `false`. |
| `outcome_note` | `text` |  | No | Ghi chú kết quả sau xử lý sự cố. |
| `created_at` | `timestamp` |  | Yes | Thời điểm bản ghi được tạo. |
| `updated_at` | `timestamp` |  | Yes | Thời điểm bản ghi được cập nhật gần nhất. |

# 25. `EmergencyCostRequest`

**Ý nghĩa bảng:** Yêu cầu phê duyệt chi phí khẩn cấp phát sinh từ Incident.

| Field | Type | Key | Required | Ý nghĩa |
|---|---|---|---|---|
| `emergency_cost_request_id` | `uuid` | PK | No | Khóa chính định danh duy nhất bản ghi `EmergencyCostRequest`. |
| `incident_report_id` | `uuid` | FK | Yes | IncidentReport làm phát sinh khoản chi phí khẩn cấp. |
| `requested_by_employee_id` | `uuid` | FK | Yes | Employee đã đề xuất yêu cầu chi phí; dữ liệu cũ được backfill từ `IncidentReport.employee_id`. |
| `request_code` | `varchar(50)` |  | Yes | Mã yêu cầu bổ sung/sửa giấy tờ. |
| `category` | `emergency_cost_category` |  | Yes | Nhóm chi phí khẩn cấp như veterinary, medication, repair, towing, route change... Enum `emergency_cost_category`: VETERINARY, MEDICATION, VEHICLE_REPAIR, TOWING, REPLACEMENT_VEHICLE, EMERGENCY_STABLE, ROUTE_CHANGE, ACCOMMODATION, OTHER. |
| `description` | `text` |  | Yes | Mô tả chi tiết. |
| `requested_amount` | `decimal(18,2)` |  | Yes | Số tiền được yêu cầu phê duyệt. |
| `currency` | `varchar(3)` |  | Yes | Mã tiền tệ, thường theo chuẩn 3 ký tự như USD/VND. |
| `status` | `emergency_cost_status` |  | Yes | Trạng thái hiện tại của bản ghi theo enum tương ứng của bảng. Enum `emergency_cost_status`: PENDING, APPROVED, REJECTED, CANCELLED. |
| `invoice_required` | `boolean` |  | Yes | Có cần hóa đơn VAT hay không. Mặc định `false`. |
| `receipt_status` | `emergency_receipt_status` |  | Yes | Trạng thái chứng từ chi phí. Enum: NOT_REQUIRED, PENDING, SUBMITTED, APPROVED, REJECTED. Mặc định `NOT_REQUIRED`. |
| `justification` | `text` |  | Yes | Giải trình/lý do phát sinh yêu cầu. |
| `requested_at` | `timestamp` |  | Yes | Thời điểm yêu cầu được tạo/gửi. |
| `approved_amount` | `decimal(18,2)` |  | No | Số tiền thực tế được phê duyệt. |
| `cancelled_at` | `timestamp` |  | No | Thời điểm nghiệp vụ/bản ghi bị hủy. |
| `cancellation_reason` | `text` |  | No | Lý do hủy. |
| `created_at` | `timestamp` |  | Yes | Thời điểm bản ghi được tạo. |
| `updated_at` | `timestamp` |  | Yes | Thời điểm bản ghi được cập nhật gần nhất. |

# 56. `IncidentPhoto`

**Ý nghĩa bảng:** Bảng ảnh dùng chung cho ảnh sự cố, ảnh chi tiết xe/ngựa và ảnh chứng từ chi phí khẩn cấp.

| Field | Type | Key | Required | Ý nghĩa |
|---|---|---|---|---|
| `incident_photo_id` | `uuid` | PK | No | Khóa chính của ảnh sự cố. |
| `incident_report_id` | `uuid` | FK | Yes | IncidentReport sở hữu ảnh. |
| `vehicle_incident_detail_id` | `uuid` | FK | No | Chi tiết sự cố xe liên quan; NULL nếu ảnh không thuộc nhóm xe. |
| `horse_health_incident_detail_id` | `uuid` | FK | No | Chi tiết sức khỏe ngựa liên quan; NULL nếu ảnh không thuộc nhóm ngựa. |
| `emergency_cost_request_id` | `uuid` | FK | No | Yêu cầu chi phí có ảnh biên lai/báo giá; NULL nếu ảnh không thuộc nhóm chi phí. |
| `photo_url` | `varchar(500)` |  | Yes | URL công khai của ảnh. |
| `caption` | `varchar(500)` |  | No | Chú thích ảnh. |
| `sort_order` | `int` |  | Yes | Thứ tự hiển thị, mặc định `0`. |
| `created_at` | `timestamp` |  | Yes | Thời điểm ảnh được tạo. |

**Quy tắc:** mỗi ảnh chỉ liên kết tối đa một nhóm chi tiết (`vehicle_incident_detail_id`, `horse_health_incident_detail_id` hoặc `emergency_cost_request_id`); cả ba có thể NULL nếu ảnh gắn chung cho IncidentReport.

# 26. `CostApprovalDecision`

**Ý nghĩa bảng:** Quyết định duyệt hoặc từ chối một EmergencyCostRequest.

| Field | Type | Key | Required | Ý nghĩa |
|---|---|---|---|---|
| `cost_approval_decision_id` | `uuid` | PK | No | Khóa chính định danh duy nhất bản ghi `CostApprovalDecision`. |
| `emergency_cost_request_id` | `uuid` | FK | Yes | EmergencyCostRequest được quyết định; một request có tối đa một decision theo model hiện tại. |
| `employee_id` | `uuid` | FK | Yes | Employee đưa ra quyết định. |
| `decision` | `approval_decision` |  | Yes | Kết quả quyết định duyệt hoặc từ chối. Enum `approval_decision`: APPROVED, REJECTED. |
| `approved_amount` | `decimal(18,2)` |  | No | Số tiền thực tế được phê duyệt. |
| `decision_reason` | `text` |  | No | Lý do của quyết định. |
| `decided_at` | `timestamp` |  | Yes | Thời điểm đưa ra quyết định. |
| `created_at` | `timestamp` |  | Yes | Thời điểm bản ghi được tạo. |

# 27. `Invoice`

**Ý nghĩa bảng:** Hóa đơn phát sinh cho Booking.

| Field | Type | Key | Required | Ý nghĩa |
|---|---|---|---|---|
| `invoice_id` | `uuid` | PK | No | Khóa chính định danh duy nhất bản ghi `Invoice`. |
| `invoice_code` | `varchar(50)` |  | Yes | Mã nghiệp vụ duy nhất của `Invoice`. |
| `booking_id` | `uuid` | FK | Yes | Booking mà hóa đơn này được lập cho. |
| `employee_id` | `uuid` | FK | Yes | Employee phụ trách/lập hóa đơn. |
| `type` | `invoice_type` |  | Yes | Loại chứng từ tài chính: QUOTATION (báo giá), DEPOSIT (đặt cọc), FINAL (quyết toán cuối), SUPPLEMENTARY (phát sinh thêm) hoặc CREDIT_NOTE (điều chỉnh giảm/hoàn tiền). Mặc định là QUOTATION. Các loại hóa đơn của cùng một Booking được truy xuất qua `booking_id`. |
| `status` | `invoice_status` |  | Yes | Trạng thái hiện tại của bản ghi theo enum tương ứng của bảng. Enum `invoice_status`: DRAFT, ISSUED, PARTIALLY_PAID, PAID, OVERDUE, CANCELLED. |
| `currency` | `varchar(3)` |  | Yes | Mã tiền tệ, thường theo chuẩn 3 ký tự như USD/VND. |
| `subtotal` | `decimal(18,2)` |  | Yes | Tổng tiền trước giảm giá và thuế. |
| `discount_amount` | `decimal(18,2)` |  | Yes | Số tiền giảm giá. |
| `tax_amount` | `decimal(18,2)` |  | Yes | Số tiền thuế. |
| `total_amount` | `decimal(18,2)` |  | Yes | Tổng tiền cuối cùng của hóa đơn. |
| `issued_at` | `timestamp` |  | No | Thời điểm hóa đơn được phát hành. |
| `due_date` | `timestamp` |  | No | Thời điểm đến hạn thanh toán (UTC, có cả giờ). Job nền chạy mỗi giờ đánh dấu hóa đơn `OVERDUE` khi quá hạn. |
| `paid_at` | `timestamp` |  | No | Thời điểm ghi nhận thanh toán. |
| `cancelled_at` | `timestamp` |  | No | Thời điểm nghiệp vụ/bản ghi bị hủy. |
| `cancellation_reason` | `text` |  | No | Lý do hủy. |
| `billing_note` | `text` |  | No | Ghi chú liên quan đến hóa đơn/thanh toán. |
| `created_at` | `timestamp` |  | Yes | Thời điểm bản ghi được tạo. |
| `updated_at` | `timestamp` |  | Yes | Thời điểm bản ghi được cập nhật gần nhất. |

# 28. `InvoiceItem`

**Ý nghĩa bảng:** Một dòng phí/chi phí chi tiết trong Invoice.

| Field | Type | Key | Required | Ý nghĩa |
|---|---|---|---|---|
| `invoice_item_id` | `uuid` | PK | No | Khóa chính định danh duy nhất bản ghi `InvoiceItem`. |
| `invoice_id` | `uuid` | FK | Yes | Khóa ngoại tham chiếu `Invoice.invoice_id`. |
| `horse_id` | `uuid` | FK | No | Horse liên quan trực tiếp tới dòng phí; có thể NULL với phí chung. |
| `emergency_cost_request_id` | `uuid` | FK | No | EmergencyCostRequest được billing vào dòng này; NULL nếu không phải emergency cost. |
| `item_type` | `invoice_item_type` |  | Yes | Loại dòng phí của InvoiceItem. Enum `invoice_item_type`: TRANSPORT_FEE, HORSE_SERVICE_FEE, HANDLING_FEE, COMPLIANCE_FEE, EMERGENCY_COST, OTHER. |
| `description` | `varchar(500)` |  | Yes | Mô tả chi tiết. |
| `quantity` | `decimal(10,2)` |  | Yes | Số lượng tính phí của dòng InvoiceItem. |
| `unit_price` | `decimal(18,2)` |  | Yes | Đơn giá. |
| `line_amount` | `decimal(18,2)` |  | Yes | Thành tiền của dòng InvoiceItem. |
| `note` | `text` |  | No | Ghi chú bổ sung. |
| `created_at` | `timestamp` |  | Yes | Thời điểm bản ghi được tạo. |
| `updated_at` | `timestamp` |  | Yes | Thời điểm bản ghi được cập nhật gần nhất. |

# 29. `Receipt`

**Ý nghĩa bảng:** Một lần ghi nhận thanh toán cho Invoice.

| Field | Type | Key | Required | Ý nghĩa |
|---|---|---|---|---|
| `receipt_id` | `uuid` | PK | No | Khóa chính định danh duy nhất bản ghi `Receipt`. |
| `receipt_code` | `varchar(50)` |  | Yes | Mã nghiệp vụ duy nhất của `Receipt`. |
| `invoice_id` | `uuid` | FK | Yes | Khóa ngoại tham chiếu `Invoice.invoice_id`. |
| `amount` | `decimal(18,2)` |  | Yes | Số tiền của giao dịch/bản ghi. |
| `currency` | `varchar(3)` |  | Yes | Mã tiền tệ, thường theo chuẩn 3 ký tự như USD/VND. |
| `payment_method` | `payment_method` |  | Yes | Phương thức thanh toán. Enum `payment_method`: CASH, BANK_TRANSFER, OTHER. |
| `status` | `receipt_status` |  | Yes | Trạng thái hiện tại của bản ghi theo enum tương ứng của bảng. Enum `receipt_status`: PENDING, CONFIRMED, FAILED, REFUNDED. |
| `transaction_reference` | `varchar(150)` |  | No | Mã tham chiếu giao dịch từ ngân hàng/cổng thanh toán/hệ thống ngoài. |
| `paid_at` | `timestamp` |  | No | Thời điểm ghi nhận thanh toán. |
| `confirmed_at` | `timestamp` |  | No | Thời điểm giao dịch được xác nhận. |
| `note` | `text` |  | No | Ghi chú bổ sung. |
| `created_at` | `timestamp` |  | Yes | Thời điểm bản ghi được tạo. |
| `updated_at` | `timestamp` |  | Yes | Thời điểm bản ghi được cập nhật gần nhất. |

# 30. `InvoiceStatusHistory`

**Ý nghĩa bảng:** InvoiceStatusHistory: Lịch sử thay đổi trạng thái Invoice. type cho biết hành động nghiệp vụ gây ra thay đổi; changed_by_account_id có thể null nếu hệ thống tự động thực hiện, ví dụ MARK_OVERDUE.

**Lưu ý:** đây là business history/audit cho thay đổi trạng thái. Hệ thống không có centralized `AuditLog`; riêng compliance dossier có `AuditDossier` append-only.

| Field | Type | Key | Required | Ý nghĩa |
|---|---|---|---|---|
| `invoice_status_history_id` | `uuid` | PK | No | Khóa chính định danh duy nhất bản ghi `InvoiceStatusHistory`. |
| `invoice_id` | `uuid` | FK | Yes | Khóa ngoại tham chiếu `Invoice.invoice_id`. |
| `type` | `invoice_history_type` |  | Yes | Loại hành động gây thay đổi Invoice: CREATE, ISSUE, RECORD_PAYMENT, MARK_OVERDUE hoặc CANCEL. Enum `invoice_history_type`: CREATE, ISSUE, RECORD_PAYMENT, MARK_OVERDUE, CANCEL. |
| `previous_status` | `invoice_status` |  | No | Trạng thái trước khi hành động xảy ra. Enum `invoice_status`: DRAFT, ISSUED, PARTIALLY_PAID, PAID, OVERDUE, CANCELLED. |
| `new_status` | `invoice_status` |  | Yes | Trạng thái sau khi hành động xảy ra. Enum `invoice_status`: DRAFT, ISSUED, PARTIALLY_PAID, PAID, OVERDUE, CANCELLED. |
| `changed_by_account_id` | `uuid` | FK | No | Account thực hiện thay đổi; có thể NULL nếu hệ thống tự động thực hiện, ví dụ MARK_OVERDUE. |
| `reason` | `text` |  | No | Lý do của hành động/thay đổi. |
| `changed_at` | `timestamp` |  | Yes | Thời điểm thay đổi trạng thái. |
| `created_at` | `timestamp` |  | Yes | Thời điểm bản ghi được tạo. |

# 31. `FinancialReport`

**Ý nghĩa bảng:** FinancialReport: Báo cáo tài chính tổng hợp theo một Transport, được hệ thống sinh từ dữ liệu Invoice, InvoiceItem, Receipt và chi phí liên quan; không có trạng thái lưu nháp.

| Field | Type | Key | Required | Ý nghĩa |
|---|---|---|---|---|
| `financial_report_id` | `uuid` | PK | No | Khóa chính định danh duy nhất bản ghi `FinancialReport`. |
| `report_code` | `varchar(50)` |  | Yes | Mã báo cáo duy nhất. |
| `transport_id` | `uuid` | FK | Yes | Khóa ngoại tham chiếu `Transport.transport_id`. |
| `currency` | `varchar(3)` |  | Yes | Mã tiền tệ, thường theo chuẩn 3 ký tự như USD/VND. |
| `total_invoiced_amount` | `decimal(18,2)` |  | Yes | Tổng giá trị đã xuất hóa đơn cho Transport. |
| `total_paid_amount` | `decimal(18,2)` |  | Yes | Tổng số tiền đã thanh toán/xác nhận. |
| `total_emergency_cost` | `decimal(18,2)` |  | Yes | Tổng chi phí khẩn cấp liên quan đến Transport. |
| `outstanding_amount` | `decimal(18,2)` |  | Yes | Số tiền còn phải thu/chưa thanh toán. |
| `summary_note` | `text` |  | No | Ghi chú tổng hợp của báo cáo. |
| `generated_at` | `timestamp` |  | Yes | Thời điểm hệ thống sinh báo cáo. |
| `created_at` | `timestamp` |  | Yes | Thời điểm bản ghi được tạo. |
| `updated_at` | `timestamp` |  | Yes | Thời điểm bản ghi được cập nhật gần nhất. |

# 32. `Claim`

**Ý nghĩa bảng:** Claim: Khiếu nại do Customer gửi liên quan đến một lần bàn giao. Hệ thống chỉ lưu khiếu nại và kết quả; việc trao đổi/thương lượng giữa Customer và Manager diễn ra ngoài hệ thống.

| Field | Type | Key | Required | Ý nghĩa |
|---|---|---|---|---|
| `claim_id` | `uuid` | PK | No | Khóa chính định danh duy nhất bản ghi `Claim`. |
| `claim_code` | `varchar(50)` |  | Yes | Mã nghiệp vụ duy nhất của `Claim`. |
| `customer_id` | `uuid` | FK | Yes | Khóa ngoại tham chiếu `Customer.customer_id`. |
| `handover_record_id` | `uuid` | FK | Yes | Lần bàn giao mà Claim liên quan tới. |
| `claim_type` | `claim_type` |  | Yes | Loại khiếu nại. Enum `claim_type`: HORSE_INJURY, HORSE_HEALTH, LOSS, DAMAGE, DELAY, DOCUMENT_ISSUE, SERVICE_ISSUE, OTHER. |
| `status` | `claim_status` |  | Yes | Trạng thái hiện tại của bản ghi theo enum tương ứng của bảng. Enum `claim_status`: PENDING, APPROVED, REJECTED, RESOLVED, CANCELLED. |
| `title` | `varchar(200)` |  | Yes | Tiêu đề ngắn của bản ghi/nghiệp vụ. |
| `description` | `text` |  | Yes | Mô tả chi tiết. |
| `requested_compensation_amount` | `decimal(18,2)` |  | No | Mức bồi thường Customer yêu cầu. |
| `currency` | `varchar(3)` |  | No | Mã tiền tệ, thường theo chuẩn 3 ký tự như USD/VND. |
| `evidence_note` | `text` |  | No | Ghi chú về bằng chứng hỗ trợ Claim. |
| `customer_note` | `text` |  | No | Ghi chú do Customer cung cấp. |
| `submitted_at` | `timestamp` |  | Yes | Thời điểm nghiệp vụ được gửi/submitted. |
| `reviewed_at` | `timestamp` |  | No | Thời điểm review. |
| `resolved_at` | `timestamp` |  | No | Thời điểm nghiệp vụ được xử lý xong. |
| `cancelled_at` | `timestamp` |  | No | Thời điểm nghiệp vụ/bản ghi bị hủy. |
| `cancellation_reason` | `text` |  | No | Lý do hủy. |
| `created_at` | `timestamp` |  | Yes | Thời điểm bản ghi được tạo. |
| `updated_at` | `timestamp` |  | Yes | Thời điểm bản ghi được cập nhật gần nhất. |

# 33. `ClaimResolution`

**Ý nghĩa bảng:** ClaimResolution: Kết quả xử lý/thỏa thuận cuối cùng của một Claim, ví dụ mức bồi thường thực tế sau khi hai bên trao đổi trực tiếp.

| Field | Type | Key | Required | Ý nghĩa |
|---|---|---|---|---|
| `claim_resolution_id` | `uuid` | PK | No | Khóa chính định danh duy nhất bản ghi `ClaimResolution`. |
| `claim_id` | `uuid` | FK | Yes | Claim được giải quyết; mỗi Claim có tối đa một ClaimResolution theo model hiện tại. |
| `resolution_type` | `claim_resolution_type` |  | Yes | Hình thức giải quyết Claim. Enum `claim_resolution_type`: COMPENSATION, PARTIAL_COMPENSATION, REFUND, SERVICE_RECOVERY, NO_COMPENSATION, OTHER. |
| `resolution_summary` | `text` |  | Yes | Tóm tắt cách sự cố/claim được giải quyết. |
| `approved_compensation_amount` | `decimal(18,2)` |  | No | Mức bồi thường cuối cùng được chấp thuận/thỏa thuận. |
| `currency` | `varchar(3)` |  | No | Mã tiền tệ, thường theo chuẩn 3 ký tự như USD/VND. |
| `corrective_action` | `text` |  | No | Biện pháp khắc phục được thống nhất. |
| `internal_note` | `text` |  | No | Ghi chú nội bộ. |
| `resolved_at` | `timestamp` |  | Yes | Thời điểm nghiệp vụ được xử lý xong. |
| `created_at` | `timestamp` |  | Yes | Thời điểm bản ghi được tạo. |
| `updated_at` | `timestamp` |  | Yes | Thời điểm bản ghi được cập nhật gần nhất. |

# 34. `ComplianceRequirement`

**Ý nghĩa bảng:** ComplianceRequirement: Danh mục master các loại yêu cầu pháp lý hoặc loại giấy tờ mà hệ thống biết có thể cần trong hồ sơ.

**Soft delete:** Bảng này thuộc scope soft delete của V6. Record không bị xóa vật lý khi user thực hiện thao tác xóa.

| Field | Type | Key | Required | Ý nghĩa |
|---|---|---|---|---|
| `compliance_requirement_id` | `uuid` | PK | No | Khóa chính định danh duy nhất bản ghi `ComplianceRequirement`. |
| `requirement_code` | `varchar(50)` |  | Yes | Mã master của loại yêu cầu pháp lý. |
| `requirement_name` | `varchar(200)` |  | Yes | Tên loại yêu cầu pháp lý. |
| `description` | `text` |  | No | Mô tả chi tiết. |
| `country` | `varchar(100)` |  | No | Quốc gia/phạm vi quốc gia áp dụng requirement; có thể lưu mã ISO hoặc tên quốc gia theo quy ước backend. |
| `category` | `compliance_requirement_category` |  | Yes | Nhóm requirement: QUARANTINE, CUSTOMS, TRANSPORT, HORSE_HEALTH, HORSE_IDENTITY hoặc OTHER. |
| `issuing_authority` | `varchar(200)` |  | No | Cơ quan ban hành/cấp giấy tờ hoặc requirement. |
| `bcp_code` | `varchar(50)` |  | No | Mã BCP liên quan đến requirement kiểm dịch. |
| `legal_basis` | `text` |  | No | Căn cứ pháp lý của requirement. |
| `validity_required` | `boolean` |  | Yes | Cho biết loại giấy tờ/yêu cầu này có cần kiểm tra thời hạn hiệu lực hay không. |
| `validity_period_days` | `int` |  | No | Số ngày hiệu lực mặc định khi requirement được áp dụng vào một dossier; NULL nếu không có quy định số ngày cố định. |
| `status` | `compliance_requirement_status` |  | Yes | Trạng thái hiện tại của bản ghi theo enum tương ứng của bảng. Enum `compliance_requirement_status`: ACTIVE, INACTIVE. |
| `is_deleted` | `boolean` |  | Yes | Cờ soft delete. `false` = record đang hoạt động/tồn tại về mặt logic; `true` = đã bị xóa logic nhưng vẫn giữ trong database để bảo toàn lịch sử và các FK. |
| `deleted_at` | `timestamp` |  | No | Thời điểm record bị soft delete; NULL nếu record chưa bị xóa logic. |
| `deleted_by_account_id` | `uuid` | FK | No | Account thực hiện thao tác soft delete; NULL nếu record chưa bị xóa hoặc việc xóa không có actor được ghi nhận. |
| `created_at` | `timestamp` |  | Yes | Thời điểm bản ghi được tạo. |
| `updated_at` | `timestamp` |  | Yes | Thời điểm bản ghi được cập nhật gần nhất. |

# 35. `DossierTemplate`

**Ý nghĩa bảng:** DossierTemplate: Bộ hồ sơ pháp lý mẫu cho một kịch bản vận chuyển. Template xác định thông thường cần những loại yêu cầu nào.

**Soft delete:** Bảng này thuộc scope soft delete của V6. Record không bị xóa vật lý khi user thực hiện thao tác xóa.

| Field | Type | Key | Required | Ý nghĩa |
|---|---|---|---|---|
| `dossier_template_id` | `uuid` | PK | No | Khóa chính định danh duy nhất bản ghi `DossierTemplate`. |
| `template_code` | `varchar(50)` |  | Yes | Mã bộ hồ sơ mẫu. |
| `template_name` | `varchar(200)` |  | Yes | Tên bộ hồ sơ mẫu. |
| `description` | `text` |  | No | Mô tả chi tiết. |
| `version_no` | `int` |  | Yes | Số phiên bản. |
| `status` | `dossier_template_status` |  | Yes | Trạng thái hiện tại của bản ghi theo enum tương ứng của bảng. Enum `dossier_template_status`: DRAFT, ACTIVE, INACTIVE. |
| `effective_from` | `date` |  | No | Ngày bắt đầu hiệu lực. |
| `effective_to` | `date` |  | No | Ngày kết thúc hiệu lực. |
| `is_deleted` | `boolean` |  | Yes | Cờ soft delete. `false` = record đang hoạt động/tồn tại về mặt logic; `true` = đã bị xóa logic nhưng vẫn giữ trong database để bảo toàn lịch sử và các FK. |
| `deleted_at` | `timestamp` |  | No | Thời điểm record bị soft delete; NULL nếu record chưa bị xóa logic. |
| `deleted_by_account_id` | `uuid` | FK | No | Account thực hiện thao tác soft delete; NULL nếu record chưa bị xóa hoặc việc xóa không có actor được ghi nhận. |
| `created_at` | `timestamp` |  | Yes | Thời điểm bản ghi được tạo. |
| `updated_at` | `timestamp` |  | Yes | Thời điểm bản ghi được cập nhật gần nhất. |

# 36. `DossierTemplateItem`

**Ý nghĩa bảng:** DossierTemplateItem: Một dòng yêu cầu nằm trong DossierTemplate, dùng để liên kết Template với ComplianceRequirement và đánh dấu yêu cầu bắt buộc hay không.

| Field | Type | Key | Required | Ý nghĩa |
|---|---|---|---|---|
| `dossier_template_item_id` | `uuid` | PK | No | Khóa chính định danh duy nhất bản ghi `DossierTemplateItem`. |
| `dossier_template_id` | `uuid` | FK | Yes | DossierTemplate chứa requirement này. |
| `compliance_requirement_id` | `uuid` | FK | Yes | Loại ComplianceRequirement được đưa vào template. |
| `sequence_no` | `int` |  | Yes | Thứ tự requirement trong template. |
| `is_mandatory` | `boolean` |  | Yes | Cho biết requirement có bắt buộc hay không. |
| `applies_per_horse` | `boolean` |  | Yes | Cho biết khi Apply Template có tạo một `DossierRequirement` riêng cho từng Horse hay chỉ tạo một requirement chung; mặc định `false`. |
| `instruction` | `text` |  | No | Hướng dẫn chi tiết cách đáp ứng requirement. |
| `created_at` | `timestamp` |  | Yes | Thời điểm bản ghi được tạo. |
| `updated_at` | `timestamp` |  | Yes | Thời điểm bản ghi được cập nhật gần nhất. |

# 37. `ComplianceDossier`

**Cập nhật BE:** bổ sung `dossier_template_id uuid` nullable, liên kết tới `DossierTemplate`; **V9:** thêm `dossier_type`; `transport_id` không còn unique riêng mà unique theo cặp `(transport_id, dossier_type)`, nên một `Transport` có tối đa một hồ sơ `MAIN` và một hồ sơ `EXTRA`.

**Ý nghĩa bảng:** ComplianceDossier: Bộ hồ sơ pháp lý do Transport Specialist lập và chịu trách nhiệm hoàn thiện trước khi Transport xuất phát.

**Context:** đây là **bộ hồ sơ chuẩn bị trước chuyến của một Transport**, không phải template và không phải trạng thái tổng của toàn chuyến. Kết quả phát sinh tại cửa khẩu nằm ở `QuarantineProof` và `CustomsClearanceProof`.

| Field | Type | Key | Required | Ý nghĩa |
|---|---|---|---|---|
| `compliance_dossier_id` | `uuid` | PK | No | Khóa chính định danh duy nhất bản ghi `ComplianceDossier`. |
| `dossier_code` | `varchar(50)` |  | Yes | Mã bộ hồ sơ pháp lý thực tế. |
| `transport_id` | `uuid` | FK | Yes | Transport sở hữu bộ hồ sơ pháp lý thực tế này; quan hệ 1-N (tối đa một hồ sơ cho mỗi `dossier_type`). |
| `dossier_type` | `dossier_type` |  | Yes | Loại hồ sơ (V9). Enum `dossier_type`: MAIN, EXTRA. `MAIN` là hồ sơ của chuyến; `EXTRA` là hồ sơ bổ sung mở khi LM duyệt RoutePlan RETURN (BR-048). Mặc định MAIN. |
| `dossier_template_id` | `uuid` | FK | No | Template dùng làm cơ sở sinh các yêu cầu pháp lý cho dossier; có thể NULL. |
| `status` | `compliance_dossier_status` |  | Yes | Trạng thái vòng đời bộ hồ sơ trước chuyến/AuthoritySubmission. Enum: DRAFT, IN_PREPARATION, READY_FOR_SUBMISSION, SUBMITTED, PENDING, APPROVED, REJECTED, CLOSED. Không cập nhật field này từ kết quả BCP/Hải quan. |
| `opened_at` | `timestamp` |  | Yes | Thời điểm bộ hồ sơ thực tế được mở/tạo để xử lý. |
| `ready_for_submission_at` | `timestamp` |  | No | Thời điểm dossier đạt trạng thái sẵn sàng gửi cơ quan. |
| `approved_at` | `timestamp` |  | No | Thời điểm được phê duyệt. |
| `rejected_at` | `timestamp` |  | No | Thời điểm bị từ chối. |
| `closed_at` | `timestamp` |  | No | Thời điểm dossier được đóng. |
| `rejection_reason` | `text` |  | No | Lý do bị từ chối. |
| `note` | `text` |  | No | Ghi chú bổ sung. |
| `created_at` | `timestamp` |  | Yes | Thời điểm bản ghi được tạo. |
| `updated_at` | `timestamp` |  | Yes | Thời điểm bản ghi được cập nhật gần nhất. |

# 38. `DossierRequirement`

**Ý nghĩa bảng:** DossierRequirement: Một yêu cầu pháp lý thực tế đã được sinh ra trong ComplianceDossier của chuyến đi cụ thể, dựa trên loại ComplianceRequirement.

| Field | Type | Key | Required | Ý nghĩa |
|---|---|---|---|---|
| `dossier_requirement_id` | `uuid` | PK | No | Khóa chính định danh duy nhất bản ghi `DossierRequirement`. |
| `compliance_dossier_id` | `uuid` | FK | Yes | ComplianceDossier thực tế chứa requirement này. |
| `compliance_requirement_id` | `uuid` | FK | Yes | Loại requirement master mà instance thực tế này dựa trên. |
| `horse_id` | `uuid` | FK | No | Horse cụ thể mà requirement áp dụng. NULL nghĩa là requirement chung cho toàn chuyến; có giá trị nghĩa là requirement riêng cho Horse đó. |
| `status` | `dossier_requirement_status` |  | Yes | Trạng thái hiện tại của bản ghi theo enum tương ứng của bảng. Enum `dossier_requirement_status`: PENDING, REQUESTED, PROVIDED, SATISFIED, REJECTED, WAIVED. |
| `is_mandatory` | `boolean` |  | Yes | Cho biết requirement có bắt buộc hay không. |
| `is_deleted` | `boolean` |  | Yes | Cờ soft delete cho Optional DossierRequirement; requirement mandatory không được xóa logic. |
| `deleted_at` | `timestamp` |  | No | Thời điểm requirement bị soft delete. |
| `deleted_by_account_id` | `uuid` | FK | No | Account thực hiện soft delete requirement. |
| `due_date` | `date` |  | No | Ngày đến hạn thanh toán hoặc hoàn thành yêu cầu, tùy bảng. |
| `valid_until` | `date` |  | No | Ngày hết hiệu lực của requirement instance, được backend tính khi áp dụng từ `ComplianceRequirement.validity_period_days`. |
| `instruction` | `text` |  | No | Hướng dẫn chi tiết cách đáp ứng requirement. |
| `satisfied_at` | `timestamp` |  | No | Thời điểm requirement được xác nhận đã đáp ứng. |
| `waived_reason` | `text` |  | No | Lý do requirement được miễn/waive. |
| `note` | `text` |  | No | Ghi chú bổ sung. |
| `created_at` | `timestamp` |  | Yes | Thời điểm bản ghi được tạo. |
| `updated_at` | `timestamp` |  | Yes | Thời điểm bản ghi được cập nhật gần nhất. |

# 39. `ComplianceDocument`

**Ý nghĩa bảng:** ComplianceDocument: Giấy tờ thực tế được cung cấp để đáp ứng một DossierRequirement. Đây là document logic, không phải từng file upload.

**Context:** đây là **document logic**; file upload cụ thể nằm ở `DocumentVersion`.

| Field | Type | Key | Required | Ý nghĩa |
|---|---|---|---|---|
| `compliance_document_id` | `uuid` | PK | No | Khóa chính định danh duy nhất bản ghi `ComplianceDocument`. |
| `dossier_requirement_id` | `uuid` | FK | Yes | DossierRequirement mà giấy tờ này dùng để đáp ứng; hiện model 1-0..1. |
| `document_code` | `varchar(50)` |  | Yes | Mã document logic. |
| `document_name` | `varchar(200)` |  | Yes | Tên giấy tờ thực tế. |
| `status` | `compliance_document_status` |  | Yes | Trạng thái hiện tại của bản ghi theo enum tương ứng của bảng. Enum `compliance_document_status`: PROVIDED, PENDING, APPROVED, REJECTED, EXPIRED. |
| `document_number` | `varchar(150)` |  | No | Số hiệu/số văn bản của giấy tờ. |
| `issuing_authority` | `varchar(200)` |  | No | Cơ quan ban hành/cấp giấy tờ hoặc requirement. |
| `issued_date` | `date` |  | No | Ngày giấy tờ được cấp. |
| `expiry_date` | `date` |  | No | Ngày hết hạn của giấy tờ. |
| `note` | `text` |  | No | Ghi chú bổ sung. |
| `created_by_account_id` | `uuid` | FK | Yes | Account tạo ComplianceDocument; dùng để lưu actor khi thao tác thay Customer. |
| `updated_by_account_id` | `uuid` | FK | Yes | Account cập nhật ComplianceDocument gần nhất. |
| `created_at` | `timestamp` |  | Yes | Thời điểm bản ghi được tạo. |
| `updated_at` | `timestamp` |  | Yes | Thời điểm bản ghi được cập nhật gần nhất. |

# 40. `DocumentVersion`

**Ý nghĩa bảng:** DocumentVersion: Một phiên bản file cụ thể của ComplianceDocument. Khi giấy tờ bị sửa hoặc upload lại thì tạo version mới, không tạo ComplianceDocument mới.

| Field | Type | Key | Required | Ý nghĩa |
|---|---|---|---|---|
| `document_version_id` | `uuid` | PK | No | Khóa chính định danh duy nhất bản ghi `DocumentVersion`. |
| `compliance_document_id` | `uuid` | FK | Yes | ComplianceDocument logic mà file/version này thuộc về. |
| `version_no` | `int` |  | Yes | Số phiên bản. |
| `file_name` | `varchar(255)` |  | Yes | Tên file đã upload. |
| `file_url` | `varchar(1000)` |  | Yes | URL/vị trí lưu file. |
| `mime_type` | `varchar(100)` |  | No | MIME type của file, ví dụ application/pdf. |
| `file_size_bytes` | `bigint` |  | No | Kích thước file tính bằng byte. |
| `upload_note` | `text` |  | No | Ghi chú khi upload phiên bản file. |
| `uploaded_by_account_id` | `uuid` | FK | Yes | Account thực hiện upload version; có thể là Customer hoặc Transport Specialist. |
| `uploaded_at` | `timestamp` |  | Yes | Thời điểm upload file. |
| `created_at` | `timestamp` |  | Yes | Thời điểm bản ghi được tạo. |

# 41. `DocumentAttachment`

**Ý nghĩa bảng:** DocumentAttachment: Một file vật lý cụ thể thuộc một `DocumentVersion`. Một version có thể có nhiều attachment, ví dụ nhiều ảnh của các trang hộ chiếu ngựa.

| Field | Type | Key | Required | Ý nghĩa |
|---|---|---|---|---|
| `document_attachment_id` | `uuid` | PK | No | Khóa chính định danh duy nhất file attachment. |
| `document_version_id` | `uuid` | FK | Yes | `DocumentVersion` mà file này thuộc về. |
| `file_name` | `varchar(255)` |  | Yes | Tên file vật lý. |
| `file_url` | `varchar(1000)` |  | Yes | URL/vị trí lưu file. |
| `mime_type` | `varchar(100)` |  | No | MIME type của file. |
| `file_size_bytes` | `bigint` |  | No | Kích thước file tính bằng byte. |
| `sort_order` | `int` |  | Yes | Thứ tự hiển thị/xử lý file trong cùng một `DocumentVersion`; unique theo `document_version_id`. |
| `created_at` | `timestamp` |  | Yes | Thời điểm attachment được tạo. |

# 42. `DocumentReview`

**Ý nghĩa bảng:** DocumentReview: Kết quả kiểm tra của một DocumentVersion cụ thể, ví dụ APPROVED, REJECTED hoặc REVISION_REQUIRED.

| Field | Type | Key | Required | Ý nghĩa |
|---|---|---|---|---|
| `document_review_id` | `uuid` | PK | No | Khóa chính định danh duy nhất bản ghi `DocumentReview`. |
| `document_version_id` | `uuid` | FK | Yes | DocumentVersion được kiểm tra. |
| `result` | `document_review_result` |  | Yes | Kết quả review của DocumentVersion. Enum `document_review_result`: APPROVED, REJECTED, REVISION_REQUIRED, REVOKED. |
| `review_comment` | `text` |  | No | Nhận xét của lần review. |
| `reviewed_by_account_id` | `uuid` | FK | Yes | Account thực hiện review; theo contract là Transport Specialist. |
| `reviewed_at` | `timestamp` |  | Yes | Thời điểm review. |
| `created_at` | `timestamp` |  | Yes | Thời điểm bản ghi được tạo. |

# 43. `DocumentRequest`

**Ý nghĩa bảng:** DocumentRequest: Yêu cầu gửi tới Customer để cung cấp, sửa, upload lại hoặc bổ sung thông tin cho một DossierRequirement.

| Field | Type | Key | Required | Ý nghĩa |
|---|---|---|---|---|
| `document_request_id` | `uuid` | PK | No | Khóa chính định danh duy nhất bản ghi `DocumentRequest`. |
| `document_review_id` | `uuid` | FK, UQ | No | DocumentReview tạo ra yêu cầu này; một review chỉ gắn tối đa một DocumentRequest. |
| `dossier_requirement_id` | `uuid` | FK | Yes | DossierRequirement cần Customer bổ sung/sửa giấy tờ. |
| `error_category` | `varchar(40)` |  | No | Nhóm lỗi của giấy tờ bị từ chối hoặc cần chỉnh sửa. |
| `request_code` | `varchar(50)` |  | Yes | Mã yêu cầu bổ sung/sửa giấy tờ. |
| `request_type` | `document_request_type` |  | Yes | Loại yêu cầu gửi Customer. Enum `document_request_type`: MISSING_DOCUMENT, REVISION, REUPLOAD, ADDITIONAL_INFORMATION. |
| `status` | `document_request_status` |  | Yes | Trạng thái hiện tại của bản ghi theo enum tương ứng của bảng. Enum `document_request_status`: OPEN, FULFILLED, CANCELLED. |
| `message` | `text` |  | Yes | Nội dung thông báo/yêu cầu gửi tới người nhận. |
| `requested_at` | `timestamp` |  | Yes | Thời điểm yêu cầu được tạo/gửi. |
| `due_date` | `date` |  | No | Ngày đến hạn thanh toán hoặc hoàn thành yêu cầu, tùy bảng. |
| `fulfilled_at` | `timestamp` |  | No | Thời điểm DocumentRequest được đáp ứng. |
| `cancelled_at` | `timestamp` |  | No | Thời điểm nghiệp vụ/bản ghi bị hủy. |
| `cancellation_reason` | `text` |  | No | Lý do hủy. |
| `requested_by_account_id` | `uuid` | FK | Yes | Account tạo DocumentRequest; theo contract là Transport Specialist. |
| `created_at` | `timestamp` |  | Yes | Thời điểm bản ghi được tạo. |
| `updated_at` | `timestamp` |  | Yes | Thời điểm bản ghi được cập nhật gần nhất. |

# 44. `AuthoritySubmission`

**Ý nghĩa bảng:** AuthoritySubmission: Một lần thực tế gửi ComplianceDossier tới cơ quan có thẩm quyền. Một dossier có thể được gửi nhiều lần nếu bị trả về hoặc từ chối.

| Field | Type | Key | Required | Ý nghĩa |
|---|---|---|---|---|
| `authority_submission_id` | `uuid` | PK | No | Khóa chính định danh duy nhất bản ghi `AuthoritySubmission`. |
| `compliance_dossier_id` | `uuid` | FK | Yes | ComplianceDossier được gửi trong lần submission này. |
| `submission_no` | `int` |  | Yes | Số thứ tự lần gửi dossier tới cơ quan. |
| `authority_name` | `varchar(200)` |  | Yes | Tên cơ quan có thẩm quyền nhận hồ sơ. |
| `status` | `authority_submission_status` |  | Yes | Trạng thái hiện tại của bản ghi theo enum tương ứng của bảng. Enum `authority_submission_status`: DRAFT, SUBMITTED, PENDING, APPROVED, REJECTED, RETURNED. |
| `submission_reference` | `varchar(150)` |  | No | Mã tham chiếu của lần nộp/gửi hồ sơ. |
| `submission_method` | `varchar(100)` |  | No | Phương thức nộp hồ sơ, ví dụ trực tiếp/portal/email tùy nghiệp vụ. |
| `submitted_by_account_id` | `uuid` | FK | Yes | Account ghi nhận lần nộp hồ sơ. |
| `is_required` | `boolean` |  | Yes | Cho biết authority submission có bắt buộc để ComplianceDossier được APPROVED hay không; mặc định `true`. |
| `permit_number` | `varchar(150)` |  | No | Số giấy phép/quyết định của cơ quan; bắt buộc khi decision là APPROVED. |
| `permit_issued_date` | `date` |  | No | Ngày cấp giấy phép/quyết định. |
| `permit_expiry_date` | `date` |  | No | Ngày hết hạn giấy phép/quyết định. |
| `revision_due_date` | `date` |  | No | Hạn phải bổ sung/sửa hồ sơ khi authority trả về yêu cầu chỉnh sửa. |
| `submitted_at` | `timestamp` |  | No | Thời điểm nghiệp vụ được gửi/submitted. |
| `response_received_at` | `timestamp` |  | No | Thời điểm nhận phản hồi từ cơ quan. |
| `decision_note` | `text` |  | No | Ghi chú/kết quả phản hồi của cơ quan. |
| `rejection_reason` | `text` |  | No | Lý do bị từ chối. |
| `created_at` | `timestamp` |  | Yes | Thời điểm bản ghi được tạo. |
| `updated_at` | `timestamp` |  | Yes | Thời điểm bản ghi được cập nhật gần nhất. |

# 45. `AuthoritySubmissionEvidenceFile`

**Ý nghĩa bảng:** AuthoritySubmissionEvidenceFile: File vật lý là bằng chứng phản hồi của cơ quan chức năng cho một lần `AuthoritySubmission`, ví dụ văn bản chấp nhận, từ chối hoặc yêu cầu bổ sung.

| Field | Type | Key | Required | Ý nghĩa |
|---|---|---|---|---|
| `authority_submission_evidence_file_id` | `uuid` | PK | No | Khóa chính định danh duy nhất file bằng chứng. |
| `authority_submission_id` | `uuid` | FK | Yes | Lần `AuthoritySubmission` mà file phản hồi thuộc về. |
| `evidence_type` | `authority_evidence_type` |  | Yes | Loại văn bản: ACCEPTANCE, REJECTION, ADDITIONAL_INFORMATION hoặc OTHER. |
| `uploaded_by_employee_id` | `uuid` | FK | Yes | Employee upload/lưu văn bản phản hồi vào hệ thống. |
| `file_name` | `varchar(255)` |  | Yes | Tên file vật lý. |
| `file_url` | `varchar(1000)` |  | Yes | URL/vị trí lưu file. |
| `mime_type` | `varchar(100)` |  | No | MIME type của file. |
| `file_size_bytes` | `bigint` |  | No | Kích thước file tính bằng byte. |
| `note` | `text` |  | No | Ghi chú về file hoặc phản hồi của cơ quan. |
| `created_at` | `timestamp` |  | Yes | Thời điểm file được ghi nhận. |

# 46. `QuarantineProof`

**Ý nghĩa bảng:** QuarantineProof: Bằng chứng và kết quả kiểm dịch tại trạm BCP do Driver hoặc Escort nộp và Transport Specialist xác minh. Đây là entity riêng cho UC-28a/UC-28b; mọi kết quả đạt, tạm giữ, thất bại hoặc từ chối đều lưu tại đây và không ghi vào trạng thái CDos.

**Nhiều file:** Các file của proof được lưu ở bảng `ProofAttachment`, không lưu trực tiếp trong bảng này.

| Field | Type | Key | Required | Ý nghĩa |
|---|---|---|---|---|
| `quarantine_proof_id` | `uuid` | PK | No | Khóa chính định danh bằng chứng kiểm dịch. |
| `compliance_dossier_id` | `uuid` | FK | Yes | Dossier của chuyến đi mà bằng chứng kiểm dịch thuộc về. |
| `status` | `quarantine_proof_status` |  | Yes | Trạng thái: PENDING_VERIFICATION, PASSED, PASSED_WITH_CONDITIONS, HOLDBACK, FAILED hoặc REJECTED. |
| `submitted_by_employee_id` | `uuid` | FK | Yes | Driver/Escort nộp bằng chứng. Business logic phải kiểm tra role phù hợp. |
| `verified_by_employee_id` | `uuid` | FK | No | Transport Specialist xác minh/chốt bằng chứng. |
| `location_id` | `uuid` | FK | No | Location của trạm BCP hoặc nơi kiểm dịch. |
| `bcp_name` | `varchar(200)` |  | Yes | Tên trạm kiểm soát biên giới BCP. |
| `official_veterinarian_name` | `varchar(200)` |  | No | Tên bác sĩ thú y chính thức tại BCP. |
| `bcp_receipt_number` | `varchar(150)` |  | No | Số biên nhận/tờ khai kiểm dịch BCP. |
| `submitted_note` | `text` |  | No | Ghi chú nhanh do Driver/Escort gửi cùng proof, đặc biệt khi BCP holdback. |
| `microchip_checklist` | `jsonb` |  | No | Checklist đối chiếu microchip theo từng Horse trong dossier. |
| `inspection_at` | `timestamp` |  | No | Thời điểm kiểm tra thực tế tại BCP. |
| `submitted_at` | `timestamp` |  | Yes | Thời điểm Driver/Escort gửi proof. |
| `verified_at` | `timestamp` |  | No | Thời điểm TS xác minh/chốt proof. |
| `conditions` | `text` |  | No | Điều kiện di chuyển kèm theo nếu kết quả PASSED_WITH_CONDITIONS. |
| `verification_note` | `text` |  | No | Ghi chú của TS khi xác minh. |
| `rejection_reason` | `text` |  | No | Lý do proof bị từ chối. |
| `is_finalized` | `boolean` |  | Yes | Đã chốt bất biến hay chưa; mặc định false. |
| `finalized_at` | `timestamp` |  | No | Thời điểm proof được chốt bất biến. |
| `created_at` | `timestamp` |  | Yes | Thời điểm bản ghi được tạo. |
| `updated_at` | `timestamp` |  | Yes | Thời điểm bản ghi được cập nhật gần nhất. |

# 47. `CustomsClearanceProof`

**Ý nghĩa bảng:** CustomsClearanceProof: Bằng chứng và kết quả thông quan hải quan do Driver nộp và Transport Specialist xác minh. Đây là entity riêng cho UC-29a/UC-29b; kết quả thông quan, tạm giữ hoặc từ chối đều lưu tại đây và không ghi vào trạng thái CDos.

**Nhiều file:** Các file của proof được lưu ở bảng `ProofAttachment`, không lưu trực tiếp trong bảng này.

| Field | Type | Key | Required | Ý nghĩa |
|---|---|---|---|---|
| `customs_clearance_proof_id` | `uuid` | PK | No | Khóa chính định danh bằng chứng thông quan hải quan. |
| `compliance_dossier_id` | `uuid` | FK | Yes | Dossier của chuyến đi mà bằng chứng hải quan thuộc về. |
| `status` | `customs_clearance_proof_status` |  | Yes | Trạng thái: PENDING_VERIFICATION, CLEARED, HELD hoặc REJECTED. |
| `customs_lane` | `customs_lane` |  | No | Phân luồng hải quan: GREEN_LANE, ORANGE_LANE hoặc RED_LANE. |
| `submitted_by_employee_id` | `uuid` | FK | Yes | Driver nộp bằng chứng. |
| `verified_by_employee_id` | `uuid` | FK | No | Transport Specialist xác minh/chốt bằng chứng. |
| `location_id` | `uuid` | FK | No | Location của cửa khẩu/cảng hải quan. |
| `customs_port_name` | `varchar(200)` |  | Yes | Tên cửa khẩu/cảng hải quan. |
| `mrn_number` | `varchar(150)` |  | No | Mã MRN hoặc mã tham chiếu tờ khai hải quan. |
| `submitted_note` | `text` |  | No | Ghi chú nhanh do Driver gửi cùng proof, đặc biệt khi shipment bị giữ. |
| `ata_carnet_expiry_date` | `date` |  | No | Ngày hết hạn ATA Carnet cần đối chiếu trong quy trình thông quan. |
| `clearance_at` | `timestamp` |  | No | Thời điểm thông quan hoặc có kết quả kiểm tra. |
| `submitted_at` | `timestamp` |  | Yes | Thời điểm Driver gửi proof. |
| `verified_at` | `timestamp` |  | No | Thời điểm TS xác minh/chốt proof. |
| `verification_note` | `text` |  | No | Ghi chú của TS khi xác minh. |
| `rejection_reason` | `text` |  | No | Lý do proof bị từ chối. |
| `is_finalized` | `boolean` |  | Yes | Đã chốt bất biến hay chưa; mặc định false. |
| `finalized_at` | `timestamp` |  | No | Thời điểm proof được chốt bất biến. |
| `created_at` | `timestamp` |  | Yes | Thời điểm bản ghi được tạo. |
| `updated_at` | `timestamp` |  | Yes | Thời điểm bản ghi được cập nhật gần nhất. |

# 48. `ProofAttachment`

**Ý nghĩa bảng:** ProofAttachment: File vật lý thuộc một `QuarantineProof` hoặc `CustomsClearanceProof`. Một proof có thể có nhiều file.

**Ràng buộc:** Mỗi record phải tham chiếu đúng một trong hai FK `quarantine_proof_id` hoặc `customs_clearance_proof_id`; không được đồng thời có cả hai hoặc cùng để NULL. `sort_order` là duy nhất trong phạm vi từng proof.

| Field | Type | Key | Required | Ý nghĩa |
|---|---|---|---|---|
| `proof_attachment_id` | `uuid` | PK | No | Khóa chính định danh file proof. |
| `quarantine_proof_id` | `uuid` | FK | No | Proof kiểm dịch mà file thuộc về; chỉ được có giá trị khi `customs_clearance_proof_id` là NULL. |
| `customs_clearance_proof_id` | `uuid` | FK | No | Proof hải quan mà file thuộc về; chỉ được có giá trị khi `quarantine_proof_id` là NULL. |
| `file_name` | `varchar(255)` |  | Yes | Tên file vật lý. |
| `file_url` | `varchar(1000)` |  | Yes | URL/vị trí lưu file. |
| `mime_type` | `varchar(100)` |  | No | MIME type của file. |
| `file_size_bytes` | `bigint` |  | No | Kích thước file tính bằng byte. |
| `sort_order` | `int` |  | Yes | Thứ tự hiển thị/xử lý file trong cùng một proof. |
| `created_at` | `timestamp` |  | Yes | Thời điểm file được ghi nhận. |

# 49. `AuditDossier`

**Ý nghĩa bảng:** AuditDossier: Nhật ký append-only của `ComplianceDossier`. Bản ghi chỉ được thêm mới, không được sửa/xóa qua API.

**Polymorphic target:** `target_entity + target_id` trỏ tới entity bị tác động, ví dụ `ComplianceDossier`, `DossierRequirement`, `DocumentVersion`, `AuthoritySubmission`, `QuarantineProof` hoặc `CustomsClearanceProof`. Cặp này phải được backend validate.

| Field | Type | Key | Required | Ý nghĩa |
|---|---|---|---|---|
| `audit_dossier_id` | `uuid` | PK | No | Khóa chính định danh dòng audit. |
| `compliance_dossier_id` | `uuid` | FK | Yes | Dossier làm phạm vi của sự kiện audit. |
| `action_type` | `varchar(100)` |  | Yes | Loại hành động, ví dụ DOSSIER_INITIALIZED, DOCUMENT_VERSION_SUBMITTED, DOCUMENT_REVIEW_APPROVED, AUTHORITY_SUBMITTED, PROOF_VERIFIED. |
| `target_entity` | `varchar(100)` |  | Yes | Tên entity bị tác động. |
| `target_id` | `uuid` |  | No | ID của target entity; nullable nếu sự kiện không gắn một record cụ thể. |
| `actor_account_id` | `uuid` | FK | Yes | Account thực hiện hành động. |
| `summary` | `varchar(500)` |  | No | Tóm tắt ngắn của sự kiện. |
| `details` | `jsonb` |  | No | Chi tiết mở rộng trước/sau, metadata hoặc nội dung audit. |
| `visibility` | `audit_visibility` |  | Yes | PUBLIC hoặc INTERNAL; dùng để giới hạn nội dung Customer được xem. |
| `created_at` | `timestamp` |  | Yes | Thời điểm sự kiện được ghi nhận. |

# 50. `CatalogAuditLog`

**Ý nghĩa bảng:** CatalogAuditLog: Nhật ký append-only cho các thay đổi trên bảng catalog/master như `ComplianceRequirement`. Bảng này không thay thế `AuditDossier`, vì `AuditDossier` dùng cho các sự kiện trong phạm vi một `ComplianceDossier` cụ thể.

| Field | Type | Key | Required | Ý nghĩa |
|---|---|---|---|---|
| `catalog_audit_log_id` | `uuid` | PK | No | Khóa chính định danh dòng audit catalog. |
| `entity_type` | `varchar(100)` |  | Yes | Tên loại entity catalog bị thay đổi, ví dụ `ComplianceRequirement`. |
| `entity_id` | `uuid` |  | Yes | ID của entity catalog bị thay đổi. |
| `action_type` | `varchar(100)` |  | Yes | Loại hành động, ví dụ CREATE, UPDATE hoặc STATUS_CHANGED. |
| `actor_account_id` | `uuid` | FK | Yes | Account thực hiện thay đổi catalog. |
| `summary` | `varchar(500)` |  | No | Tóm tắt ngắn của thay đổi. |
| `before_data` | `jsonb` |  | Yes | Snapshot dữ liệu trước khi thay đổi. |
| `after_data` | `jsonb` |  | Yes | Snapshot dữ liệu sau khi thay đổi. |
| `reference_count_snapshot` | `jsonb` |  | No | Snapshot số lượng reference tại thời điểm thay đổi, nếu có. |
| `created_at` | `timestamp` |  | Yes | Thời điểm audit event được ghi nhận. |

# 51. `Notification`

**Ý nghĩa bảng:** Nội dung thông báo nghiệp vụ được tạo trong phạm vi một Transport.

| Field | Type | Key | Required | Ý nghĩa |
|---|---|---|---|---|
| `notification_id` | `uuid` | PK | No | Khóa chính định danh duy nhất bản ghi `Notification`. |
| `transport_id` | `uuid` | FK | Yes | Transport làm ngữ cảnh chính của Notification. |
| `notification_type` | `notification_type` |  | Yes | Nhóm nghiệp vụ phát sinh Notification. Enum `notification_type`: BOOKING, TRANSPORT, ROUTE, HANDOVER, HORSE_HEALTH, INCIDENT, EMERGENCY_COST, PAYMENT, CLAIM, COMPLIANCE, DOCUMENT, SYSTEM. |
| `priority` | `notification_priority` |  | Yes | Mức độ ưu tiên của Notification. Enum `notification_priority`: LOW, NORMAL, HIGH, URGENT. |
| `title` | `varchar(200)` |  | Yes | Tiêu đề ngắn của bản ghi/nghiệp vụ. |
| `message` | `text` |  | Yes | Nội dung thông báo/yêu cầu gửi tới người nhận. |
| `reference_type` | `varchar(100)` |  | No | Tên loại entity nghiệp vụ mà Notification đang tham chiếu. |
| `reference_id` | `uuid` |  | No | ID của entity được tham chiếu bởi Notification; được kiểm tra bằng business logic. |
| `action_url` | `varchar(1000)` |  | No | URL/deep-link đưa người dùng tới màn hình liên quan. |
| `created_at` | `timestamp` |  | Yes | Thời điểm bản ghi được tạo. |
| `expires_at` | `timestamp` |  | No | Thời điểm token/notification hết hạn tùy bảng. |

# 52. `AccountNotification`

**Ý nghĩa bảng:** Bảng N-N giữa Account và Notification, đồng thời lưu trạng thái nhận/đọc theo từng Account.

| Field | Type | Key | Required | Ý nghĩa |
|---|---|---|---|---|
| `account_id` | `uuid` | FK | Yes | Account nhận Notification. |
| `notification_id` | `uuid` | FK | Yes | Notification được gửi tới Account. |
| `status` | `recipient_status` |  | Yes | Trạng thái hiện tại của bản ghi theo enum tương ứng của bảng. Enum `recipient_status`: PENDING, DELIVERED, READ, FAILED. |
| `delivered_at` | `timestamp` |  | No | Thời điểm Notification được giao tới Account. |
| `read_at` | `timestamp` |  | No | Thời điểm Account đọc Notification. |
| `created_at` | `timestamp` |  | Yes | Thời điểm bản ghi được tạo. |

# 53. `DocumentRequestComment`

**Ý nghĩa bảng:** Lưu trao đổi giữa Customer và Transport Specialist trong phạm vi một `DocumentRequest`.

| Field | Type | Key | Required | Ý nghĩa |
|---|---|---|---|---|
| `document_request_comment_id` | `uuid` | PK | No | Khóa chính của bình luận. |
| `document_request_id` | `uuid` | FK | Yes | DocumentRequest mà bình luận thuộc về. |
| `author_account_id` | `uuid` | FK | Yes | Account tạo bình luận. |
| `message` | `varchar(2000)` |  | Yes | Nội dung trao đổi. |
| `created_at` | `timestamp` |  | Yes | Thời điểm tạo bình luận. |

# 54. `MissingDocumentReminder`

**Ý nghĩa bảng:** Hàng đợi durable cho email nhắc Customer bổ sung hồ sơ còn thiếu. Worker cập nhật trạng thái gửi và retry khi lỗi.

| Field | Type | Key | Required | Ý nghĩa |
|---|---|---|---|---|
| `missing_document_reminder_id` | `uuid` | PK | No | Khóa chính của reminder. |
| `compliance_dossier_id` | `uuid` | FK | Yes | ComplianceDossier có tài liệu còn thiếu. |
| `recipient_account_id` | `uuid` | FK | Yes | Account nhận email nhắc. |
| `requirement_ids` | `uuid[]` |  | Yes | Danh sách DossierRequirement còn thiếu. |
| `due_date` | `date` |  | Yes | Hạn bổ sung hồ sơ. |
| `note` | `varchar(2000)` |  | No | Ghi chú trong reminder. |
| `automatic_date` | `date` |  | No | Ngày reminder tự động được tạo. |
| `created_at` | `timestamp` |  | Yes | Thời điểm tạo reminder. |
| `last_attempt_at` | `timestamp` |  | No | Lần gửi email gần nhất. |
| `email_sent_at` | `timestamp` |  | No | Thời điểm gửi email thành công. |
| `cancelled_at` | `timestamp` |  | No | Thời điểm reminder bị hủy. |
| `email_attempts` | `int` |  | Yes | Số lần worker thử gửi email. |

# 55. `OutboxMessage`

**Ý nghĩa bảng:** Lưu integration event trong cùng transaction với thay đổi nghiệp vụ. Worker xử lý sau commit cho email và SignalR.

| Field | Type | Key | Required | Ý nghĩa |
|---|---|---|---|---|
| `outbox_message_id` | `uuid` | PK | No | Khóa chính của outbox message. |
| `message_type` | `varchar(100)` |  | Yes | Loại message, hiện gồm `EMAIL` và `SIGNALR_ACCOUNT`. |
| `payload` | `text` |  | Yes | Payload JSON dành cho integration tương ứng. |
| `status` | `varchar(20)` |  | Yes | Trạng thái xử lý: `PENDING`, `PROCESSING`, `FAILED`, `PROCESSED`, `DEAD`. |
| `attempts` | `int` |  | Yes | Số lần xử lý thất bại. |
| `available_at` | `timestamp` |  | Yes | Thời điểm message đủ điều kiện xử lý/retry. |
| `locked_at` | `timestamp` |  | No | Thời điểm worker claim message. |
| `processed_at` | `timestamp` |  | No | Thời điểm xử lý thành công. |
| `last_error` | `text` |  | No | Lỗi gần nhất khi xử lý. |
| `created_at` | `timestamp` |  | Yes | Thời điểm tạo message trong transaction nghiệp vụ. |
| `updated_at` | `timestamp` |  | Yes | Thời điểm cập nhật message gần nhất. |

# Appendix A — Các business rule V8 cần backend kiểm tra

1. `Vehicle.transport_mode` phải phù hợp với `Route.transport_mode` của chặng được gán qua `RoutePlanRouteVehicle`.
2. `RoutePlanRoute.driver_employee_id` nếu có phải trỏ tới Employee có `role = DRIVER`.
3. Trước khi RoutePlan chuyển `CONFIRMED/ACTIVE`, mỗi chặng cần Driver theo business rule của V6.
4. Handover dùng XOR Customer/Employee ở mỗi phía; không được đồng thời điền cả customer và employee cho cùng một phía.
5. Handover giữa hai chặng phải cùng RoutePlan và chặng `to` phải ngay sau chặng `from` (`to.sequence_no = from.sequence_no + 1`).
6. Bàn giao Driver → Driver phải khớp Driver được assign tại hai `RoutePlanRoute` tương ứng.
7. `Notification.reference_type + reference_id` là polymorphic reference nên phải được application validate.
8. Các tổng tiền trong `FinancialReport` phải được tính từ dữ liệu nguồn, không tin trực tiếp giá trị từ client.

9. Soft delete áp dụng cho 11 bảng đã chốt; riêng `DossierRequirement` chỉ cho phép soft-delete Optional DR. Các transaction/history table tiếp tục dùng status/lifecycle/history thay vì `is_deleted`.
10. Với các field unique trong bảng soft delete, nếu muốn tái sử dụng giá trị sau khi record cũ bị soft delete thì PostgreSQL migration nên dùng partial unique index như `WHERE is_deleted = false`.
11. `DossierRequirement.horse_id` nullable: `NULL` là requirement cấp chuyến; có giá trị là requirement riêng cho Horse. Unique scope phải xét cả `horse_id`.
12. Khi tạo `DossierRequirement`, nếu `ComplianceRequirement.validity_period_days` có giá trị thì backend tính `valid_until` từ ngày áp dụng; không dùng một ngày hết hạn chung cho toàn dossier.
13. `AuthoritySubmissionEvidenceFile` chỉ lưu bằng chứng phản hồi của Authority cho một `AuthoritySubmission`; `evidence_type` phải phản ánh ACCEPTANCE, REJECTION, ADDITIONAL_INFORMATION hoặc OTHER.
14. `QuarantineProof` và `CustomsClearanceProof` là hai luồng bằng chứng riêng; backend phải validate status transition, actor và nội dung `microchip_checklist` tương ứng với nghiệp vụ. Mọi kết quả tại cửa khẩu, kể cả `FAILED`/`REJECTED`, không được ghi đè `ComplianceDossier.status`.
15. `ProofAttachment` phải tham chiếu đúng một Proof bằng XOR giữa `quarantine_proof_id` và `customs_clearance_proof_id`; không được lưu đồng thời cả hai hoặc để cả hai NULL.
16. `AuditDossier` là append-only: không update/delete qua API; `target_entity + target_id` phải được validate vì đây là polymorphic reference.
17. Các file trong `DocumentAttachment` thuộc đúng một `DocumentVersion`; các file phản hồi Authority thuộc `AuthoritySubmissionEvidenceFile`, không gộp vào hồ sơ Customer.

# Appendix B — Phân biệt nhanh nhóm Compliance

| Table | Nghĩa |
|---|---|
| `ComplianceRequirement` | Loại yêu cầu pháp lý master mà hệ thống biết. |
| `DossierTemplate` | Bộ hồ sơ mẫu. |
| `DossierTemplateItem` | Requirement được cấu hình trong template. |
| `ComplianceDossier` | Bộ hồ sơ pháp lý thực tế của một Transport. |
| `DossierRequirement` | Requirement thực tế của chuyến. |
| `ComplianceDocument` | Giấy tờ logic Customer cung cấp. |
| `DocumentVersion` | File/version cụ thể của giấy tờ. |
| `DocumentReview` | Kết quả kiểm tra một version. |
| `DocumentRequest` | Yêu cầu Customer bổ sung/sửa/upload lại. |
| `AuthoritySubmission` | Một lần gửi dossier tới cơ quan có thẩm quyền. |
| `DocumentAttachment` | Một file vật lý thuộc một `DocumentVersion`; một version có thể có nhiều file. |
| `AuthoritySubmissionEvidenceFile` | File/văn bản phản hồi của Authority: chấp nhận, từ chối hoặc yêu cầu bổ sung. |
| `QuarantineProof` | Bằng chứng kiểm dịch/BCP, gồm BCP, OV, receipt, microchip checklist và file đính kèm. |
| `CustomsClearanceProof` | Bằng chứng thông quan hải quan, gồm MRN, customs lane, trạng thái và file đính kèm. |
| `ProofAttachment` | File vật lý của QuarantineProof hoặc CustomsClearanceProof; một proof có thể có nhiều file. |
| `AuditDossier` | Nhật ký audit append-only cho các thay đổi trong compliance dossier. |

**Tổng số bảng:** 56
**Tổng số enum:** 56
**Tổng số field được mô tả:** 702

