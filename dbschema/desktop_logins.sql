-- Every desktop sign-in, and the attendance mark that follows from it.
--
-- Attendance was a button somebody in HRMS had to press. But signing in to the
-- desktop already proves a person turned up, so the first sign-in of the day
-- marks them present by itself. `source` separates the two: 'login' was earned
-- by signing in, 'hrms' was entered by hand.
--
-- Every sign-in is kept, not just the first, so the day reads as it happened:
-- how many times somebody signed in, when they first arrived and when they
-- last came back. work_date is stored alongside `at` so a day can be counted
-- without a function over the timestamp defeating the index.
--
-- Idempotent: safe to re-run.

CREATE TABLE IF NOT EXISTS desktop_logins (
    id           BIGINT AUTO_INCREMENT PRIMARY KEY,
    user_id      BIGINT       NOT NULL,
    at           DATETIME     NOT NULL,
    work_date    DATE         NOT NULL,
    method       ENUM('password','fingerprint') NOT NULL DEFAULT 'password',
    device_label VARCHAR(160) NOT NULL DEFAULT '',
    INDEX idx_dl_user_day (user_id, work_date),
    INDEX idx_dl_day (work_date),
    CONSTRAINT fk_dl_user FOREIGN KEY (user_id) REFERENCES app_users(id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
