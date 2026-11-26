---
name: "studio-architecture-tour"
description: "Give a concise architecture tour of elsa-foundation-studio (the ASP.NET Core/CShells host and React/Vite Elsa Studio). Use when a user asks what the repository is, how to orient, where to start, or how the shell, modules, routing, API, and authentication fit together."
argument-hint: "Optional tour focus"
compatibility: "Requires the elsa-foundation-studio repository"
metadata:
  author: "elsa-foundation-studio"
  source: "docs/architecture-tour.md"
user-invocable: true
disable-model-invocation: false
---

## User Input

```text
$ARGUMENTS
```

## Outline

1. Read `docs/architecture-tour.md`.
2. Follow its source links only for the system the user asks about.
3. Keep the tour concise and point to the next source to inspect.

Do not expand into a module implementation unless the user asks for one.
