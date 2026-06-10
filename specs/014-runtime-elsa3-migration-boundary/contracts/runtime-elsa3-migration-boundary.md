# Contract: Runtime Elsa 3 Migration Boundary

Elsa 3 compatibility is import-only:

```text
Elsa 3 authored definition -> Elsa3 import adapter -> Elsa 4 design entity / diagnostics
Elsa 3 workflow instance state -> unsupported diagnostic
```

Rules:

- Runtime does not read Elsa 3 workflow instance state as Elsa 4 continuation state.
- Runtime projects do not reference `Elsa3.*` import or compatibility modules.
- Authored definition migration returns diagnostics rather than requiring callers to infer compatibility from arbitrary exceptions.
- Migration diagnostics include actionable guidance for live instance cutover.
