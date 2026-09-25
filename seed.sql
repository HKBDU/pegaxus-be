-- ========================================================
-- PEGAXUS SEED DATA SCRIPT (FLOW 1)
-- ========================================================

BEGIN;

-- 1. SEED EMPLOYEE ACCOUNTS (Password: Pegaxus@2026)
INSERT INTO "Account" (
    account_id, email, password_hash, account_type, status,
    email_verified_at, is_deleted, created_at, updated_at
) VALUES 
('a1111111-1111-1111-1111-111111111111', 'lm@pegaxus.com', 'AQAAAAIAAYagAAAAECYV6abJbLMF6CCjeM++f3roirLF/6sGIB1vSzZuP1hzn7kQX7TK15BfzTZPN8nFfw==', 'EMPLOYEE', 'ACTIVE', NOW(), false, NOW(), NOW()),
('a2222222-2222-2222-2222-222222222222', 'ts@pegaxus.com', 'AQAAAAIAAYagAAAAECYV6abJbLMF6CCjeM++f3roirLF/6sGIB1vSzZuP1hzn7kQX7TK15BfzTZPN8nFfw==', 'EMPLOYEE', 'ACTIVE', NOW(), false, NOW(), NOW()),
('a3333333-3333-3333-3333-333333333333', 'frc@pegaxus.com', 'AQAAAAIAAYagAAAAECYV6abJbLMF6CCjeM++f3roirLF/6sGIB1vSzZuP1hzn7kQX7TK15BfzTZPN8nFfw==', 'EMPLOYEE', 'ACTIVE', NOW(), false, NOW(), NOW()),
('a4444444-4444-4444-4444-444444444444', 'driver@pegaxus.com', 'AQAAAAIAAYagAAAAECYV6abJbLMF6CCjeM++f3roirLF/6sGIB1vSzZuP1hzn7kQX7TK15BfzTZPN8nFfw==', 'EMPLOYEE', 'ACTIVE', NOW(), false, NOW(), NOW()),
('a5555555-5555-5555-5555-555555555555', 'escort@pegaxus.com', 'AQAAAAIAAYagAAAAECYV6abJbLMF6CCjeM++f3roirLF/6sGIB1vSzZuP1hzn7kQX7TK15BfzTZPN8nFfw==', 'EMPLOYEE', 'ACTIVE', NOW(), false, NOW(), NOW())
ON CONFLICT (email) DO NOTHING;

-- 2. SEED EMPLOYEES
INSERT INTO "Employee" (
    employee_id, account_id, employee_code, role, full_name,
    phone, address, country, date_of_birth, is_active, is_deleted, created_at, updated_at
) VALUES 
('e1111111-1111-1111-1111-111111111111', 'a1111111-1111-1111-1111-111111111111', 'EMP-LM-001', 'LOGISTICS_MANAGER', 'Lê Hoàng Phúc', '0908123456', 'Pegaxus Headquarters, District 1, Ho Chi Minh City', 'Vietnam', '1985-04-12', true, false, NOW(), NOW()),
('e2222222-2222-2222-2222-222222222222', 'a2222222-2222-2222-2222-222222222222', 'EMP-TS-001', 'TRANSPORT_SPECIALIST', 'Trần Thanh Tâm', '0908234567', 'Pegaxus Headquarters, District 1, Ho Chi Minh City', 'Vietnam', '1990-08-20', true, false, NOW(), NOW()),
('e3333333-3333-3333-3333-333333333333', 'a3333333-3333-3333-3333-333333333333', 'EMP-FRC-001', 'FLEET_ROUTE_COORDINATOR', 'Phạm Quốc Bảo', '0908345678', 'Pegaxus Headquarters, District 1, Ho Chi Minh City', 'Vietnam', '1988-11-05', true, false, NOW(), NOW()),
('e4444444-4444-4444-4444-444444444444', 'a4444444-4444-4444-4444-444444444444', 'EMP-DRV-001', 'DRIVER', 'Nguyễn Văn Lái', '0908456789', 'Pegaxus Transport Hub, Binh Duong', 'Vietnam', '1992-03-15', true, false, NOW(), NOW()),
('e5555555-5555-5555-5555-555555555555', 'a5555555-5555-5555-5555-555555555555', 'EMP-ESC-001', 'ESCORT', 'Hoàng Thị Mai', '0908567890', 'Pegaxus Transport Hub, Binh Duong', 'Vietnam', '1995-07-22', true, false, NOW(), NOW())
ON CONFLICT (account_id) DO NOTHING;

