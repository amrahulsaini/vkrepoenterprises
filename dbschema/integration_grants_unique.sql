USE crm_master;

ALTER TABLE agency_integration_grants DROP INDEX IF EXISTS uq_agency_finance;
ALTER TABLE agency_integration_grants DROP INDEX IF EXISTS uq_agency_account;
ALTER TABLE agency_integration_grants ADD UNIQUE KEY uq_agency_finance (agency_id, finance_id);
ALTER TABLE agency_integration_grants ADD UNIQUE KEY uq_agency_account (agency_id, integration_account_id);
