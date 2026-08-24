
ALTER TABLE `auth_challenges`
  ADD COLUMN IF NOT EXISTS `login_recorded` TINYINT(1) NOT NULL DEFAULT 0 AFTER `resolved_at`;
