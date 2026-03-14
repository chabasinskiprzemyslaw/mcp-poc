# Low-Level Design

## Description

Implementation-focused design for modules, contracts, and execution logic.

## Goal Of This File

Translate high-level architecture into concrete technical details that are directly buildable.

## Success Looks Like

Developers can implement client-side model interaction behavior with minimal ambiguity and a clear path to MCP integration in the next iteration.

## Module Breakdown

- `Program.cs`
  - Loads configuration from environment.
  - Runs interactive console loop.
  - Sends request payload to OpenAI endpoint.
  - Extracts and prints assistant text.

## Data Contracts

- Request payload (client -> OpenAI):
  - `model: string`
  - `input: string`
  - `reasoning.effort: "medium"`
- Response payload (OpenAI -> client):
  - Prefer `output_text` when present.
  - Fallback: aggregate text fields from `output[*].content[*].text`.

## Validation Rules

- `OPENAI_API_KEY` must be present; app exits with explicit error when missing.
- Empty user input is ignored.
- `exit` (case-insensitive) terminates the console loop.

## Routing Execution Details

- Not implemented in iteration 1.
- Future iteration: route selected request types through MCP server tools before final user response.

## Error Handling

- Network failures (`HttpRequestException`) are shown to the user and loop continues.
- Request timeout (`TaskCanceledException`) is shown to the user and loop continues.
- Non-success API responses print status code and payload for debugging.

## Logging and Tracing

- Iteration 1 uses console output only.
- Structured trace logging is deferred to MCP server integration iteration.

## Test Cases

- Build succeeds for `client\McpPoc.Client`.
- Missing API key produces explicit startup error.
- Valid prompt path prints assistant text for successful API responses.
