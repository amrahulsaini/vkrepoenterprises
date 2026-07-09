CREATE TABLE IF NOT EXISTS integration_agency_messages (
  id                     INT UNSIGNED NOT NULL AUTO_INCREMENT,
  integration_account_id INT          NOT NULL,
  from_finance_name      VARCHAR(200) DEFAULT NULL,
  from_email             VARCHAR(200) DEFAULT NULL,
  message                TEXT         NOT NULL,
  is_read                TINYINT(1)   NOT NULL DEFAULT 0,
  created_at             TIMESTAMP    NOT NULL DEFAULT CURRENT_TIMESTAMP,
  PRIMARY KEY (id),
  KEY idx_iam_created (created_at),
  KEY idx_iam_read (is_read)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
