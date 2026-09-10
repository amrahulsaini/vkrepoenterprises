-- ─────────────────────────────────────────────────────────────────────────
--  Authorization-letter stamp — one signed-and-sealed image per agency,
--  placed at a fixed spot on every letter the agency generates.
--  Coordinates are PDF points on the A4 page the generator draws (595 x 842,
--  origin top-left), so they mean the same thing on every device.
--  Runs on crm_master, NOT on a tenant DB.
-- ─────────────────────────────────────────────────────────────────────────
ALTER TABLE agencies
    ADD COLUMN stamp_path VARCHAR(512) DEFAULT NULL,
    ADD COLUMN stamp_x    FLOAT NOT NULL DEFAULT 380,
    ADD COLUMN stamp_y    FLOAT NOT NULL DEFAULT 690,
    ADD COLUMN stamp_w    FLOAT NOT NULL DEFAULT 150,
    ADD COLUMN stamp_h    FLOAT NOT NULL DEFAULT 80;
