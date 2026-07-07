-- ─────────────────────────────────────────────────────────────────────────
--  CRMS — INTEGRATION ACCOUNTS (finance-company portal)
--  Run on crm_master. Idempotent — safe to re-run.
--
--  integration_accounts       : one global account per finance company. They
--                               apply once at agency.crmrecoverysoftware.com/
--                               integrations and can be granted access to many
--                               agencies. Passwords are stored in plain text by
--                               explicit product decision.
--  agency_integration_grants  : the tick made on an agency's manage page — links
--                               one integration account to one of that agency's
--                               head offices (tenant finances.id) plus optional
--                               view/upload filter options.
-- ─────────────────────────────────────────────────────────────────────────

USE crm_master;

CREATE TABLE IF NOT EXISTS integration_accounts (
    id            INT AUTO_INCREMENT PRIMARY KEY,
    finance_name  VARCHAR(200) NOT NULL,
    email         VARCHAR(200) NOT NULL UNIQUE,
    password      VARCHAR(255) NOT NULL,
    status        ENUM('active','suspended') NOT NULL DEFAULT 'active',
    created_at    TIMESTAMP    NOT NULL DEFAULT CURRENT_TIMESTAMP,
    last_login_at TIMESTAMP    NULL,
    INDEX idx_status (status)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;

CREATE TABLE IF NOT EXISTS agency_integration_grants (
    id                     INT AUTO_INCREMENT PRIMARY KEY,
    agency_id              INT          NOT NULL,
    integration_account_id INT          NOT NULL,
    finance_id             INT UNSIGNED NOT NULL,
    finance_name           VARCHAR(255) NOT NULL,
    filters                TEXT         NULL,
    active                 TINYINT(1)   NOT NULL DEFAULT 1,
    created_at             TIMESTAMP    NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at             TIMESTAMP    NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    UNIQUE KEY uq_grant (agency_id, integration_account_id, finance_id),
    INDEX idx_account (integration_account_id),
    INDEX idx_agency  (agency_id),
    CONSTRAINT fk_grant_agency  FOREIGN KEY (agency_id)              REFERENCES agencies(id)             ON DELETE CASCADE,
    CONSTRAINT fk_grant_account FOREIGN KEY (integration_account_id) REFERENCES integration_accounts(id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
