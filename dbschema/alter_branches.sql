-- Alter script: extend branches table to store multiple optional contact fields and metadata
USE `vkre_db1`;

-- Add additional optional columns if they don't exist
ALTER TABLE `branches`
  ADD COLUMN IF NOT EXISTS `contact1` VARCHAR(64) NULL,
  ADD COLUMN IF NOT EXISTS `contact2` VARCHAR(64) NULL,
  ADD COLUMN IF NOT EXISTS `contact3` VARCHAR(64) NULL,
  ADD COLUMN IF NOT EXISTS `city` VARCHAR(128) NULL,
  ADD COLUMN IF NOT EXISTS `state` VARCHAR(128) NULL,
  ADD COLUMN IF NOT EXISTS `postal_code` VARCHAR(32) NULL,
  ADD COLUMN IF NOT EXISTS `branch_code` VARCHAR(64) NULL,
  ADD COLUMN IF NOT EXISTS `notes` TEXT NULL;

-- If there is an existing `contact` column, preserve it by copying into contact1 (one-time manual step)
-- UPDATE branches SET contact1 = contact WHERE contact1 IS NULL AND contact IS NOT NULL;

-- Optional: create indexes on commonly queried columns
CREATE INDEX IF NOT EXISTS `idx_branches_branch_code` ON `branches` (`branch_code`(50));
CREATE INDEX IF NOT EXISTS `idx_branches_city` ON `branches` (`city`(50));

-- End of alters
