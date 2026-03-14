# Idea

## Description

Initial concept note for the MCP PoC and the core research direction.

## Goal Of This File

Capture the original problem framing, motivation, and baseline solution idea before detailed design.

## Success Looks Like

Anyone can read this file and quickly understand what we are building, why it matters, and the first implementation direction.

Local Tool-Using MCP Agent (Structured Context Router)

🎯 Goal: Build an MCP agent that selects tools based on structured JSON context.

🧠 Research roots:

ReAct
Toolformer

🛠 Build:

LLM

Tool registry (calculator, search, DB query)

Context schema (user role, risk level, domain)

Policy-based routing (if context.type == "finance" → call risk tool)

Extend:

Add structured memory (vector store)

Add role-based constraints

Log reasoning traces

💼 Why it matters:
Foundation of enterprise AI orchestration.

## Proposed MVP Tool Set

Goal: Keep tools intentionally simple so we can evaluate MCP routing quality.

1. `calculator_tool`
   - Purpose: deterministic arithmetic (`+`, `-`, `*`, `/`) for low-risk utility calls.
   - PoC simplicity: no symbolic math, no history, no advanced parsing.
2. `search_tool`
   - Purpose: simple keyword lookup over a local document set.
   - PoC simplicity: local data only, no ranking model tuning.
3. `db_query_tool`
   - Purpose: read-only query on a small local SQLite dataset.
   - PoC simplicity: only parameterized `SELECT`, no writes.
4. `risk_check_tool`
   - Purpose: return a basic risk label for finance-like requests.
   - PoC simplicity: static rule table, no ML scoring.

## Tool Design Principle For This PoC

The main experiment is routing and policy correctness, not tool intelligence.
If a tool can return predictable output for a fixed input, it is sufficient for this phase.

## Out Of Scope For Current Iteration

- External APIs and internet-scale search
- Production-grade database hardening
- Complex risk models and explainability pipelines
- Multi-agent tool negotiation