-- 3. SEED CUSTOMER ACCOUNT (Password: Racing2026)
INSERT INTO "Account" (
    account_id, email, password_hash, account_type, status,
    email_verified_at, is_deleted, created_at, updated_at
) VALUES 
('a6666666-6666-6666-6666-666666666666', 'khang@saigonracing.vn', 'AQAAAAIAAYagAAAAEGC0mNtCiru/e5U3O65DAtFEpgZJhvzOWO6iaGahk/uZCBlrOudmoxcRUnsVJ3c9bQ==', 'CUSTOMER', 'ACTIVE', NOW(), false, NOW(), NOW())
ON CONFLICT (email) DO NOTHING;

-- 4. SEED CUSTOMER
INSERT INTO "Customer" (
    customer_id, account_id, full_name, phone, address, country, date_of_birth, is_deleted, created_at, updated_at
) VALUES 
('c6666666-6666-6666-6666-666666666666', 'a6666666-6666-6666-6666-666666666666', 'Nguyễn Minh Khang', '0901234567', '12 Nguyễn Huệ, Quận 1, TP.HCM', 'Vietnam', '1990-05-12', false, NOW(), NOW())
ON CONFLICT (account_id) DO NOTHING;

-- 5. SEED HORSES FOR KHANG (khang@saigonracing.vn)
INSERT INTO "Horse" (
    horse_id, customer_id, horse_name, sex, breed, date_of_birth, color, country_of_origin,
    weight_kg, height_cm, passport_number, microchip_number, identification_notes, is_deleted, created_at, updated_at
) VALUES 
('baaaaaaa-1111-1111-1111-111111111111', 'c6666666-6666-6666-6666-666666666666', 'Golden Thunder', 'MALE', 'Thoroughbred', '2021-09-14', 'Bay', 'Australia', 520.5, 165.2, 'AUS-TB-2021-0457', '985112003456781', 'Đốm trắng trên trán', false, NOW(), NOW()),
('baaaaaaa-2222-2222-2222-222222222222', 'c6666666-6666-6666-6666-666666666666', 'Silver Arrow', 'FEMALE', 'Thoroughbred', '2022-10-02', 'Grey', 'Australia', 510.0, 162.0, 'AUS-TB-2022-0001', '985112003456782', 'White mark on forehead', false, NOW(), NOW()),
('baaaaaaa-3333-3333-3333-333333333333', 'c6666666-6666-6666-6666-666666666666', 'Black Stallion', 'MALE', 'Arabian', '2020-04-18', 'Black', 'Australia', 495.0, 158.0, 'AUS-AR-2020-0088', '985112003456783', 'Star on forehead', false, NOW(), NOW())
ON CONFLICT (horse_id) DO NOTHING;

-- 6. SEED HORSES FOR QUOCBAO (quocbao.nguyen16102006@gmail.com)
INSERT INTO "Horse" (
    horse_id, customer_id, horse_name, sex, breed, date_of_birth, color, country_of_origin,
    weight_kg, height_cm, passport_number, microchip_number, identification_notes, is_deleted, created_at, updated_at
) VALUES 
('baaaaaaa-4444-4444-4444-444444444444', '6990fbdd-bcf2-4de0-9bd2-20d491df8c97', 'Red Rum', 'MALE', 'Thoroughbred', '2021-03-12', 'Bay', 'United Kingdom', 512.0, 164.0, 'GBR-TB-2021-0111', '985112003456111', 'White star on forehead, two white socks', false, NOW(), NOW()),
('baaaaaaa-5555-5555-5555-555555555555', '6990fbdd-bcf2-4de0-9bd2-20d491df8c97', 'Secretariat', 'MALE', 'Thoroughbred', '2020-05-20', 'Chestnut', 'United States', 530.0, 168.0, 'USA-TB-2020-0222', '985112003456222', 'White star and stripe, three white stockings', false, NOW(), NOW()),
('baaaaaaa-6666-6666-6666-666666666666', '6990fbdd-bcf2-4de0-9bd2-20d491df8c97', 'Shadowfax', 'GELDING', 'Arabian', '2022-01-15', 'White', 'Australia', 485.0, 160.0, 'AUS-AR-2022-0333', '985112003456333', 'Pure white coat, black hooves', false, NOW(), NOW())
ON CONFLICT (horse_id) DO NOTHING;

-- 7. SEED HORSES FOR BAO46656 (bao46656@gmail.com)
INSERT INTO "Horse" (
    horse_id, customer_id, horse_name, sex, breed, date_of_birth, color, country_of_origin,
    weight_kg, height_cm, passport_number, microchip_number, identification_notes, is_deleted, created_at, updated_at
) VALUES 
('baaaaaaa-7777-7777-7777-777777777777', 'b3b91db6-060b-4d1b-86bb-a2a1bd34bd97', 'Man o War', 'MALE', 'Thoroughbred', '2021-06-10', 'Chestnut', 'United States', 525.0, 166.0, 'USA-TB-2021-0444', '985112003456444', 'White star', false, NOW(), NOW()),
('baaaaaaa-8888-8888-8888-888888888888', 'b3b91db6-060b-4d1b-86bb-a2a1bd34bd97', 'Eclipse', 'FEMALE', 'Arabian', '2022-08-04', 'Bay', 'United Kingdom', 490.0, 159.0, 'GBR-AR-2022-0555', '985112003456555', 'Right hind foot white', false, NOW(), NOW())
ON CONFLICT (horse_id) DO NOTHING;

