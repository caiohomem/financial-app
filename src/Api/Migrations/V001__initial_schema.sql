CREATE TABLE import_batches (
    id          SERIAL PRIMARY KEY,
    source      TEXT NOT NULL,
    filename    TEXT NOT NULL,
    imported_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    row_count   INTEGER NOT NULL
);

CREATE TABLE accounts (
    id          SERIAL PRIMARY KEY,
    name        TEXT NOT NULL,
    source      TEXT NOT NULL CHECK (source IN ('activobank', 'wise')),
    currency    TEXT NOT NULL,
    created_at  TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE TABLE categories (
    id        SERIAL PRIMARY KEY,
    name      TEXT NOT NULL,
    parent_id INTEGER REFERENCES categories(id),
    is_system BOOLEAN NOT NULL DEFAULT false
);

CREATE TABLE transactions (
    id                    SERIAL PRIMARY KEY,
    account_id            INTEGER NOT NULL REFERENCES accounts(id),
    source                TEXT NOT NULL CHECK (source IN ('activobank', 'wise')),
    source_transaction_id TEXT,
    booking_date          DATE NOT NULL,
    value_date            DATE,
    raw_description       TEXT NOT NULL,
    normalized_merchant   TEXT,
    amount                DECIMAL(18,4) NOT NULL CHECK (amount >= 0),
    direction             TEXT NOT NULL CHECK (direction IN ('IN', 'OUT')),
    currency              TEXT NOT NULL,
    running_balance       DECIMAL(18,4),
    status                TEXT NOT NULL CHECK (status IN ('completed', 'refunded', 'cancelled', 'pending')),
    category_canonical_id INTEGER REFERENCES categories(id),
    category_source       TEXT,
    import_batch_id       INTEGER NOT NULL REFERENCES import_batches(id),
    dedup_hash            TEXT
);

CREATE INDEX idx_transactions_source_source_transaction_id
    ON transactions (source, source_transaction_id);

CREATE INDEX idx_transactions_dedup_hash
    ON transactions (dedup_hash);

CREATE TABLE category_mappings (
    id                    SERIAL PRIMARY KEY,
    source                TEXT NOT NULL CHECK (source IN ('wise', 'activobank', 'rule')),
    source_label          TEXT NOT NULL,
    category_canonical_id INTEGER NOT NULL REFERENCES categories(id)
);

CREATE TABLE rules (
    id                    SERIAL PRIMARY KEY,
    pattern               TEXT NOT NULL,
    match_type            TEXT NOT NULL CHECK (match_type IN ('contains', 'regex', 'merchant_eq')),
    category_canonical_id INTEGER NOT NULL REFERENCES categories(id),
    priority              INTEGER NOT NULL DEFAULT 0
);
