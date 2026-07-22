-- ─────────────────────────────────────────────────────────────────────────
--  confirm_captures — photo captured by a field agent at "Send Confirm" time.
--  One row per confirmation photo: which vehicle (RC + chassis), who took it,
--  when, and where the image is stored. Lives in each tenant DB.
--  Run on every existing tenant DB; also part of tenant_template.sql for new
--  agencies.
-- ─────────────────────────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS confirm_captures (
    id          BIGINT       NOT NULL AUTO_INCREMENT,
    user_id     BIGINT       NOT NULL,
    vehicle_no  VARCHAR(32)           DEFAULT NULL,
    chassis_no  VARCHAR(40)           DEFAULT NULL,
    image_path  VARCHAR(255) NOT NULL,
    captured_at DATETIME              DEFAULT NULL,
    created_at  TIMESTAMP    NOT NULL DEFAULT CURRENT_TIMESTAMP,
    PRIMARY KEY (id),
    INDEX idx_cc_user    (user_id),
    INDEX idx_cc_vehicle (vehicle_no),
    INDEX idx_cc_chassis (chassis_no)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
