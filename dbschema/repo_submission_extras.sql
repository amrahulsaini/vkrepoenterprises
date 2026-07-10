ALTER TABLE `repo_submissions`
  ADD COLUMN IF NOT EXISTS `collection_update` VARCHAR(512) NULL AFTER `executive_name`,
  ADD COLUMN IF NOT EXISTS `remark`            VARCHAR(512) NULL AFTER `collection_update`;

ALTER TABLE `billing_member_finances`
  ADD UNIQUE KEY IF NOT EXISTS `uq_bmf_finance` (`finance_id`);
