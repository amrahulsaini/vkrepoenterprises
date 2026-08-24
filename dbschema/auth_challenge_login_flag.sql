-- Guards the attendance mark against the desktop's polling.
--
-- The desktop polls the challenge every two seconds until it is approved, so
-- the approval is observed many times. Recording a sign-in from that poll
-- without a guard would count one arrival as a dozen. Claiming this flag is a
-- conditional UPDATE, so exactly one poll wins and records the sign-in.
--
-- Idempotent: safe to re-run.

ALTER TABLE `auth_challenges`
  ADD COLUMN IF NOT EXISTS `login_recorded` TINYINT(1) NOT NULL DEFAULT 0 AFTER `resolved_at`;
