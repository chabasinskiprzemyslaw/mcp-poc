# Routing Policy

## Description

Rulebook describing how context maps to tool authorization and selection.

## Goal Of This File

Specify deterministic policy evaluation order, conflict handling, and guardrails.

## Success Looks Like

Given the same context, the system always chooses the same allowed tool path and logs a clear reason.

## Policy Goals

## Rule Evaluation Order

## Allow and Deny Conditions

## Tool Selection Rules

1. If `domain = finance`, route to `risk_check_tool` before any other business tool.
2. If `actionType = calculation` and `riskLevel != high`, route to `calculator_tool`.
3. If `actionType = lookup`, route to `search_tool`.
4. If `actionType = data-read` and role is allowed, route to `db_query_tool`.

Default: deny when no rule matches.

## Conflict Resolution

## Example Policy Cases

1. Context: `domain=finance`, `actionType=calculation`, `role=analyst`
	- Expected: `risk_check_tool` first, then `calculator_tool` only if allowed.
2. Context: `domain=general`, `actionType=lookup`, `role=user`
	- Expected: `search_tool`.
3. Context: `domain=operations`, `actionType=data-read`, `role=viewer`
	- Expected: `db_query_tool` (read-only).
4. Context: unknown `actionType`
	- Expected: deny with trace explaining no matching rule.
