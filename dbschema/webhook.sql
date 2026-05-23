-- Webhook tables — mirrored from MongoDB collections on droplet-no-2
-- Run against every tenant database (crmr_<slug>) and the legacy vkre_db1.

CREATE TABLE IF NOT EXISTS `webhook_banks` (
  `id`         int          NOT NULL AUTO_INCREMENT,
  `bank_name`  varchar(255) NOT NULL,
  `created_at` timestamp    NOT NULL DEFAULT current_timestamp(),
  PRIMARY KEY (`id`),
  UNIQUE KEY `uq_bank_name` (`bank_name`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;

CREATE TABLE IF NOT EXISTS `webhook_files` (
  `id`            int           NOT NULL AUTO_INCREMENT,
  `bank_id`       int           NOT NULL,
  `file_name`     varchar(500)  NOT NULL,
  `file_path`     varchar(1000) DEFAULT NULL,
  `vehicle_type`  varchar(100)  DEFAULT NULL,
  `uploaded_by`   varchar(255)  DEFAULT NULL,
  `uploaded_date` varchar(100)  DEFAULT NULL,
  `file_guid`     varchar(255)  DEFAULT NULL,
  `total_records` int           NOT NULL DEFAULT 0,
  `created_at`    timestamp     NOT NULL DEFAULT current_timestamp(),
  PRIMARY KEY (`id`),
  KEY `idx_wf_bank` (`bank_id`),
  KEY `idx_wf_created` (`created_at`),
  CONSTRAINT `fk_wf_bank` FOREIGN KEY (`bank_id`) REFERENCES `webhook_banks` (`id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;

CREATE TABLE IF NOT EXISTS `webhook_users` (
  `id`            int          NOT NULL AUTO_INCREMENT,
  `username`      varchar(255) NOT NULL,
  `password_hash` varchar(64)  NOT NULL,
  `created_at`    timestamp    NOT NULL DEFAULT current_timestamp(),
  PRIMARY KEY (`id`),
  UNIQUE KEY `uq_username` (`username`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
