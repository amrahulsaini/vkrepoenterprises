
ALTER TABLE `agencies`
  ADD COLUMN IF NOT EXISTS `geo_lat`      DECIMAL(10,7) NULL AFTER `qr_proximity`,
  ADD COLUMN IF NOT EXISTS `geo_lng`      DECIMAL(10,7) NULL AFTER `geo_lat`,
  ADD COLUMN IF NOT EXISTS `geo_radius_m` INT NOT NULL DEFAULT 200 AFTER `geo_lng`,
  ADD COLUMN IF NOT EXISTS `geo_label`    VARCHAR(190) NOT NULL DEFAULT '' AFTER `geo_radius_m`;

ALTER TABLE `auth_challenges`
  ADD COLUMN IF NOT EXISTS `phone_lat`  DECIMAL(10,7) NULL AFTER `phone_ip`,
  ADD COLUMN IF NOT EXISTS `phone_lng`  DECIMAL(10,7) NULL AFTER `phone_lat`,
  ADD COLUMN IF NOT EXISTS `phone_acc`  INT NULL AFTER `phone_lng`,
  ADD COLUMN IF NOT EXISTS `distance_m` INT NULL AFTER `phone_acc`,
  ADD COLUMN IF NOT EXISTS `mock_gps`   TINYINT(1) NOT NULL DEFAULT 0 AFTER `distance_m`;
