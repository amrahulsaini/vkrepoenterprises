-- ─────────────────────────────────────────────────────────────────────────
--  Mobile app users + subscriptions
--  Run once on the MySQL server.
-- ─────────────────────────────────────────────────────────────────────────

CREATE TABLE IF NOT EXISTS app_users (
    id         BIGINT        NOT NULL AUTO_INCREMENT,
    mobile     VARCHAR(15)   NOT NULL,
    name       VARCHAR(150)  NOT NULL,
    address    TEXT,
    pincode    VARCHAR(10),
    pfp        MEDIUMTEXT,             -- base64-encoded image (optional)
    device_id  VARCHAR(500),           -- Android device ID registered at signup
    is_active  TINYINT(1)    NOT NULL DEFAULT 0,  -- 0 = pending admin approval
    is_admin   TINYINT(1)    NOT NULL DEFAULT 0,
    balance    DECIMAL(10,2)          DEFAULT 0.00,
    created_at TIMESTAMP     NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMP     NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    PRIMARY KEY (id),
    UNIQUE  KEY uq_mobile   (mobile),
    INDEX       idx_device  (device_id(100))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS subscriptions (
    id         BIGINT        NOT NULL AUTO_INCREMENT,
    user_id    BIGINT        NOT NULL,
    start_date DATE          NOT NULL,
    end_date   DATE          NOT NULL,
    amount     DECIMAL(10,2),
    notes      VARCHAR(300),
    created_at TIMESTAMP     NOT NULL DEFAULT CURRENT_TIMESTAMP,
    PRIMARY KEY (id),
    INDEX       idx_user    (user_id),
    CONSTRAINT  fk_sub_user FOREIGN KEY (user_id)
        REFERENCES app_users(id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Recommended indexes for rc_info / chassis_info if not already present
-- (these power the instant vehicle search on mobile)
-- CREATE INDEX IF NOT EXISTS idx_last4 ON rc_info  (last4);
-- CREATE INDEX IF NOT EXISTS idx_last5 ON chassis_info (last5);
