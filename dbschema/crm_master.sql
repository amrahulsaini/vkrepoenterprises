-- ─────────────────────────────────────────────────────────────────────────
--  CRMS — MASTER DATABASE
--  Holds the central registry of agencies and email-OTP state used by the
--  agency.crmrecoverysoftware.com portal. Per-agency application data lives
--  in its own `crmr_<slug>` database (see dbschema/tenant_template.sql).
--  Run on the server as MySQL root via socket.
-- ─────────────────────────────────────────────────────────────────────────

CREATE DATABASE IF NOT EXISTS crm_master
    CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci;

USE crm_master;

-- ───── agencies — one row per agency that has registered ─────
CREATE TABLE IF NOT EXISTS agencies (
    id              INT AUTO_INCREMENT PRIMARY KEY,
    name            VARCHAR(200)  NOT NULL,
    slug            VARCHAR(64)   NOT NULL UNIQUE,          -- used in DB name crmr_<slug>
    mobile1         VARCHAR(20)   NOT NULL,
    mobile2         VARCHAR(20),
    address         TEXT,
    logo_path       VARCHAR(500),                            -- /uploads/agencies/<slug>.webp
    email1          VARCHAR(200)  NOT NULL UNIQUE,
    email2          VARCHAR(200),
    password_hash   VARCHAR(255)  NOT NULL,
    db_name         VARCHAR(80),                             -- set when approved (crmr_<slug>)
    db_user         VARCHAR(80),                             -- set when approved
    db_password_enc VARBINARY(512),                          -- encrypted at-rest
    status          ENUM('pending','approved','rejected','suspended')
                    NOT NULL DEFAULT 'pending',
    rejected_reason TEXT,
    created_at      TIMESTAMP    NOT NULL DEFAULT CURRENT_TIMESTAMP,
    approved_at     TIMESTAMP    NULL,
    last_login_at   TIMESTAMP    NULL,
    INDEX  idx_status (status),
    INDEX  idx_email1 (email1)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;

-- ───── agency_otps — short-lived OTP codes for email verification ─────
CREATE TABLE IF NOT EXISTS agency_otps (
    id         INT AUTO_INCREMENT PRIMARY KEY,
    email      VARCHAR(200) NOT NULL,
    code       VARCHAR(10)  NOT NULL,
    purpose    VARCHAR(50)  NOT NULL DEFAULT 'register',
    expires_at DATETIME     NOT NULL,
    consumed   TINYINT      NOT NULL DEFAULT 0,
    created_at TIMESTAMP    NOT NULL DEFAULT CURRENT_TIMESTAMP,
    INDEX idx_email_purpose (email, purpose, consumed)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;

-- ───── manage_sessions — opaque tokens for the /manage password gate ─────
CREATE TABLE IF NOT EXISTS manage_sessions (
    token      CHAR(64)     NOT NULL PRIMARY KEY,
    created_at TIMESTAMP    NOT NULL DEFAULT CURRENT_TIMESTAMP,
    expires_at DATETIME     NOT NULL,
    INDEX idx_expires (expires_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
