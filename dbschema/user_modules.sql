ALTER TABLE `app_users`
  ADD COLUMN IF NOT EXISTS `modules_override` TEXT NULL AFTER `role_id`;
