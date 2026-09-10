-- ─────────────────────────────────────────────────────────────────────────
--  Rate-list audience + courier inventory remark. Runs on every tenant DB.
-- ─────────────────────────────────────────────────────────────────────────

-- rate_list_users — which app users may see a rate-list entry. An entry with
-- no rows here is visible to nobody, so the office must pick its audience.
CREATE TABLE IF NOT EXISTS rate_list_users (
    rate_list_id BIGINT NOT NULL,
    user_id      BIGINT NOT NULL,
    PRIMARY KEY (rate_list_id, user_id),
    INDEX idx_rlu_user (user_id),
    CONSTRAINT fk_rlu_list FOREIGN KEY (rate_list_id)
        REFERENCES rate_lists(id) ON DELETE CASCADE,
    CONSTRAINT fk_rlu_user FOREIGN KEY (user_id)
        REFERENCES app_users(id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

ALTER TABLE repo_submissions
  ADD COLUMN IF NOT EXISTS inventory_remark TEXT NULL AFTER courier_yn;
