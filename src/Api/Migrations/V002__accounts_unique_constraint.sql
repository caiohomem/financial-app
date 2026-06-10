ALTER TABLE accounts
    ADD CONSTRAINT uq_accounts_source_identifier UNIQUE (source, name);
