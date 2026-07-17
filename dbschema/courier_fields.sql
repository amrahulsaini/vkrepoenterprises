ALTER TABLE repo_submissions
  ADD COLUMN IF NOT EXISTS repo_charges   DECIMAL(12,2) NULL AFTER remark,
  ADD COLUMN IF NOT EXISTS advance        DECIMAL(12,2) NULL AFTER repo_charges,
  ADD COLUMN IF NOT EXISTS courier_yn     VARCHAR(3)    NULL AFTER advance,
  ADD COLUMN IF NOT EXISTS banker_address TEXT          NULL AFTER courier_yn,
  ADD COLUMN IF NOT EXISTS pod_number     VARCHAR(128)  NULL AFTER banker_address,
  ADD COLUMN IF NOT EXISTS courier_updated_at DATETIME  NULL AFTER pod_number;
