-- Vehicle Records: one row per vehicle per branch upload.
-- When a new Excel is uploaded for a branch, all existing rows for that
-- branch_id are deleted first, then new rows inserted (overwrite semantics).
-- branches.total_records and branches.uploaded_at are updated after each upload.

CREATE TABLE IF NOT EXISTS vehicle_records (
  id               BIGINT       PRIMARY KEY AUTO_INCREMENT,
  branch_id        INT          NOT NULL,

  -- Core vehicle identity
  vehicle_no       VARCHAR(50)  NULL COMMENT 'formatted, e.g. MH-12-AB-1234',
  chassis_no       VARCHAR(100) NULL,
  engine_no        VARCHAR(100) NULL,
  model            VARCHAR(200) NULL,

  -- Finance / agreement
  agreement_no     VARCHAR(100) NULL,
  bucket           VARCHAR(50)  NULL,
  gv               VARCHAR(50)  NULL,
  od               VARCHAR(50)  NULL,
  seasoning        VARCHAR(50)  NULL,
  tbr_flag         VARCHAR(20)  NULL,
  sec9_available   VARCHAR(20)  NULL,
  sec17_available  VARCHAR(20)  NULL,

  -- Customer / owner
  customer_name    VARCHAR(200) NULL,
  customer_address TEXT         NULL,
  customer_contact VARCHAR(100) NULL,
  owner_name       VARCHAR(200) NULL,
  mobile_no        VARCHAR(50)  NULL,

  -- Location
  region           VARCHAR(100) NULL,
  area             VARCHAR(100) NULL,
  branch_name_raw  VARCHAR(200) NULL COMMENT 'branch name from Excel column, not FK',

  -- Recovery agents / levels
  level1           VARCHAR(200) NULL,
  level1_contact   VARCHAR(100) NULL,
  level2           VARCHAR(200) NULL,
  level2_contact   VARCHAR(100) NULL,
  level3           VARCHAR(200) NULL,
  level3_contact   VARCHAR(100) NULL,
  level4           VARCHAR(200) NULL,
  level4_contact   VARCHAR(100) NULL,

  -- Communication
  sender_mail1     VARCHAR(200) NULL,
  sender_mail2     VARCHAR(200) NULL,
  executive_name   VARCHAR(200) NULL,
  pos              VARCHAR(100) NULL,
  toss             VARCHAR(100) NULL,
  remark           TEXT         NULL,

  created_at       TIMESTAMP    DEFAULT CURRENT_TIMESTAMP,

  INDEX  idx_branch      (branch_id),
  INDEX  idx_vehicle_no  (vehicle_no),
  INDEX  idx_chassis_no  (chassis_no),

  FOREIGN KEY (branch_id) REFERENCES branches(id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
