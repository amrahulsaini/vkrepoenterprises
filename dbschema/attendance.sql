CREATE TABLE IF NOT EXISTS attendance (
  id          BIGINT AUTO_INCREMENT PRIMARY KEY,
  user_id     BIGINT       NOT NULL,
  work_date   DATE         NOT NULL,
  marked_at   DATETIME     NOT NULL,
  status      VARCHAR(16)  NOT NULL DEFAULT 'present',
  source      VARCHAR(16)  NOT NULL DEFAULT 'hrms',
  lat         DOUBLE       NULL,
  lng         DOUBLE       NULL,
  location    VARCHAR(255) NULL,
  note        VARCHAR(255) NULL,
  marked_by   VARCHAR(190) NULL,
  UNIQUE KEY uq_att_user_day (user_id, work_date),
  INDEX idx_att_day (work_date),
  INDEX idx_att_user (user_id),
  CONSTRAINT fk_att_user FOREIGN KEY (user_id) REFERENCES app_users(id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
