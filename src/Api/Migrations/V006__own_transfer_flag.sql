ALTER TABLE transactions
    ADD COLUMN IF NOT EXISTS is_own_transfer BOOLEAN NOT NULL DEFAULT false;

CREATE INDEX IF NOT EXISTS idx_transactions_is_own_transfer
    ON transactions (is_own_transfer) WHERE is_own_transfer = true;
