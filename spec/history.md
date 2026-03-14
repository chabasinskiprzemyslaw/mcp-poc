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
