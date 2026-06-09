# Extension points — Pipelines domain

The per-domain catalog (framework §2.22.1). Anchored at `Elsa.Pipelines.Core` — this is the only project in this domain (no separate feature project); the middleware composition engine lives here. One section applies.

---

## Implementable contributor interfaces

### `IMiddleware` *(Core — `Elsa.Pipelines.Core`)*
- **Kind:** Contributor (pipeline middleware step).
- **Signature:** `ValueTask InvokeAsync(TContext context, Func<TContext, ValueTask> next);` (generic per pipeline context).
- **Register:** via `UseMiddleware<TMiddleware>()` on the pipeline builder (not DI-registered directly). Pipelines are built at startup via `MiddlewareExtensions.BuildMiddlewareDelegate()`.
- **Consumed by:** the pipeline delegate chain built by `BuildMiddlewareDelegate`. There is no single aggregating `IEventHandler`; the pipeline is a composed delegate, not an event-driven fan-in.

**Sub-interfaces (domain-specific pipeline shapes):**
- `ICommandMiddleware` *(Core — `Elsa.Mediator.Core`)* — middleware step in the Mediator command pipeline; see [`Elsa.Mediator/EXTENSION_POINTS.md`](../Elsa.Mediator/EXTENSION_POINTS.md).
- `IRequestMiddleware` *(Core — `Elsa.Mediator.Core`)* — middleware step in the Mediator request pipeline.
- `IEventMiddleware` *(Core — `Elsa.Events.Core`)* — middleware step in the event pipeline; see [`Elsa.Events/EXTENSION_POINTS.md`](../Elsa.Events/EXTENSION_POINTS.md).

**Known implementations (shipped):** see each domain-specific sub-interface's owning feature catalog.

---

## Cross-references

- Repo-wide index: [`../../EXTENSION_POINTS.md`](../../EXTENSION_POINTS.md).
- Constitutional basis: §2.22.1.
