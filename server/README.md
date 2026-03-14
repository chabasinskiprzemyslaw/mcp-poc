# MCP Tool Service Simulations

This directory now contains simple HTTP services that simulate MCP tool backends for transport/routing tests.

## Services

- `services\calculator_tool`
  - Endpoints:
    - `GET /health`
    - `POST /calculate`
    - `POST /add-multiple`
- `services\search_tool`
  - Local movie dataset (`movies.json`, 5 records).
  - Endpoints:
    - `GET /health`
    - `GET /movies`
    - `GET /search?q=<keyword>`
- `services\db_query_tool`
  - Local SQLite dataset seeded from `data\init.sql`.
  - Endpoint:
    - `POST /query` (read-only `SELECT` queries)
- `services\risk_check_tool`
  - Static finance-style risk rules.
  - Endpoint:
    - `POST /risk-check`

## Running all services

From repository root:

```bash
docker compose up --build
```

Ports:

- Calculator: `http://localhost:8001`
- Search: `http://localhost:8002`
- DB Query: `http://localhost:8003`
- Risk Check: `http://localhost:8004`
