# Copilot Coding Agent Onboarding

## Repository Summary

- Purpose: Proof-of-concept MCP environment with a .NET console client and local Python tool services (plus FastMCP wrappers) for routing/transport experiments.
- Project type: Multi-component monorepo (1 .NET app + 8 Python containers).
- Approx size: small repo (
  - 1 solution / 1 C# project
  - 4 Flask tool services
  - 4 FastMCP wrapper servers
  - design docs under `spec/`
    ).
- Languages/runtimes:
  - C# (.NET `net9.0` target)
  - Python (Docker images use `python:3.12-slim`)
  - Docker Compose for local orchestration

## Always-Use Workflow (Fast Path)

Run from repo root unless noted.

1. `dotnet build mcp-poc.sln -v minimal`
2. `dotnet test mcp-poc.sln -v minimal`
3. `docker compose up --build -d`
4. Validate HTTP tools:
   - `Invoke-RestMethod http://localhost:8001/health`
   - `Invoke-RestMethod http://localhost:8002/health`
   - `Invoke-RestMethod http://localhost:8003/health`
   - `Invoke-RestMethod http://localhost:8004/health`
5. Run client: `dotnet run --project client/McpPoc.Client/McpPoc.Client.csproj`

Always use this sequence before proposing major code changes to reduce avoidable failures.

## Prerequisites (Validated)

- .NET SDK: validated with `dotnet --version` -> `10.0.201` (build targeting `net9.0` works).
- Docker Desktop + Compose: validated with Docker 27.x and Compose v2.
- Python: validated in host as `3.13.1`; Dockerized services still run on Python 3.12 base images.
- Environment file: `.env` is effectively required for normal client usage (it supplies `OPENAI_API_KEY` and defaults).

## Bootstrap, Build, Test, Run, Lint

### Bootstrap

- Ensure `.mcp-server-registry.json` exists (copy from `.mcp-server-registry.example.json` when needed).
- If using Docker path, no host pip installs are required.

### Build

- Works:
  - `dotnet build mcp-poc.sln -v minimal` (implicit restore succeeds).
  - `dotnet clean mcp-poc.sln -v minimal; dotnet restore mcp-poc.sln -v minimal; dotnet build mcp-poc.sln -v minimal`.
- Observed timings (local validation): clean/restore/build each ~0.5-1.5s after warm cache.

### Test

- `dotnet test mcp-poc.sln -v minimal` succeeds but currently runs no test assemblies (no test project in solution).
- Keep this command in validation anyway; CI may later add tests.

### Run (.NET client)

- Command: `dotnet run --project client/McpPoc.Client/McpPoc.Client.csproj`.
- Interactive slash commands are implemented in `LocalCommandDispatcher`:
  - `/servers`
  - `/servers health`
  - `/servers metrics`
  - `/servers find <tag...>`
  - `/mcp show`
  - `/mcp tools <serverId>`
  - `/mcp call <serverId> <tool> "<jsonArgs>"`
  - `/mcp route-call <tool> "<jsonArgs>" <tag...>`

### Run (Docker stack)

- `docker compose down`
- `docker compose up --build -d`
- `docker compose ps`

### Lint/Format

- No dedicated linting or formatting pipeline is configured for either C# or Python in this repo.
- Do not assume lint jobs exist; validate by build + runtime checks instead.

## Known Failure Modes and Workarounds (Validated)

- Port binding conflict on startup:

  - Symptom: `Bind for 0.0.0.0:8001 failed: port is already allocated` during `docker compose up`.
  - Cause: another local process/container already using port 8001.
  - Mitigation:
    1. `docker ps --format "table {{.Names}}\t{{.Ports}}"`
    2. `Get-NetTCPConnection -LocalPort 8001 -State Listen`
    3. stop conflicting service or remap port in `docker-compose.yml`, then rerun compose.

- Raw MCP HTTP invocation pitfalls with FastMCP wrappers:

  - Observed responses include:
    - `406 Not Acceptable: Client must accept both application/json and text/event-stream`
    - `Bad Request: Missing session ID`
  - Impact: direct ad-hoc POSTs to `/mcp` can fail unless protocol/session headers are exactly correct.
  - Recommendation: prefer invoking wrappers through project client flow (`McpClient`) or use a compliant MCP client.

- Slash command JSON quoting:

  - Incorrectly escaped JSON in `/mcp call` can throw unhandled `JsonReaderException`.
  - Always pass a valid JSON object string.

- Timeout behavior:
  - No command in this onboarding validation failed due tool timeout; failures were protocol/port related.

## Project Layout and Architecture

### Root files/directories

- `mcp-poc.sln`: solution entrypoint.
- `docker-compose.yml`: orchestrates all tool + wrapper containers.
- `.env.example`: required environment keys template.
- `.mcp-server-registry.example.json`: server metadata template.
- `client/`: .NET console client.
- `server/`: Python services and MCP wrappers.
- `spec/`: architecture/design/test docs.
- `postman/`: collection/environment for manual API tests.

### Key source locations

- Client startup/main loop: `client/McpPoc.Client/Program.cs`.
- Env/bootstrap helpers: `client/McpPoc.Client/DotEnvHelper.cs`, `client/McpPoc.Client/AppBootstrapHelper.cs`.
- Chat persistence: `client/McpPoc.Client/ChatHistoryService.cs`.
- Local command routing: `client/McpPoc.Client/LocalCommandDispatcher.cs`.
- MCP transport client: `client/McpPoc.Client/McpClient.cs`.
- Server registry + health/metrics/hot-reload: `client/McpPoc.Client/ServerRegistry.cs`.

### Server side

- Tool services (Flask):
  - `server/services/calculator_tool/app.py`
  - `server/services/search_tool/app.py`
  - `server/services/db_query_tool/app.py`
  - `server/services/risk_check_tool/app.py`
- MCP wrappers (FastMCP):
  - `server/mcp_wrappers/calculator_mcp_server.py`
  - `server/mcp_wrappers/search_mcp_server.py`
  - `server/mcp_wrappers/db_query_mcp_server.py`
  - `server/mcp_wrappers/risk_check_mcp_server.py`
  - shared HTTP utility: `server/mcp_wrappers/http_client.py`

## CI / Pre-check-in Reality

- No `.github/workflows/` currently present.
- Treat local validation as the gate:
  1. `dotnet build mcp-poc.sln -v minimal`
  2. `dotnet test mcp-poc.sln -v minimal`
  3. `docker compose up --build -d`
  4. health endpoint checks for 8001-8004
  5. quick client smoke run (`/servers`, `/servers health`, `/mcp show`)

## Documentation Pointers

- System architecture: `spec/high-level-design.md`
- Implementation details: `spec/low-level-design.md`
- Routing intent: `spec/routing-policy.md`
- Planned tests: `spec/test-plan.md`
- Service usage notes: `server/README.md`

## Agent Efficiency Rules

- Trust this file first. Only search the repository when information here is missing or proven incorrect.
- Prefer targeted edits in the files listed above instead of broad repo scans.
- Always validate in this order: build -> test -> docker stack -> client smoke commands.
- If MCP calls fail, check protocol/header/session assumptions before changing business logic.
