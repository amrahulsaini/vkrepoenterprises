ALTER TABLE `billing_settings`
  ADD COLUMN `sign_cert_path`     VARCHAR(512) NULL AFTER `background_path`,
  ADD COLUMN `sign_cert_password` VARCHAR(255) NULL AFTER `sign_cert_path`,
  ADD COLUMN `signer_name`        VARCHAR(255) NULL AFTER `sign_cert_password`,
  ADD COLUMN `signer_reason`      VARCHAR(255) NULL AFTER `signer_name`,
  ADD COLUMN `signer_location`    VARCHAR(255) NULL AFTER `signer_reason`;
