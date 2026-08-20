ALTER TABLE `app_users`
  ADD COLUMN IF NOT EXISTS `profile_password_hash` VARCHAR(255) NULL AFTER `admin_pass`,
  ADD COLUMN IF NOT EXISTS `profile_password_set_at` DATETIME NULL AFTER `profile_password_hash`,
  ADD COLUMN IF NOT EXISTS `profile_password_by` VARCHAR(190) NULL AFTER `profile_password_set_at`;
