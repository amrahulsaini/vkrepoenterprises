-- Keep the existing repo_charges column as the Accounts seizing-charge amount.
-- Billing has a separate amount and must never overwrite the Accounts amount.
ALTER TABLE repo_submissions
  ADD COLUMN IF NOT EXISTS billing_repo_charges DECIMAL(12,2) NULL AFTER total_gross;

-- Recover the billing component from the recorded invoice total where available.
UPDATE repo_submissions
SET billing_repo_charges = total_gross - COALESCE(addl_charges_amount, 0)
WHERE billing_repo_charges IS NULL AND total_gross IS NOT NULL AND bill_status = 'billed';
