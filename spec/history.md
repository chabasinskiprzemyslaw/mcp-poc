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

## Iteration 4 - FastMCP Wrapper Layer for Tool Services

### What We Did

- Added `server\mcp_wrappers` with four FastMCP servers:
  - `calculator_mcp_server.py`
  - `search_mcp_server.py`
  - `db_query_mcp_server.py`
  - `risk_check_mcp_server.py`
- Implemented MCP tools that delegate to existing Flask HTTP services.
- Added shared lightweight HTTP JSON client helper for wrapper-to-service calls.
- Extended `docker-compose.yml` to run the 4 MCP wrappers alongside 4 HTTP tool services.

### What We Expected

- A clean separation between MCP protocol handling and tool execution logic.
- Ability to run realistic multi-server MCP setup over HTTP transport for routing experiments.

### What Changed

- Existing Python services remain execution backends.
- FastMCP wrappers now expose MCP endpoints (`/mcp`) on separate ports (`9001-9004`).
- Documentation updated with wrapper startup and configuration.

### What We Learned

- Wrapper architecture keeps core tool services reusable outside MCP.
- FastMCP provides low-friction MCP server exposure with simple function decorators.

### Follow-ups

- Add contract/integration tests for MCP wrapper tool responses.
- Add health/readiness checks in compose for wrapper startup ordering.
- Add authentication controls if moving beyond local-only use.

## Iteration 5 - Client Server Registry (Filesystem + Hot Reload)

### What We Did

- Added `client\McpPoc.Client\ServerRegistry.cs` as a separate registry module.
- Added registry JSON schema with per-server fields:
  - `serverId`, `name`, `transport`, `baseUrl`/`command`, `author`, `capabilities`, `priority`, `health`, `tags`, `version`.
- Added stable-ID validation and logical-name separation from endpoint addressing.
- Added simple HTTP health checks with retry policy and latency/error tracking.
- Added hot reload for registry file updates via filesystem watcher.
- Added client commands for operational visibility:
  - `/servers`
  - `/servers health`
  - `/servers metrics`
  - `/servers find <tag...>`

### What We Expected

- Practical PoC-friendly registry that can evolve toward DB-backed service discovery later.
- Better routing/fallback decisions based on priority, health, latency, and error rate signals.

### What Changed

- Client startup now loads server registry from `MCP_SERVER_REGISTRY_PATH` (or default `.mcp-server-registry.json`).
- Added `.mcp-server-registry.example.json` and local `.mcp-server-registry.json`.
- Added env and ignore updates for registry file handling.

### What We Learned

- Registry-as-file is enough for early MCP orchestration experiments.
- Separating logical IDs from physical endpoints keeps future migration paths cleaner.
- Runtime metrics provide immediate insight for fallback ordering.

### Follow-ups

- Add circuit breaker state transitions (open/half-open/closed) for repeated failures.
- Add capability discovery cache with refresh-on-reconnect strategy.
- Move endpoint/secrets management to dedicated config + secret store for non-local environments.

## Iteration 6 - MCP Client Core in .NET Host

### What We Did

- Added `client\McpPoc.Client\McpClient.cs` implementing MCP client basics:
  - `initialize`
  - `notifications/initialized`
  - `tools/list`
  - `tools/call`
- Implemented HTTP JSON-RPC request/response handling for MCP wrapper endpoints.
- Added local command surface in client:
  - `/mcp tools <serverId>`
  - `/mcp call <serverId> <toolName> "<jsonArgs>"`
  - `/mcp route-call <toolName> "<jsonArgs>" <tag...>`
- Integrated route-call fallback with server registry ordering and runtime metrics.
- Expanded registry examples to include MCP wrapper entries (`transport = mcp-http`, ports `9001-9004`).

### What We Expected

- First practical host-side MCP client loop aligned with initial research notes.
- Ability to discover and invoke tools from local FastMCP servers from within the .NET client.

### What Changed

- Client now supports explicit MCP tool discovery/call commands in addition to OpenAI chat loop.
- Registry can now represent both direct HTTP services and MCP HTTP wrapper servers.

### What We Learned

- Minimal MCP lifecycle and tool invocation can be integrated cleanly without overbuilding transport abstractions for PoC.
- Registry + metrics provides an immediate path to deterministic fallback routing.

### Follow-ups

- Add full protocol notifications handling (`listChanged`, progress, logging).
- Add reconnect supervisor and protocol-version negotiation fallback.
- Add dedicated MCP transport abstraction (`ITransport`) as complexity grows.