-- 8. SEED HORSES FOR NGUYENVANA (nguyenvana@example.com)
INSERT INTO "Horse" (
    horse_id, customer_id, horse_name, sex, breed, date_of_birth, color, country_of_origin,
    weight_kg, height_cm, passport_number, microchip_number, identification_notes, is_deleted, created_at, updated_at
) VALUES 
('baaaaaaa-9999-9999-9999-999999999999', '576102b0-3585-4e7e-b28e-19400d9b1ed5', 'Northern Dancer', 'MALE', 'Thoroughbred', '2021-02-14', 'Bay', 'Canada', 505.0, 161.0, 'CAN-TB-2021-0666', '985112003456666', 'White sock on left foreleg', false, NOW(), NOW()),
('baaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa', '576102b0-3585-4e7e-b28e-19400d9b1ed5', 'Frankel', 'MALE', 'Thoroughbred', '2020-07-28', 'Bay', 'United Kingdom', 535.0, 167.0, 'GBR-TB-2020-0777', '985112003456777', 'White star and snip', false, NOW(), NOW())
ON CONFLICT (horse_id) DO NOTHING;

-- 9. SEED HORSES FOR NBAO05843 (nbao05843@gmail.com)
INSERT INTO "Horse" (
    horse_id, customer_id, horse_name, sex, breed, date_of_birth, color, country_of_origin,
    weight_kg, height_cm, passport_number, microchip_number, identification_notes, is_deleted, created_at, updated_at
) VALUES 
('baaaaaaa-bbbb-bbbb-bbbb-bbbbbbbbbbbb', '39268624-e48f-4c9a-835c-918c9378660c', 'Desert Orchid', 'GELDING', 'Thoroughbred', '2021-11-05', 'Grey', 'United Kingdom', 518.0, 165.0, 'GBR-TB-2021-0888', '985112003456888', 'Grey roan, blaze', false, NOW(), NOW()),
('baaaaaaa-cccc-cccc-cccc-cccccccccccc', '39268624-e48f-4c9a-835c-918c9378660c', 'Zenyatta', 'FEMALE', 'Thoroughbred', '2022-04-01', 'Dark Bay', 'United States', 540.0, 172.0, 'USA-TB-2022-0999', '985112003456999', 'White dot on forehead', false, NOW(), NOW())
ON CONFLICT (horse_id) DO NOTHING;

-- 10. SEED SAMPLE PENDING BOOKING (FOR KHANG)
INSERT INTO "Booking" (
    booking_id, booking_code, customer_id, status,
    origin_address, origin_country, destination_address, destination_country,
    requested_departure_date, requested_arrival_date,
    special_requirements, customer_note, submitted_at, created_at, updated_at
) VALUES 
('bbbbbbbb-1111-1111-1111-111111111111', 'BK-20260917-0001', 'c6666666-6666-6666-6666-666666666666', 'PENDING',
'Flemington Racecourse, 448 Epsom Rd, Melbourne', 'Australia',
'Sha Tin Racecourse, Tai Po Road, Sha Tin', 'Hong Kong',
CURRENT_DATE + INTERVAL '25 days', CURRENT_DATE + INTERVAL '28 days',
'Xe có điều hòa, khoang đơn; cần bác sĩ thú y đi kèm', 'Yêu cầu vận chuyển tiêu chuẩn thi đấu',
NOW(), NOW(), NOW())
ON CONFLICT (booking_id) DO NOTHING;

-- 11. SEED BOOKING HORSE
INSERT INTO "BookingHorse" (
    booking_id, horse_id, special_handling_notes, created_at
) VALUES 
('bbbbbbbb-1111-1111-1111-111111111111', 'baaaaaaa-1111-1111-1111-111111111111', 'Chỉ ăn cỏ khô, không ngũ cốc', NOW())
ON CONFLICT (booking_id, horse_id) DO NOTHING;

-- 12. SEED BOOKING STATUS HISTORY (Action: CREATED)
INSERT INTO "BookingStatusHistory" (
    booking_status_history_id, booking_id, type, previous_status, new_status,
    changed_by_account_id, reason, changed_at
) VALUES 
('bbbbbbbb-5555-5555-5555-555555555555', 'bbbbbbbb-1111-1111-1111-111111111111', 'SUBMIT', NULL, 'PENDING',
'a6666666-6666-6666-6666-666666666666', 'Created initial booking', NOW())
ON CONFLICT (booking_status_history_id) DO NOTHING;

COMMIT;
