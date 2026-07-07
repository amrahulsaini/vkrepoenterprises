CREATE TABLE IF NOT EXISTS integration_uploads (
  id            INT UNSIGNED NOT NULL AUTO_INCREMENT,
  finance_id    INT UNSIGNED NOT NULL,
  branch_id     INT UNSIGNED NOT NULL,
  uploaded_by   VARCHAR(200)  DEFAULT NULL,
  file_name     VARCHAR(500)  DEFAULT NULL,
  file_path     VARCHAR(1000) DEFAULT NULL,
  total_records INT NOT NULL DEFAULT 0,
  created_at    TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
  PRIMARY KEY (id),
  KEY idx_iu_finance (finance_id),
  KEY idx_iu_branch (branch_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;

ALTER TABLE vehicle_records ADD COLUMN IF NOT EXISTS upload_id INT UNSIGNED DEFAULT NULL;
