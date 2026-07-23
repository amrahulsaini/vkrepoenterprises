-- ─────────────────────────────────────────────────────────────────────────
--  repo_kits — PDF "repo kits" an admin uploads for a head office (finance).
--  Field agents search a head office by name and download its kit(s).
--  Lives in each tenant DB (+ tenant_template.sql for new agencies).
-- ─────────────────────────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS repo_kits (
    id          BIGINT       NOT NULL AUTO_INCREMENT,
    finance_id  INT          NOT NULL,
    title       VARCHAR(200)          DEFAULT NULL,
    file_path   VARCHAR(255) NOT NULL,
    file_name   VARCHAR(200)          DEFAULT NULL,
    uploaded_at TIMESTAMP    NOT NULL DEFAULT CURRENT_TIMESTAMP,
    PRIMARY KEY (id),
    INDEX idx_rk_finance (finance_id),
    CONSTRAINT fk_rk_finance FOREIGN KEY (finance_id)
        REFERENCES finances(id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
