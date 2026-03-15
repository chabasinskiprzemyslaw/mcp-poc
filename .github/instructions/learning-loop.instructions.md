---
applyTo: "**"
---

# Learning Loop Instruction

Whenever you implement a recognized software design pattern, architecture choice, or complex algorithm, you MUST append a short learning note to a markdown file in `AppSeparateFolderLearning`.

## Required behavior

1. At the start of each coding session, create (or reuse) a session file in `AppSeparateFolderLearning` named `session-YYYY-MM-DD.md`.
2. When a qualifying implementation is made, append one compact entry immediately.
3. Each entry must include:
   - Pattern / Architecture / Algorithm name
   - Why it was chosen (1-3 sentences)
   - Implementation location (file path, and line if available)
   - Trade-off or risk to watch
4. Keep each entry concise (roughly 4-8 lines).
5. Never include secrets, API keys, tokens, or personal data.

## Entry template

```md
## [HH:mm] <Name>
- Type: Pattern | Architecture | Algorithm
- Why chosen: <short rationale>
- Implemented in: <path/to/file>
- Trade-off: <short note>
```

If no such implementation occurred in a session, do not write filler notes.
