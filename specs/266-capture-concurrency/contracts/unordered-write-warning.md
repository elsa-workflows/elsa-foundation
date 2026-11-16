# Contract: the unordered-write warning

## Workflow draft validation

`ValidationError`, unchanged identity:

| Field | Value |
|---|---|
| `Type` | `ValueFlow/ConcurrentWrite` |
| `Path` | `$workflow/variables/{referenceKey}` for the workflow scope, `{scopeId}/variables/{referenceKey}` otherwise |
| `Severity` | `Warning` (was `Error`); promotion, preflight and publication do not block on it |
| `Message` | See below |

Duplicate capture target on one node: `Type` `ValueFlow/DuplicateCaptureTarget`, `Path` `{nodeId}/outputs/{outputKey}`
(the second output), `Severity` `Warning`.

## Graph activity draft validation and compilation

`ActivityDiagnostic`:

| Field | Value |
|---|---|
| `Code` | `activity.graph.unordered-variable-write` (duplicate: `activity.graph.duplicate-capture-target`) |
| `Severity` | `Warning`; compilation still succeeds when there are no errors. Reported alongside other diagnostics, also when the body has errors |
| `Metadata` | `validationType` = `ValueFlow/ConcurrentWrite` (or `ValueFlow/DuplicateCaptureTarget`), `validationPath` as above |
| `Message` | See below |

## Message

```
Variable '{key}' in scope '{scope}' is written by {n} nodes whose order the definition does not fix: '{a}', '{b}'.
[Node '{x}' can run more than once at the same time.]
[Structure '{kind}' (node '{id}') does not declare how its children are ordered, so they are treated as unordered.]
[Structure '{kind}' (node '{id}') does not declare whether it completes only after its children, so its children's writes are treated as unordered with what follows it.]
The last successfully committed write wins, so the final value can differ between runs. To make it deterministic,
order the branches, capture into separate variables, or combine them with one write after the join. A step that reads
the variable and writes it back is a read-modify-write: opt it in to the Fault lost-update mode where an overwrite
must stop the step (ADR 0092).
```

## Structure declaration

See [data-model.md](../data-model.md): `IActivityStructureHandler.RelateChildren` and `CompletesAfterItsChildren`,
recorded in the extension-point catalogs of `Elsa.Workflows.Design.Core` and of every built-in structure.
