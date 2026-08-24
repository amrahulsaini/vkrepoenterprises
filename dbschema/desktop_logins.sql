
CREATE TABLE IF NOT EXISTS desktop_logins (
    id           BIGINT AUTO_INCREMENT PRIMARY KEY,
    user_id      BIGINT       NOT NULL,
    at           DATETIME     NOT NULL,
    work_date    DATE         NOT NULL,
    method       ENUM('password','fingerprint') NOT NULL DEFAULT 'password',
    device_label VARCHAR(160) NOT NULL DEFAULT '',
    INDEX idx_dl_user_day (user_id, work_date),
    INDEX idx_dl_day (work_date),
    CONSTRAINT fk_dl_user FOREIGN KEY (user_id) REFERENCES app_users(id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
