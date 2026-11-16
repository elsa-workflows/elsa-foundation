# Data model: Background eligibility

No persisted shape changes. These are in-memory types in `Elsa.Workflows.Runtime.Core`.

## BackgroundEligibilityCriterion

The criterion ids of ADR 0093 decision 2: `C1` … `C10`. Each has a check kind:

| Id | Kind | Checked at |
|---|---|---|
| C1 leaf, C2 completes in one go, C3 typed result, C4 durable result, C5 no hidden result, C9 variables only through inputs and results, C10 worker-bound inputs supported | E | publication and run time (admission and worker) |
| C6 no coordinator effects from the body | R | worker, after the body ran |
| C7 tolerates re-runs, C8 cancels cooperatively | D | not checked; documented on the attribute |

The component also checks a precondition before C1: the activity type declares `[BackgroundExecutionSafe]`
(`SupportsBackgroundExecution` in the pinned contract). Its refusal names no criterion but "does not declare
BackgroundExecutionSafe", because the declaration is how the author promises C7 and C8.

## BackgroundEligibilitySubject

What a criterion reads. Built by each caller from what it has; a field a caller cannot know is absent, and the
criteria that need it run only where it is present (C6 only at the worker).

| Field | Publication | Admission | Worker |
|---|---|---|---|
| node id | yes | yes | yes |
| is intrinsic / is reusable boundary | yes (authored node) | yes (pinned `IntrinsicKind`) | yes |
| has structure or child slots | yes | yes | yes |
| CLR activity type | resolved | well-known type registry | activated object's type |
| pinned activity contract (declaration, result type, policies, representations, inputs) | yes | yes | yes |
| input bindings (for C10) | yes | yes (and the input snapshot) | the input snapshot |
| staged coordinator effects (for C6) | — | — | yes |

## BackgroundEligibilityRefusal

| Field | Meaning |
|---|---|
| `Criterion` | `C1` … `C10`, or none for the declaration precondition |
| `NodeId` | the node refused |
| `Reason` | plain text, as in today's messages |
| `Code` | always `VF-BG-001` |

Rendered: `VF-BG-001 (C9): Background execution is not eligible for activity node 'n1': the activity reads
variables by name; variables reach a background step only through its inputs and output captures.`
