-- The area an agency's staff may approve a desktop sign-in from.
--
-- One circle per agency, applying to every profile: a centre the agency sets
-- from HRMS and a radius in metres. When a phone approves a desktop QR it
-- sends its GPS; the server measures the distance from this centre and refuses
-- anything outside. That is what stops someone photographing the QR and having
-- a friend approve it from home.
--
-- geo_radius_m defaults to 200 because indoor GPS commonly drifts 20-50 m; a
-- tighter circle rejects people who are genuinely at their desk.
--
-- qr_proximity (added in auth_challenge_proximity.sql) is reused as the policy
-- for this check: off / warn (record only) / block.
--
-- Idempotent: safe to re-run.

ALTER TABLE `agencies`
  ADD COLUMN IF NOT EXISTS `geo_lat`      DECIMAL(10,7) NULL AFTER `qr_proximity`,
  ADD COLUMN IF NOT EXISTS `geo_lng`      DECIMAL(10,7) NULL AFTER `geo_lat`,
  ADD COLUMN IF NOT EXISTS `geo_radius_m` INT NOT NULL DEFAULT 200 AFTER `geo_lng`,
  ADD COLUMN IF NOT EXISTS `geo_label`    VARCHAR(190) NOT NULL DEFAULT '' AFTER `geo_radius_m`;

-- Where the phone actually was, kept even when the policy is 'warn' so an
-- agency can see what enforcing would have refused.
ALTER TABLE `auth_challenges`
  ADD COLUMN IF NOT EXISTS `phone_lat`  DECIMAL(10,7) NULL AFTER `phone_ip`,
  ADD COLUMN IF NOT EXISTS `phone_lng`  DECIMAL(10,7) NULL AFTER `phone_lat`,
  ADD COLUMN IF NOT EXISTS `phone_acc`  INT NULL AFTER `phone_lng`,
  ADD COLUMN IF NOT EXISTS `distance_m` INT NULL AFTER `phone_acc`,
  ADD COLUMN IF NOT EXISTS `mock_gps`   TINYINT(1) NOT NULL DEFAULT 0 AFTER `distance_m`;
