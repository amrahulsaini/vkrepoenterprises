-- Run this on your server MySQL to enable device-change requests and live-user tracking.
-- mysql -u vkre_db1 -p'db1' vkre_db1 < live_users_migration.sql

-- 1. Device change requests (one per user, latest wins)
CREATE TABLE IF NOT EXISTS device_change_requests (
    id           BIGINT PRIMARY KEY AUTO_INCREMENT,
    user_id      BIGINT       NOT NULL,
    user_name    VARCHAR(150) NOT NULL DEFAULT '',
    user_mobile  VARCHAR(15)  NOT NULL DEFAULT '',
    new_device_id VARCHAR(500) NOT NULL DEFAULT '',
    requested_at TIMESTAMP    NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    UNIQUE KEY uq_user (user_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

-- 2. Live location / last-seen on app_users
ALTER TABLE app_users
    ADD COLUMN IF NOT EXISTS last_seen TIMESTAMP NULL,
    ADD COLUMN IF NOT EXISTS last_lat  DECIMAL(10,8) NULL,
    ADD COLUMN IF NOT EXISTS last_lng  DECIMAL(11,8) NULL;
