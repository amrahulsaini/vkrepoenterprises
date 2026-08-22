CREATE TABLE IF NOT EXISTS `app_user_settings` (
  `user_id`         BIGINT       NOT NULL,
  `two_column_view` TINYINT(1)   NOT NULL DEFAULT 1,
  `online_only`     TINYINT(1)   NOT NULL DEFAULT 1,
  `show_hyphens`    TINYINT(1)   NOT NULL DEFAULT 1,
  `updated_at`      TIMESTAMP    NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  PRIMARY KEY (`user_id`),
  CONSTRAINT `fk_app_user_settings_user` FOREIGN KEY (`user_id`)
    REFERENCES `app_users` (`id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
