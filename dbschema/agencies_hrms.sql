-- HRMS per-agency opt-in.
--
-- HRMS is not part of the base product: an agency only gets it once an
-- administrator turns it on from the manage portal. The flag lives on
-- `agencies` (crm_master) rather than in the tenant DB because it is a
-- commercial entitlement decided by CRMRS, not agency-editable data.
--
-- hrms_enabled_at records when it was granted, so "since when" is answerable
-- without an audit table.
--
-- Idempotent: safe to re-run (MariaDB 10.11 supports IF NOT EXISTS here).

ALTER TABLE `agencies`
  ADD COLUMN IF NOT EXISTS `hrms_enabled` TINYINT(1) NOT NULL DEFAULT 0 AFTER `status`,
  ADD COLUMN IF NOT EXISTS `hrms_enabled_at` TIMESTAMP NULL DEFAULT NULL AFTER `hrms_enabled`;
