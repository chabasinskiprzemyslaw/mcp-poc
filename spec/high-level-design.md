# High-Level Design

## Description

System-level architecture for the MCP PoC with major components and interactions.

## Goal Of This File

Define the big picture so implementation stays aligned with intended behavior and boundaries.

## Success Looks Like

A new contributor can understand the architecture, data flow, and top-level trade-offs without reading code.

## Problem Statement

We need a first working iteration for MCP research with two independent projects:
- A client app that sends user input to an OpenAI reasoning model.
- A local set of MCP-like HTTP tool services for transport and routing experiments.

The immediate goal is to establish structure, interfaces, and a repeatable iteration workflow.

## Goals

- Create clear separation between client and server repositories/directories.
- Provide a runnable client baseline for prompt/response experimentation.
- Provide four runnable HTTP tool simulations to test MCP-style transport.
- Capture architecture and interaction flow in diagrams.
- Keep the first iteration small and observable.

## Non-Goals

- Implement full production MCP server runtime.
- Implement tool routing or policy engine runtime.
- Add production hardening, auth layers, or deployment automation.

## System Context

The user interacts with a local .NET console client.  
The client calls OpenAI Chat API via the OpenAI .NET SDK for reasoning output.  
The client persists user/assistant history locally to preserve context across turns and runs.  
The server side provides four local HTTP tool services containerized with Docker Compose.

```mermaid
flowchart LR
    U[User] --> C[Client: .NET Console App]
    C --> O[OpenAI Chat API]
    C --> H[Local Chat History JSON]
    C -. tool call .-> T1[Calculator Tool]
    C -. tool call .-> T2[Search Tool]
    C -. tool call .-> T3[DB Query Tool]
    C -. tool call .-> T4[Risk Check Tool]
```

## Core Components

- Client Console App
  - Reads user prompt from terminal.
  - Loads/saves persistent chat history.
  - Sends request with conversation context to OpenAI provider.
  - Prints model output to terminal.
- OpenAI Provider Integration
  - Uses `OPENAI_API_KEY`, optional `OPENAI_MODEL`, and optional `OPENAI_CHAT_HISTORY_PATH`.
  - Uses OpenAI .NET SDK `ChatClient` for chat completion calls.
- Local History Store
  - JSON file containing user/assistant message history.
  - Default path: `.openai-chat-history.json` (project root unless overridden).
- Tool Service Layer (HTTP, Python, containerized)
  - `calculator_tool`: arithmetic endpoints including add-multiple.
  - `search_tool`: keyword search over local movie JSON documents.
  - `db_query_tool`: read-only SQLite `SELECT` query endpoint.
  - `risk_check_tool`: simple finance-like static rule checker.
  - Orchestrated with `docker-compose.yml` for local multi-service startup.

## End-to-End Flow

```mermaid
sequenceDiagram
    participant U as User
    participant C as .NET Client
    participant H as Local History File
    participant O as OpenAI Chat API
    participant T as HTTP Tool Services

    U->>C: Enter prompt
    C->>H: Load prior messages
    C->>O: Chat completion (history + new prompt)
    O-->>C: Optional tool decision/routing hint
    C->>T: HTTP tool request (based on policy/context)
    T-->>C: Tool response payload
    O-->>C: Assistant response
    C->>H: Append user/assistant messages
    C-->>U: Render assistant text
```

## Risks and Trade-Offs

- Trade-off accepted: local file-backed memory for simplicity over shared/distributed memory.
- Risk: unbounded history growth may increase prompt tokens and cost.
- Mitigation: keep model configurable and introduce truncation/summarization in a future iteration.
- Trade-off accepted: tool services are intentionally simple and deterministic.
- Risk: no auth/rate limit between local services.
- Mitigation: keep scope local-only for current research iteration.
