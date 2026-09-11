CREATE TABLE IF NOT EXISTS yard_lists (
    id          BIGINT       NOT NULL AUTO_INCREMENT,
    title       VARCHAR(200) NOT NULL,
    kind        VARCHAR(8)   NOT NULL DEFAULT 'file',
    url         VARCHAR(500)          DEFAULT NULL,
    file_path   VARCHAR(255)          DEFAULT NULL,
    file_name   VARCHAR(200)          DEFAULT NULL,
    file_size   BIGINT       NOT NULL DEFAULT 0,
    mime        VARCHAR(120)          DEFAULT NULL,
    notes       VARCHAR(500)          DEFAULT NULL,
    created_at  TIMESTAMP    NOT NULL DEFAULT CURRENT_TIMESTAMP,
    PRIMARY KEY (id),
    INDEX idx_yl_created (created_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
