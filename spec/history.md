# Iteration History

## Iteration 1 - MCP Capabilities Exploration Baseline

### What We Did

- Created `client\` and `server\` as separate project directories.
- Scaffolded `client\McpPoc.Client` as a .NET console app.
- Implemented client prompt loop with OpenAI Responses API integration.
- Filled key specification gaps in high-level and low-level design files.
- Added Mermaid diagrams for component relation and request flow.

### What We Expected

- A runnable client that sends user input to a reasoning model and prints output.
- A minimal server placeholder ready for future MCP implementation.
- Clear architecture docs to support iterative research.

### What Changed

- Chose direct REST integration instead of SDK for faster and dependency-light setup.
- Kept server intentionally non-runnable for this iteration to focus on client baseline.

### What We Learned

- Separating client and server from day one makes scope boundaries explicit.
- Simple env-based configuration (`OPENAI_API_KEY`, `OPENAI_MODEL`) is sufficient for early experiments.
- Mermaid diagrams improve communication of evolving architecture and flow.

### Follow-ups

- Add MCP server runtime scaffold.
- Define first cross-project contract for MCP tool invocation.
- Introduce structured trace logs across both projects.

## Iteration 2 - Client Cost Optimization and Persistent Chat Memory

### What We Did

- Switched client default model to `gpt-4.1-mini` for lower-cost testing.
- Migrated client requests to OpenAI .NET SDK chat flow (`ChatClient`).
- Added persistent conversation history in client via local JSON file.
- Introduced optional `OPENAI_CHAT_HISTORY_PATH` for custom history file location.
- Updated `.env.example` and `.gitignore` for new model default and history file handling.

### What We Expected

- Lower per-test costs while preserving iterative chat behavior.
- Context continuity across turns and app restarts.
- Zero required setup beyond existing env variables for default behavior.

### What Changed

- Client now loads prior user/assistant messages and sends them with each new prompt.
- Client now appends successful user/assistant turns to local persisted history.
- Configuration now supports `OPENAI_CHAT_HISTORY_PATH` in addition to `OPENAI_API_KEY` and `OPENAI_MODEL`.

### What We Learned

- Persistent local memory significantly improves multi-step experimentation quality.
- Keeping memory in a local JSON file is sufficient for early PoC iterations.
- Cost-focused defaults (`gpt-4.1-mini`) are practical for rapid research loops.

### Follow-ups

- Add memory management commands (clear/reset/history size control).
- Evaluate token-window controls (truncate/summarize older history).
- Consider server-side/shared memory only after MCP runtime is in place.

## Iteration 3 - Simulated MCP HTTP Tool Services

### What We Did

- Added four simple Python HTTP tool services under `server\services\`:
  - `calculator_tool`
  - `search_tool`
  - `db_query_tool`
  - `risk_check_tool`
- Added per-service Dockerfiles and dependencies.
- Added root `docker-compose.yml` to run all four services together.
- Added local movie JSON dataset (5 records) for search simulation.
- Added SQLite initialization script and read-only query endpoint for DB simulation.

### What We Expected

- A local environment that can simulate MCP HTTP transport with multiple tool backends.
- Deterministic, low-complexity service behavior suitable for routing experiments.
- One-command startup with Docker Compose.

### What Changed

- Server project moved from placeholder-only state to runnable HTTP simulation services.
- Tool APIs are now available on dedicated local ports via compose orchestration.
- Finance-like risk checking is now represented by static rule-based scoring.

### What We Learned

- Simple Flask-based services are enough for first-pass transport and orchestration tests.
- Local static datasets (JSON/SQLite) provide predictable tool outputs for validation.
- Per-service containerization keeps future replacement/refactoring isolated.

### Follow-ups

- Add request/response logging and correlation IDs across services.
- Add lightweight contract tests for each HTTP endpoint.
- Wire client/policy router to call these local services based on context rules.
