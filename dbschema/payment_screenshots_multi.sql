ALTER TABLE repo_submissions
  ADD COLUMN IF NOT EXISTS payment_screenshots TEXT NULL AFTER payment_screenshot;
