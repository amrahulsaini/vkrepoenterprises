-- ─────────────────────────────────────────────────────────────────────────
--  V.Update can carry several payment screenshots. payment_screenshot keeps
--  the first (older clients and exports read it); payment_screenshots holds
--  every one as a JSON array of upload paths. Runs on every tenant DB.
-- ─────────────────────────────────────────────────────────────────────────
ALTER TABLE repo_submissions
  ADD COLUMN IF NOT EXISTS payment_screenshots TEXT NULL AFTER payment_screenshot;
