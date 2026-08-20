-- HRMS portal sessions.
--
-- The HRMS portal at /hrms/<slug> signs in with an OTP mailed to the agency's
-- registered primary email, so it needs its own session store. It deliberately
-- does NOT reuse the agency desktop token (AgencyToken): that token unlocks the
-- whole agency API, and an HRMS sign-in should only unlock HRMS.
--
-- Idempotent: safe to re-run.

CREATE TABLE IF NOT EXISTS hrms_sessions (
    id         BIGINT AUTO_INCREMENT PRIMARY KEY,
    agency_id  INT          NOT NULL,
    token_hash CHAR(64)     NOT NULL,
    expires_at DATETIME     NOT NULL,
    revoked    TINYINT(1)   NOT NULL DEFAULT 0,
    created_at TIMESTAMP    NOT NULL DEFAULT CURRENT_TIMESTAMP,
    UNIQUE KEY uq_hrms_token (token_hash),
    INDEX idx_hrms_agency (agency_id),
    CONSTRAINT fk_hrms_agency FOREIGN KEY (agency_id) REFERENCES agencies(id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
