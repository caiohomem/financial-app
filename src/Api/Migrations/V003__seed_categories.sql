ALTER TABLE categories
    ADD CONSTRAINT uq_categories_name UNIQUE (name);

ALTER TABLE category_mappings
    ADD CONSTRAINT uq_category_mappings_source_label UNIQUE (source, source_label);

INSERT INTO categories (name, is_system)
VALUES
    ('Alimentacao', true),
    ('Restaurantes', true),
    ('Supermercado', true),
    ('Transporte', true),
    ('Subscricoes', true),
    ('Saude', true),
    ('Habitacao/Contas', true),
    ('Transferencias', true),
    ('Levantamentos', true),
    ('Taxas/Impostos', true),
    ('Lazer', true),
    ('Receitas', true),
    ('Outros', true)
ON CONFLICT (name) DO NOTHING;

UPDATE categories AS child
SET parent_id = parent.id
FROM categories AS parent
WHERE parent.name = 'Alimentacao'
  AND child.name IN ('Restaurantes', 'Supermercado')
  AND child.parent_id IS DISTINCT FROM parent.id;

INSERT INTO category_mappings (source, source_label, category_canonical_id)
SELECT 'wise', mapping.source_label, categories.id
FROM (
    VALUES
        ('Alimentação (restaurantes e afins)', 'Restaurantes'),
        ('Compras no mercado', 'Supermercado'),
        ('Dinheiro adicionado', 'Receitas'),
        ('Dinheiro em espécie', 'Levantamentos'),
        ('Compras', 'Outros'),
        ('Contas', 'Habitacao/Contas'),
        ('Entretenimento', 'Lazer'),
        ('Geral', 'Transferencias'),
        ('Recompensas', 'Receitas'),
        ('Transporte', 'Transporte')
) AS mapping(source_label, canonical_name)
JOIN categories ON categories.name = mapping.canonical_name
ON CONFLICT (source, source_label) DO NOTHING;
