SET FOREIGN_KEY_CHECKS = 0;

DROP TABLE IF EXISTS
  hrms_payslips,
  hrms_payroll_runs,
  hrms_incentives,
  hrms_advances,
  hrms_salary_structure,
  hrms_documents,
  hrms_leave_requests,
  hrms_leave_balances,
  hrms_leave_types,
  hrms_holidays,
  hrms_employment,
  hrms_settings,
  attendance,
  desktop_logins,
  device_keys,
  roles;

SET FOREIGN_KEY_CHECKS = 1;

ALTER TABLE `app_users`
  DROP COLUMN IF EXISTS `profile_password_hash`,
  DROP COLUMN IF EXISTS `profile_password_set_at`,
  DROP COLUMN IF EXISTS `profile_password_by`,
  DROP COLUMN IF EXISTS `fingerprint_required`,
  DROP COLUMN IF EXISTS `fingerprint_waived_until`,
  DROP COLUMN IF EXISTS `role_id`,
  DROP COLUMN IF EXISTS `modules_override`;
