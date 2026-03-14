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
- `mcp_wrappers\`
  - FastMCP wrappers exposing MCP tools over HTTP transport and delegating to the HTTP services above.
  - Wrapper scripts:
    - `calculator_mcp_server.py`
    - `search_mcp_server.py`
    - `db_query_mcp_server.py`
    - `risk_check_mcp_server.py`

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
- MCP Calculator server: `http://localhost:9001/mcp`
- MCP Search server: `http://localhost:9002/mcp`
- MCP DB Query server: `http://localhost:9003/mcp`
- MCP Risk Check server: `http://localhost:9004/mcp`

## Running wrappers locally without Docker

From `server\mcp_wrappers`:

```bash
pip install -r requirements.txt
python calculator_mcp_server.py
python search_mcp_server.py
python db_query_mcp_server.py
python risk_check_mcp_server.py
```

Each wrapper supports:
- `MCP_TRANSPORT` (default `http`)
- `MCP_HOST` (default `0.0.0.0`)
- `MCP_PORT` (wrapper-specific default: `9001-9004`)
