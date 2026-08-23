-- Ties a desktop QR sign-in to one person, in one place.
--
-- Before this, an auth_challenge said nothing about WHO the desktop was
-- expecting, so any enrolled staff member of the agency could approve any
-- desktop's QR and the desktop signed in as them. It also said nothing about
-- WHERE the phone was, so a screenshot of the QR sent to someone at home
-- unlocked the office machine.
--
-- claim_user_id  — the person the desktop asked for (from the mobile number
--                  typed on the sign-in screen). Approval by anyone else is
--                  refused outright; this is not a policy, it is correctness.
-- desktop_ip     — public IP the desktop created the challenge from.
-- phone_ip       — public IP the phone approved from.
-- proximity      — the verdict, recorded even when policy is 'warn' so an
--                  agency can see what enforcing would have blocked.
--
-- agencies.qr_proximity is the per-agency policy. 'warn' is the default
-- because a phone on mobile data rather than the office wifi leaves through a
-- different IP and would otherwise be locked out on day one.
--
-- Idempotent: safe to re-run.

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
