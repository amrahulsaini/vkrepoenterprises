-- ─────────────────────────────────────────────────────────────────────────
--  id_cards — official agent ID-card requests & approvals. One row per user.
--  Flow: user submits documents (status='pending') -> admin approves in the
--  desktop app with a validity in days (status='approved', valid_until set) or
--  declines with a reason (status='declined'; user must re-submit). The mobile
--  app shows the card only while status='approved' AND valid_until >= today.
--  Lives in each tenant DB (+ tenant_template.sql for new agencies).
-- ─────────────────────────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS id_cards (
    user_id        BIGINT       NOT NULL,
    photo_path     VARCHAR(255)          DEFAULT NULL,
    pcc_path       VARCHAR(255)          DEFAULT NULL,
    dra_path       VARCHAR(255)          DEFAULT NULL,
    blood_group    VARCHAR(8)            DEFAULT NULL,
    dob            VARCHAR(20)           DEFAULT NULL,
    gender         VARCHAR(10)           DEFAULT NULL,
    status         VARCHAR(12)  NOT NULL DEFAULT 'pending',  -- pending | approved | declined
    decline_reason TEXT                  DEFAULT NULL,
    valid_days     INT                   DEFAULT NULL,
    approved_at    DATETIME              DEFAULT NULL,
    valid_until    DATE                  DEFAULT NULL,
    submitted_at   DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at     TIMESTAMP    NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    PRIMARY KEY (user_id),
    INDEX idx_idcard_status (status),
    CONSTRAINT fk_idcard_user FOREIGN KEY (user_id)
        REFERENCES app_users(id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
