-- Repossession letter settings (per tenant DB).
-- Stores the editable defaults remembered for the Pre-Repossession letter:
--   * finance_id = 0  -> agency-level defaults (police-station To-block + fallback agency name)
--   * finance_id > 0  -> per head-office (finance) overrides: agency name + "authorized by" line
-- Values are used only to pre-fill the PDF; they are never written back to vehicle_records.

CREATE TABLE IF NOT EXISTS `repo_letter_settings` (
  `finance_id`     INT          NOT NULL DEFAULT 0 COMMENT '0 = agency-level defaults; otherwise finances.id',
  `agency_name`    VARCHAR(255) NULL,
  `authorized_by`  VARCHAR(255) NULL COMMENT 'head office name override; defaults to finances.name',
  `police_station` VARCHAR(255) NULL,
  `police_address` TEXT         NULL,
  `updated_at`     TIMESTAMP    NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  PRIMARY KEY (`finance_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
