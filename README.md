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

## Deteccao de anomalias

O endpoint `GET /api/anomalies?month=YYYY-MM` usa um metodo hibrido simples e configuravel: primeiro remove movimentos recorrentes do mesmo mercador com valores semelhantes ao longo do historico importado, depois sinaliza apenas os restantes que fiquem muito acima do baseline da categoria. Esta abordagem foi escolhida porque com apenas 1-2 meses de historico um filtro por recorrencia evita falsos positivos nos pagamentos mensais grandes, enquanto o teste de magnitude continua a apanhar gastos pontuais fora do padrao.

| Parametro | Default | Descricao |
|---|---:|---|
| `RecurrenceMinMonths` | `2` | Numero minimo de meses distintos em que um mercador com valor semelhante tem de aparecer para ser tratado como recorrente. |
| `RecurrenceTolerancePct` | `0.05` | Tolerancia percentual usada para considerar dois valores do mesmo mercador como equivalentes. |
| `MagnitudeMultiplier` | `2.0` | Multiplicador aplicado a mediana historica da categoria antes de comparar o valor atual. |
| `AbsoluteFloor` | `300.0` | Piso absoluto que impede micro-transacoes de serem sinalizadas como anomalias. |
| `MinHistoryMonths` | `2` | Numero minimo de meses importados para ativar o modo normal com filtro de recorrencia. |
| `ColdStartAbsoluteThreshold` | `2000.0` | Limite absoluto usado em cold start, quando ainda nao existe historico suficiente. |
