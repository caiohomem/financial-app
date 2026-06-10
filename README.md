# financial-app

Projeto de gestao financeira pessoal para uso local. A stack sobe apenas na maquina do developer com API em .NET, frontend em Next.js e PostgreSQL via Docker Compose.

## Arranque

```bash
cp .env.example .env
docker compose up --build
```

## Servicos

- Frontend: http://localhost:3000
- API health: http://localhost:8080/api/health
- PostgreSQL: localhost:5432

## Notas

- `.env` esta ignorado no git e nao deve ser commitado.
- Os dados do Postgres persistem no volume `postgres_data`.
- O frontend inicial carrega uma pagina vazia por design nesta fase.
- Quando a categorizacao por IA estiver ativa, apenas `normalized_merchant` e `raw_description` de transacoes nao-categorizadas sao enviados para a API do Claude; nenhum outro dado da conta ou do utilizador sai da aplicacao.
