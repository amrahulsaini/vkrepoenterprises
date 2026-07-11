ALTER TABLE `app_users`
  ADD COLUMN IF NOT EXISTS `billing_demand` INT NULL,
  ADD COLUMN IF NOT EXISTS `billing_target` INT NULL;

ALTER TABLE `repo_submissions`
  ADD COLUMN IF NOT EXISTS `submitted_by_user_id` BIGINT NULL;

ALTER TABLE `repo_submissions`
  ADD INDEX IF NOT EXISTS `idx_repo_submitter` (`submitted_by_user_id`);
