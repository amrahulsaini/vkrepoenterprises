ALTER TABLE `app_users`
  ADD COLUMN IF NOT EXISTS `face_template` BLOB NULL AFTER `profile_password_by`,
  ADD COLUMN IF NOT EXISTS `face_thumb` MEDIUMTEXT NULL AFTER `face_template`,
  ADD COLUMN IF NOT EXISTS `face_enrolled_at` DATETIME NULL AFTER `face_thumb`,
  ADD COLUMN IF NOT EXISTS `face_enrolled_by` VARCHAR(190) NULL AFTER `face_enrolled_at`;
