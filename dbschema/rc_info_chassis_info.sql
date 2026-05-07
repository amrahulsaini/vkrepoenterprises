-- ─────────────────────────────────────────────────────────────────────────────
--  rc_info  — fast lookup by last 4 chars of RC/vehicle number
--  chassis_info — fast lookup by last 5 chars of chassis number
--  Populated automatically by RecordsRepository after every bulk upload.
--  Rows are cascade-deleted when vehicle_records rows are deleted.
-- ─────────────────────────────────────────────────────────────────────────────

CREATE TABLE IF NOT EXISTS rc_info (
    id                BIGINT       NOT NULL AUTO_INCREMENT,
    vehicle_record_id BIGINT       NOT NULL,
    rc_number         VARCHAR(50)  NOT NULL DEFAULT '',
    model             VARCHAR(200) NOT NULL DEFAULT '',
    last4             CHAR(4)      NOT NULL DEFAULT '',
    PRIMARY KEY (id),
    INDEX  idx_last4   (last4),
    INDEX  idx_rc      (rc_number),
    INDEX  idx_vr      (vehicle_record_id),
    CONSTRAINT fk_rc_vr
        FOREIGN KEY (vehicle_record_id)
        REFERENCES  vehicle_records(id)
        ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE IF NOT EXISTS chassis_info (
    id                BIGINT       NOT NULL AUTO_INCREMENT,
    vehicle_record_id BIGINT       NOT NULL,
    chassis_number    VARCHAR(100) NOT NULL DEFAULT '',
    model             VARCHAR(200) NOT NULL DEFAULT '',
    last5             CHAR(5)      NOT NULL DEFAULT '',
    PRIMARY KEY (id),
    INDEX  idx_last5   (last5),
    INDEX  idx_chassis (chassis_number),
    INDEX  idx_vr      (vehicle_record_id),
    CONSTRAINT fk_ch_vr
        FOREIGN KEY (vehicle_record_id)
        REFERENCES  vehicle_records(id)
        ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

-- ─────────────────────────────────────────────────────────────────────────────
--  One-time back-fill from existing vehicle_records (run once after table create)
-- ─────────────────────────────────────────────────────────────────────────────
-- INSERT IGNORE INTO rc_info (vehicle_record_id, rc_number, model, last4)
-- SELECT id, vehicle_no, COALESCE(model,''), RIGHT(vehicle_no, 4)
-- FROM vehicle_records WHERE vehicle_no IS NOT NULL AND vehicle_no != '';
--
-- INSERT IGNORE INTO chassis_info (vehicle_record_id, chassis_number, model, last5)
-- SELECT id, chassis_no, COALESCE(model,''), RIGHT(chassis_no, 5)
-- FROM vehicle_records WHERE chassis_no IS NOT NULL AND chassis_no != '';
