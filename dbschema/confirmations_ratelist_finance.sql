ALTER TABLE confirm_captures
    MODIFY COLUMN image_path VARCHAR(255) DEFAULT NULL;
ALTER TABLE confirm_captures
    ADD COLUMN action_type    VARCHAR(32)  NOT NULL DEFAULT 'confirm' AFTER chassis_no,
    ADD COLUMN channel        VARCHAR(16)  NOT NULL DEFAULT 'whatsapp' AFTER action_type,
    ADD COLUMN customer_name  VARCHAR(190) DEFAULT NULL AFTER channel,
    ADD COLUMN model          VARCHAR(190) DEFAULT NULL AFTER customer_name,
    ADD COLUMN engine_no      VARCHAR(64)  DEFAULT NULL AFTER model,
    ADD COLUMN agreement_no   VARCHAR(64)  DEFAULT NULL AFTER engine_no,
    ADD COLUMN financer       VARCHAR(190) DEFAULT NULL AFTER agreement_no,
    ADD COLUMN address        VARCHAR(255) DEFAULT NULL AFTER financer,
    ADD COLUMN map_link       VARCHAR(255) DEFAULT NULL AFTER address,
    ADD COLUMN load_details   VARCHAR(190) DEFAULT NULL AFTER map_link,
    ADD COLUMN message_text   TEXT         DEFAULT NULL AFTER load_details,
    ADD INDEX idx_cc_captured (captured_at);

ALTER TABLE app_users
    ADD COLUMN show_finance_name TINYINT(1) NOT NULL DEFAULT 0;

CREATE TABLE IF NOT EXISTS rate_lists (
    id          BIGINT       NOT NULL AUTO_INCREMENT,
    title       VARCHAR(200) NOT NULL,
    kind        VARCHAR(8)   NOT NULL DEFAULT 'file',
    url         VARCHAR(500)          DEFAULT NULL,
    file_path   VARCHAR(255)          DEFAULT NULL,
    file_name   VARCHAR(200)          DEFAULT NULL,
    file_size   BIGINT       NOT NULL DEFAULT 0,
    mime        VARCHAR(120)          DEFAULT NULL,
    finance_id  INT UNSIGNED          DEFAULT NULL,
    notes       VARCHAR(500)          DEFAULT NULL,
    created_at  TIMESTAMP    NOT NULL DEFAULT CURRENT_TIMESTAMP,
    PRIMARY KEY (id),
    INDEX idx_rl_finance (finance_id),
    INDEX idx_rl_created (created_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
