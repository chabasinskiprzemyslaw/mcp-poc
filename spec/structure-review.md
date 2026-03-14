# Structure Review

## Description

Decision log for repository and documentation structure choices.

## Goal Of This File

Record what structure we accept, reject, or defer, and why.

## Success Looks Like

The team can decide folder layout confidently and track changes to that decision over iterations.

## Current Proposal

- Use two top-level directories:
  - `client\` for the .NET console application.
  - `server\` for MCP server implementation (placeholder in iteration 1).
- Keep architecture and iteration notes under `spec\`.

## Accepted Decisions

- Separate client and server codebases by directory from iteration 1.
- Implement only client runtime behavior first to establish executable baseline.
- Keep server intentionally empty except for scaffold notes.
- Use Mermaid diagrams in spec documents for architecture and flow evolution.

## Rejected or Deferred Decisions

- Deferred: monorepo build orchestration for both projects.
- Deferred: adding MCP protocol handlers in server.
- Rejected for now: combining client and server into one project for speed.

## Alternatives Considered

- Single .NET solution with both projects immediately.
- Server-first approach before client baseline.
- SDK-based OpenAI client for iteration 1.

## Open Questions

- Which language/framework should implement the MCP server in iteration 2?
- Should routing happen in client, server, or a separate host orchestrator?
- Which reasoning model should be default once benchmarking begins?

## Next Iteration Changes

- Add MCP server skeleton (startup, capability advertisement, placeholder endpoints).
- Add shared schema contract between client and server.
- Add first end-to-end call path that touches both projects.
