-- ── Feature 3: Stop user app ──────────────────────────────────────────────
ALTER TABLE app_users
    ADD COLUMN IF NOT EXISTS is_stopped TINYINT(1) NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS is_blacklisted TINYINT(1) NOT NULL DEFAULT 0;

-- ── Feature 2: Restrict finances per user ─────────────────────────────────
CREATE TABLE IF NOT EXISTS user_finance_restrictions (
    id         BIGINT AUTO_INCREMENT PRIMARY KEY,
    user_id    BIGINT NOT NULL,
    finance_id INT    NOT NULL,
    created_at DATETIME DEFAULT NOW(),
    UNIQUE KEY uq_user_finance (user_id, finance_id),
    KEY        idx_ufr_user   (user_id)
);

-- ── Feature 5: App settings (stores subspass and other server-side config) ─
CREATE TABLE IF NOT EXISTS app_settings (
    `key`       VARCHAR(100) NOT NULL PRIMARY KEY,
    `value`     TEXT,
    updated_at  DATETIME DEFAULT NOW() ON UPDATE NOW()
);

-- Default subscription management password (change via desktop settings)
INSERT INTO app_settings (`key`, `value`)
VALUES ('subs_password', '1234')
ON DUPLICATE KEY UPDATE `key` = `key`;
