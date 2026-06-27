CREATE TABLE IF NOT EXISTS `billing_settings` (
  `id`                INT          NOT NULL DEFAULT 1,
  `agency_name`       VARCHAR(255) NULL,
  `header_address`    VARCHAR(512) NULL,
  `header_contact`    VARCHAR(255) NULL,
  `header_email`      VARCHAR(255) NULL,
  `pan_no`            VARCHAR(32)  NULL,
  `gst_state`         VARCHAR(64)  NULL,
  `bank_account_name` VARCHAR(255) NULL,
  `account_no`        VARCHAR(64)  NULL,
  `ifsc_code`         VARCHAR(32)  NULL,
  `bank_branch`       VARCHAR(128) NULL,
  `parking_yard`      VARCHAR(255) NULL,
  `payment_name`      VARCHAR(255) NULL,
  `footer_line`       VARCHAR(255) NULL,
  `updated_at`        TIMESTAMP    NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  PRIMARY KEY (`id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
