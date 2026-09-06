ALTER TABLE `repo_submissions`
  ADD COLUMN IF NOT EXISTS `billing_remark`  VARCHAR(512) NULL AFTER `total_gross`,
  ADD COLUMN IF NOT EXISTS `accounts_remark` VARCHAR(512) NULL AFTER `payment_status`;
