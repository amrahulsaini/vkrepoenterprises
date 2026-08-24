ALTER TABLE `auth_challenges`
  ADD COLUMN IF NOT EXISTS `claim_user_id` BIGINT      NULL     AFTER `device_label`,
  ADD COLUMN IF NOT EXISTS `claim_mobile`  VARCHAR(20) NOT NULL DEFAULT '' AFTER `claim_user_id`,
  ADD COLUMN IF NOT EXISTS `desktop_ip`    VARCHAR(45) NOT NULL DEFAULT '' AFTER `claim_mobile`,
  ADD COLUMN IF NOT EXISTS `phone_ip`      VARCHAR(45) NOT NULL DEFAULT '' AFTER `desktop_ip`,
  ADD COLUMN IF NOT EXISTS `proximity`     ENUM('unknown','match','mismatch')
                                           NOT NULL DEFAULT 'unknown' AFTER `phone_ip`;

ALTER TABLE `agencies`
  ADD COLUMN IF NOT EXISTS `qr_proximity` ENUM('off','warn','block')
                                          NOT NULL DEFAULT 'warn' AFTER `hrms_enabled_at`;
