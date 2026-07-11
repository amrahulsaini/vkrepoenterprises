CREATE TABLE IF NOT EXISTS `user_billing_targets` (
  `user_id` BIGINT   NOT NULL,
  `year`    SMALLINT NOT NULL,
  `month`   TINYINT  NOT NULL,
  `demand`  INT      NULL,
  `target`  INT      NULL,
  PRIMARY KEY (`user_id`, `year`, `month`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;

INSERT IGNORE INTO `user_billing_targets` (`user_id`, `year`, `month`, `demand`, `target`)
SELECT `id`, YEAR(CURDATE()), MONTH(CURDATE()), `billing_demand`, `billing_target`
  FROM `app_users`
 WHERE `billing_demand` IS NOT NULL OR `billing_target` IS NOT NULL;

ALTER TABLE `app_users` DROP COLUMN IF EXISTS `billing_demand`;
ALTER TABLE `app_users` DROP COLUMN IF EXISTS `billing_target`;
