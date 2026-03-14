# Test Plan

## Description

Validation strategy for functional correctness and safety of context-based routing.

## Goal Of This File

Define what to test, at which level, and what evidence proves the PoC works.

## Success Looks Like

Passing tests demonstrate correct schema validation, policy routing, and end-to-end behavior under expected scenarios.

## Test Objectives

## Scope

In scope:
- Context schema validation
- Deterministic tool routing
- Role/risk guardrails
- Trace logging for route decisions

Out of scope:
- Tool quality benchmarking
- Load/performance testing
- Production security hardening

## Unit Tests

- Validator rejects missing required context fields.
- Router returns first matching rule by priority.
- Router denies unknown `actionType`.
- Router enforces domain-specific pre-check (`risk_check_tool` for finance).

## Integration Tests

- Host receives valid context and invokes expected MCP tool.
- Host denies disallowed role/tool combinations.
- Host records trace: input context, matched rule ID, selected tool, result status.

## End-to-End Scenarios

1. Finance request requiring risk pre-check then calculation.
2. Non-finance lookup request routed to search.
3. Read-only data request routed to DB query tool.
4. Unknown or malformed context denied safely.

## Exit Criteria

- At least 1 passing scenario per MVP tool.
- All deny-by-default scenarios behave correctly.
- Re-running same context yields same selected tool.
- Trace logs are present for all routed and denied requests.
