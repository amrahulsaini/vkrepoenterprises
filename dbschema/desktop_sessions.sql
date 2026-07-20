CREATE TABLE IF NOT EXISTS desktop_sessions (
  id            BIGINT AUTO_INCREMENT PRIMARY KEY,
  agency_id     INT          NOT NULL,
  token_hash    CHAR(64)     NOT NULL,
  pw_stamp      CHAR(64)     NOT NULL,
  device_label  VARCHAR(190) NULL,
  created_at    DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,
  last_used_at  DATETIME     NULL,
  expires_at    DATETIME     NOT NULL,
  revoked       TINYINT(1)   NOT NULL DEFAULT 0,
  UNIQUE KEY uk_desktop_sessions_token (token_hash),
  KEY idx_desktop_sessions_agency (agency_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
