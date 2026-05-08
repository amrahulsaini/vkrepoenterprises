-- KYC migration: run once on your MySQL server
-- Adds bank fields to app_users and creates user_kyc table

ALTER TABLE app_users
  ADD COLUMN IF NOT EXISTS account_number VARCHAR(30)  NULL,
  ADD COLUMN IF NOT EXISTS ifsc_code      VARCHAR(15)  NULL;

CREATE TABLE IF NOT EXISTS user_kyc (
  id             BIGINT       AUTO_INCREMENT PRIMARY KEY,
  user_id        BIGINT       NOT NULL,
  aadhaar_front  LONGTEXT     NULL,
  aadhaar_back   LONGTEXT     NULL,
  pan_front      LONGTEXT     NULL,
  created_at     TIMESTAMP    DEFAULT CURRENT_TIMESTAMP,
  updated_at     TIMESTAMP    DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  UNIQUE KEY uq_user (user_id),
  CONSTRAINT fk_kyc_user FOREIGN KEY (user_id) REFERENCES app_users(id) ON DELETE CASCADE
);
