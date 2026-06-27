CREATE TABLE IF NOT EXISTS `repo_letter_settings` (
  `finance_id`     INT          NOT NULL DEFAULT 0,
  `agency_name`    VARCHAR(255) NULL,
  `authorized_by`  VARCHAR(255) NULL,
  `police_station` VARCHAR(255) NULL,
  `police_address` TEXT         NULL,
  `logo_path`      VARCHAR(512) NULL,
  `updated_at`     TIMESTAMP    NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  PRIMARY KEY (`finance_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
