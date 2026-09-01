CREATE TABLE IF NOT EXISTS `repo_advances` (
  `id`            BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  `submission_id` BIGINT UNSIGNED NOT NULL,
  `amount`        DECIMAL(12,2)   NOT NULL,
  `advance_date`  DATE            NOT NULL,
  `note`          VARCHAR(255)    NULL,
  `created_at`    TIMESTAMP       NOT NULL DEFAULT CURRENT_TIMESTAMP,
  PRIMARY KEY (`id`),
  KEY `idx_repo_advances_sub` (`submission_id`),
  CONSTRAINT `fk_repo_advances_sub` FOREIGN KEY (`submission_id`)
    REFERENCES `repo_submissions` (`id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;

INSERT INTO `repo_advances` (`submission_id`, `amount`, `advance_date`)
SELECT rs.`id`, rs.`advance`, DATE(COALESCE(rs.`courier_updated_at`, rs.`created_at`))
  FROM `repo_submissions` rs
 WHERE rs.`advance` IS NOT NULL AND rs.`advance` <> 0
   AND NOT EXISTS (SELECT 1 FROM `repo_advances` ra WHERE ra.`submission_id` = rs.`id`);
