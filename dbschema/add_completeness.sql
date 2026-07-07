ALTER TABLE vehicle_records DROP INDEX IF EXISTS idx_vehicle_best;
ALTER TABLE vehicle_records DROP INDEX IF EXISTS idx_chassis_best;
ALTER TABLE vehicle_records DROP COLUMN IF EXISTS completeness;

ALTER TABLE vehicle_records
  ADD COLUMN completeness TINYINT UNSIGNED
  GENERATED ALWAYS AS (
      (NULLIF(TRIM(vehicle_no),'')       IS NOT NULL) +
      (NULLIF(TRIM(chassis_no),'')       IS NOT NULL) +
      (NULLIF(TRIM(engine_no),'')        IS NOT NULL) +
      (NULLIF(TRIM(model),'')            IS NOT NULL) +
      (NULLIF(TRIM(agreement_no),'')     IS NOT NULL) +
      (NULLIF(TRIM(customer_name),'')    IS NOT NULL) +
      (NULLIF(TRIM(customer_contact),'') IS NOT NULL) +
      (NULLIF(TRIM(customer_address),'') IS NOT NULL) +
      (NULLIF(TRIM(owner_name),'')       IS NOT NULL) +
      (NULLIF(TRIM(mobile_no),'')        IS NOT NULL) +
      (NULLIF(TRIM(region),'')           IS NOT NULL) +
      (NULLIF(TRIM(area),'')             IS NOT NULL) +
      (NULLIF(TRIM(bucket),'')           IS NOT NULL) +
      (NULLIF(TRIM(gv),'')               IS NOT NULL) +
      (NULLIF(TRIM(od),'')               IS NOT NULL) +
      (NULLIF(TRIM(seasoning),'')        IS NOT NULL) +
      (NULLIF(TRIM(tbr_flag),'')         IS NOT NULL) +
      (NULLIF(TRIM(sec9_available),'')   IS NOT NULL) +
      (NULLIF(TRIM(sec17_available),'')  IS NOT NULL) +
      (NULLIF(TRIM(level1),'')           IS NOT NULL) +
      (NULLIF(TRIM(level1_contact),'')   IS NOT NULL) +
      (NULLIF(TRIM(level2),'')           IS NOT NULL) +
      (NULLIF(TRIM(level2_contact),'')   IS NOT NULL) +
      (NULLIF(TRIM(level3),'')           IS NOT NULL) +
      (NULLIF(TRIM(level3_contact),'')   IS NOT NULL) +
      (NULLIF(TRIM(level4),'')           IS NOT NULL) +
      (NULLIF(TRIM(level4_contact),'')   IS NOT NULL) +
      (NULLIF(TRIM(sender_mail1),'')     IS NOT NULL) +
      (NULLIF(TRIM(sender_mail2),'')     IS NOT NULL) +
      (NULLIF(TRIM(executive_name),'')   IS NOT NULL) +
      (NULLIF(TRIM(pos),'')              IS NOT NULL) +
      (NULLIF(TRIM(toss),'')             IS NOT NULL) +
      (NULLIF(TRIM(remark),'')           IS NOT NULL)
  ) STORED;

ALTER TABLE vehicle_records ADD INDEX idx_vehicle_best (vehicle_no, completeness, id);
ALTER TABLE vehicle_records ADD INDEX idx_chassis_best (chassis_no, completeness, id);
