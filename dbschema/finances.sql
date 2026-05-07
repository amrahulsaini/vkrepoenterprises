-- Schema for VK Enterprises finances and branches
-- Run this on your MySQL server (use the vkre_db1 database)
-- Assumes MySQL 5.7+ (InnoDB, utf8mb4, fulltext support)

-- Ensure database exists (optional)
CREATE DATABASE IF NOT EXISTS `vkre_db1` CHARACTER SET = utf8mb4 COLLATE = utf8mb4_general_ci;
USE `vkre_db1`;

-- Finances table: stores finance entities (e.g., finance providers)
CREATE TABLE IF NOT EXISTS `finances` (
  `id` INT UNSIGNED NOT NULL AUTO_INCREMENT,
  `name` VARCHAR(255) NOT NULL,
  `slug` VARCHAR(255) GENERATED ALWAYS AS (LOWER(REPLACE(name, ' ', '-'))) VIRTUAL,
  `description` TEXT NULL,
  `is_active` TINYINT(1) NOT NULL DEFAULT 1,
  `created_at` TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
  `updated_at` TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  PRIMARY KEY (`id`),
  UNIQUE KEY `uq_finances_name` (`name`),
  KEY `idx_finances_created_at` (`created_at`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;

-- Fulltext index for fast name search (supported in InnoDB on modern MySQL)
-- Use MATCH(... AGAINST(...)) for relevance search
ALTER TABLE `finances` ADD FULLTEXT KEY `ft_finances_name` (`name`);

-- Branches table: many branches per finance
CREATE TABLE IF NOT EXISTS `branches` (
  `id` INT UNSIGNED NOT NULL AUTO_INCREMENT,
  `finance_id` INT UNSIGNED NOT NULL,
  `name` VARCHAR(255) NOT NULL,
  `address` TEXT NULL,
  `contact` VARCHAR(64) NULL,
  `uploaded_at` DATETIME NULL,
  `total_records` BIGINT UNSIGNED NOT NULL DEFAULT 0,
  `is_active` TINYINT(1) NOT NULL DEFAULT 1,
  `created_at` TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
  `updated_at` TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  PRIMARY KEY (`id`),
  KEY `idx_branches_finance` (`finance_id`),
  KEY `idx_branches_uploaded_at` (`uploaded_at`),
  KEY `idx_branches_total_records` (`total_records`),
  CONSTRAINT `fk_branches_finances` FOREIGN KEY (`finance_id`) REFERENCES `finances` (`id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;

-- Fulltext index for branch name search
ALTER TABLE `branches` ADD FULLTEXT KEY `ft_branches_name` (`name`);

-- Composite index to quickly find branches by finance and name (common lookup)
CREATE INDEX `idx_branches_finance_name` ON `branches` (`finance_id`, `name`(100));

-- Sample stored procedure to create a finance and return its id
DROP PROCEDURE IF EXISTS `sp_create_finance`;
DELIMITER $$
CREATE PROCEDURE `sp_create_finance` (
  IN p_name VARCHAR(255),
  IN p_description TEXT,
  OUT p_id INT
)
BEGIN
  INSERT INTO `finances` (`name`, `description`) VALUES (p_name, p_description);
  SET p_id = LAST_INSERT_ID();
END $$
DELIMITER ;

-- Sample stored procedure to create a branch under a finance (transactional)
DROP PROCEDURE IF EXISTS `sp_create_branch`;
DELIMITER $$
CREATE PROCEDURE `sp_create_branch` (
  IN p_finance_id INT,
  IN p_name VARCHAR(255),
  IN p_address TEXT,
  IN p_contact VARCHAR(64),
  OUT p_id INT
)
BEGIN
  START TRANSACTION;
  INSERT INTO `branches` (`finance_id`, `name`, `address`, `contact`) VALUES (p_finance_id, p_name, p_address, p_contact);
  SET p_id = LAST_INSERT_ID();
  COMMIT;
END $$
DELIMITER ;

-- Helpful views for quick UI queries
DROP VIEW IF EXISTS `v_finance_summary`;
CREATE VIEW `v_finance_summary` AS
SELECT f.id AS finance_id, f.name AS finance_name, f.description, f.created_at,
       COALESCE(b.branch_count,0) AS branch_count,
       COALESCE(b.total_records_sum,0) AS total_records
FROM finances f
LEFT JOIN (
  SELECT finance_id, COUNT(*) AS branch_count, SUM(total_records) AS total_records_sum
  FROM branches
  WHERE is_active = 1
  GROUP BY finance_id
) b ON b.finance_id = f.id;

-- Recommended maintenance notes (run as DBA):
-- 1) Ensure innodb_buffer_pool_size is set large enough for working set.
-- 2) Run ANALYZE TABLE and OPTIMIZE TABLE after bulk imports.
-- 3) Create appropriate secondary indexes based on actual query patterns.
-- 4) Consider partitioning branches by finance_id for extremely large branches tables.
