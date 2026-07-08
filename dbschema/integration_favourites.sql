USE crm_master;

CREATE TABLE IF NOT EXISTS integration_favourite_branches (
  integration_account_id INT          NOT NULL,
  agency_id              INT          NOT NULL,
  finance_id             INT UNSIGNED NOT NULL,
  branch_id              INT UNSIGNED NOT NULL,
  created_at             TIMESTAMP    NOT NULL DEFAULT CURRENT_TIMESTAMP,
  PRIMARY KEY (integration_account_id, agency_id, branch_id),
  KEY idx_ifb_account (integration_account_id),
  CONSTRAINT fk_ifb_account FOREIGN KEY (integration_account_id) REFERENCES integration_accounts(id) ON DELETE CASCADE,
  CONSTRAINT fk_ifb_agency  FOREIGN KEY (agency_id)              REFERENCES agencies(id)              ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
