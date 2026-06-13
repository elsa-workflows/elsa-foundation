# First-User Prompt Options

Use these prompts when a new architect or engineer enters `elsa-foundation`. They are intentionally simple. The agent should answer through the Architecture Tour, skills catalog, program-goal buckets, reports, maps, and constitutions as needed.

## Orientation Prompts

```text
Give me the architecture tour of this workspace. Keep it concise and tell me where to look next.
```

```text
Explain what the Elsa foundation workspace is, what belongs in elsa-foundation, and what might later move to elsa-workspace.
```

```text
Show me how this workspace is organized: where are the constitutions, glossary, skills, maps, reports, specs, and program goals?
```

```text
What are seams, bridges, replacement contracts, contributions, events, and startup tasks in this architecture?
```

## Workspace Mechanics Prompts

```text
How should I choose the right workflow or skill for a task in this repo?
```

```text
What should I read before starting architecture work here, and what should I avoid reading too broadly?
```

```text
Which program-goal buckets are active, and what does each one own?
```

```text
What is unfinished, unratified, unverified, or intentionally deferred right now?
```

## Hard Next-Unit Prompts

```text
Help me start the Runtime Execution Seam work unit. Use the existing handoff report and stop at a Speckit-ready plan.
```

```text
Help me identify the next Code Reality And Test Maturity work unit. Start from reports and do not implement before planning.
```

```text
Help me review one unratified constitution item. Use Critical Constitution Review and keep the review targeted.
```

```text
Help me explore a bounded feature composition slice. Use Feature Composition Explorer and do not generate appsettings until readiness is proven.
```

## Handoff Prompt Template

```text
I am starting work in elsa-foundation.

Please use the Architecture Tour first, then select the right skill and program-goal bucket for my task:
[describe task here]

If you invoke generated maps and the manifest says inputs are dirty or freshness is uncertain, refresh the relevant map first and review the generated findings before continuing.

Warn me if the task depends on draft or unratified constitution material. If ratification is the goal, help me start a targeted work unit with the available skills and guardrails.

Do not broaden this into foundation-workspace polishing. If the repo can already route me, guide me to the focused hard-work bucket and the next concrete artifact.
```
