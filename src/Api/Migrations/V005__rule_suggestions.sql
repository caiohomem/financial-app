CREATE TABLE rule_suggestions (
    id                    SERIAL PRIMARY KEY,
    transaction_id        INTEGER NOT NULL REFERENCES transactions(id),
    suggested_pattern     TEXT NOT NULL,
    suggested_match_type  TEXT NOT NULL CHECK (suggested_match_type IN ('contains', 'regex', 'merchant_eq')),
    category_canonical_id INTEGER NOT NULL REFERENCES categories(id),
    confidence            DECIMAL(4,3) NOT NULL,
    status                TEXT NOT NULL DEFAULT 'pending'
                              CHECK (status IN ('pending', 'approved', 'rejected')),
    created_at            TIMESTAMPTZ NOT NULL DEFAULT now()
);
