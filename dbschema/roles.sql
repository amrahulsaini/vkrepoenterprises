CREATE TABLE IF NOT EXISTS roles (
  id            INT AUTO_INCREMENT PRIMARY KEY,
  name          VARCHAR(80)  NOT NULL,
  is_superadmin TINYINT(1)   NOT NULL DEFAULT 0,
  modules       TEXT         NULL,
  created_at    DATETIME     NOT NULL,
  updated_at    DATETIME     NULL,
  UNIQUE KEY uq_role_name (name)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;

ALTER TABLE `app_users`
  ADD COLUMN IF NOT EXISTS `role_id` INT NULL AFTER `fingerprint_waived_until`;

INSERT INTO roles (name, is_superadmin, modules, created_at)
SELECT 'Super Admin', 1, '', NOW()
WHERE NOT EXISTS (SELECT 1 FROM roles WHERE is_superadmin = 1);
