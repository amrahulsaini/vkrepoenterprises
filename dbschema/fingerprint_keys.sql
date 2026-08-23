CREATE TABLE IF NOT EXISTS device_keys (
  id            BIGINT AUTO_INCREMENT PRIMARY KEY,
  user_id       BIGINT       NOT NULL,
  key_id        CHAR(16)     NOT NULL,
  public_key    TEXT         NOT NULL,
  device_label  VARCHAR(160) NOT NULL DEFAULT '',
  device_id     VARCHAR(500) NULL,
  enrolled_at   DATETIME     NOT NULL,
  last_used_at  DATETIME     NULL,
  revoked       TINYINT(1)   NOT NULL DEFAULT 0,
  revoked_at    DATETIME     NULL,
  UNIQUE KEY uq_dk_key (key_id),
  INDEX idx_dk_user (user_id, revoked),
  CONSTRAINT fk_dk_user FOREIGN KEY (user_id) REFERENCES app_users(id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;

ALTER TABLE `app_users`
  ADD COLUMN IF NOT EXISTS `fingerprint_required` TINYINT(1) NOT NULL DEFAULT 0 AFTER `profile_password_by`,
  ADD COLUMN IF NOT EXISTS `fingerprint_waived_until` DATETIME NULL AFTER `fingerprint_required`;
