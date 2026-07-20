-- Ties a session to its agency so deleting the agency clears its sessions.
-- Run against crm_master. Safe to re-run: it stops if the key already exists.
SET @have_fk := (
  SELECT COUNT(*) FROM information_schema.TABLE_CONSTRAINTS
   WHERE CONSTRAINT_SCHEMA = DATABASE()
     AND TABLE_NAME = 'desktop_sessions'
     AND CONSTRAINT_NAME = 'fk_desktop_sessions_agency'
);

SET @sql := IF(@have_fk = 0,
  'ALTER TABLE desktop_sessions
     ADD CONSTRAINT fk_desktop_sessions_agency
     FOREIGN KEY (agency_id) REFERENCES agencies(id) ON DELETE CASCADE',
  'SELECT "fk_desktop_sessions_agency already present" AS note');

PREPARE stmt FROM @sql;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;
