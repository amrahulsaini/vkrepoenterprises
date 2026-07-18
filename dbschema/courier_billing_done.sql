ALTER TABLE repo_submissions
  ADD COLUMN IF NOT EXISTS invoice_no VARCHAR(64)  NULL AFTER pod_number,
  ADD COLUMN IF NOT EXISTS bill_file  VARCHAR(255) NULL AFTER invoice_no;
