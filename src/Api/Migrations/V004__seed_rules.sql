DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'uq_rules_pattern_match_type'
    ) THEN
        ALTER TABLE rules
            ADD CONSTRAINT uq_rules_pattern_match_type UNIQUE (pattern, match_type);
    END IF;
END $$;

INSERT INTO rules (pattern, match_type, category_canonical_id, priority)
SELECT seed.pattern, seed.match_type, categories.id, seed.priority
FROM (
    VALUES
        ('Glovo', 'merchant_eq', 'Restaurantes', 100),
        ('Uber Eats', 'merchant_eq', 'Restaurantes', 100),
        ('Barto', 'merchant_eq', 'Restaurantes', 100),
        ('Cerveja Canil', 'merchant_eq', 'Restaurantes', 100),
        ('Cerveja Musa', 'merchant_eq', 'Restaurantes', 100),
        ('Festival Das Francesinhas', 'merchant_eq', 'Restaurantes', 100),
        ('Google One', 'merchant_eq', 'Subscricoes', 100),
        ('Transferwise', 'merchant_eq', 'Transferencias', 100),
        ('A.M.O.Brewery', 'contains', 'Restaurantes', 90),
        ('Barto', 'contains', 'Restaurantes', 90),
        ('Bolt.Eu', 'contains', 'Transporte', 90),
        ('Bolt.Eur', 'contains', 'Transporte', 90),
        ('Bolt.Euo', 'contains', 'Transporte', 90),
        ('Cerveja Canil', 'contains', 'Restaurantes', 90),
        ('Cetelem', 'contains', 'Habitacao/Contas', 90),
        ('Endesa', 'contains', 'Habitacao/Contas', 90),
        ('Festival Das Francesinhas', 'contains', 'Restaurantes', 90),
        ('Generali', 'contains', 'Habitacao/Contas', 90),
        ('Google One', 'contains', 'Subscricoes', 90),
        ('Metropolitano De Lisboa', 'contains', 'Transporte', 90),
        ('Uber One', 'contains', 'Subscricoes', 90),
        ('Vodafone', 'contains', 'Habitacao/Contas', 90),
        ('A.M.O.BREWERY', 'contains', 'Restaurantes', 50),
        ('Brewery', 'contains', 'Restaurantes', 50)
) AS seed(pattern, match_type, canonical_name, priority)
JOIN categories ON categories.name = seed.canonical_name
ON CONFLICT (pattern, match_type) DO NOTHING;
