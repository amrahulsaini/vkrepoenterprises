DROP TABLE IF EXISTS hrms_sessions, auth_challenges;

ALTER TABLE `agencies`
  DROP COLUMN IF EXISTS `hrms_enabled`,
  DROP COLUMN IF EXISTS `hrms_enabled_at`,
  DROP COLUMN IF EXISTS `qr_proximity`,
  DROP COLUMN IF EXISTS `geo_lat`,
  DROP COLUMN IF EXISTS `geo_lng`,
  DROP COLUMN IF EXISTS `geo_radius_m`,
  DROP COLUMN IF EXISTS `geo_label`;

DELETE FROM agency_otps WHERE purpose = 'hrms';
