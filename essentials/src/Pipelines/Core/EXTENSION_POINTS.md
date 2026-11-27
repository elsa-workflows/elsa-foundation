# Extension points — Pipelines domain

The per-domain catalog (framework §2.22.1). Anchored at `Elsa.Pipelines.Core` — this is the only project in this domain (no separate feature project); the middleware composition engine lives here. One section applies.

---

## Implementable contributor interfaces

### `IMiddleware` *(Core — `Elsa.Pipelines.Core`)*
- **Kind:** Contributor (pipeline middleware step).
- **Signature:** a marker with no members. The engine binds the middleware's one public `Invoke` or `InvokeAsync(TContext context)` method, which returns `ValueTask`, and hands the next step, `PipelineDelegate<TContext> next`, to its constructor.
- **Register:** via `UseMiddleware<TContext, TMiddleware>()` on the pipeline builder (not DI-registered directly). The engine behind it, which finds the `Invoke` method and activates the middleware, is internal.
- **Consumed by:** the pipeline delegate chain `UseMiddleware` composes. There is no single aggregating `IEventHandler`; the pipeline is a composed delegate, not an event-driven fan-in.

**Sub-interfaces (domain-specific pipeline shapes):**
- `IEventMiddleware` *(Core, `Elsa.Events.Core`)*: a middleware step in front of event handler dispatch; see [`Events/Elsa.Events/EXTENSION_POINTS.md`](../../Events/Elsa.Events/EXTENSION_POINTS.md).

The Mediator command and request pipeline also builds on `IMiddleware`, through `IMessageMiddleware`, but it is internal to `Elsa.Mediator` and not an extension point; see [`Mediator/Elsa.Mediator/EXTENSION_POINTS.md`](../../Mediator/Elsa.Mediator/EXTENSION_POINTS.md).

**Known implementations (shipped):** see each domain-specific sub-interface's owning feature catalog.

---

## The shared pipeline builder

`Elsa.Pipelines.Core` now owns the **one** builder implementation used by every per-message pipeline (command, request, event), replacing the three copy-pasted, diverged builders.

### `PipelineDelegate<TContext>` *(Core — `Elsa.Pipelines.Core`)*
- The canonical composed-pipeline delegate: `ValueTask PipelineDelegate<in TContext>(TContext context)`. Each domain closes it over its own context type (`IMessageContext`, `IEventContext`).

### `IPipelineBuilder<TContext>` / `PipelineBuilder<TContext>` *(Core — `Elsa.Pipelines.Core`)*
- **Kind:** The single, message-agnostic middleware composition builder. `Use(...)` appends a component; `Build()` folds the components (reverse registration order, so the first-registered middleware runs first) into one `PipelineDelegate<TContext>`.
- **Setup semantic:** the builder **accumulates** the components handed to it. **Replace** semantics — a fresh composition per `Setup()` — are achieved by constructing a new builder per call, which is the one documented semantic across the codebase's pipelines. See each pipeline's `Setup` and the owning domain `EXTENSION_POINTS.md` (e.g. Mediator's `MessagePipeline.Setup` = REPLACE).
- **Compose middleware:** `builder.UseMiddleware<TContext, TMiddleware>(args)` (in `MiddlewareExtensions`) activates `TMiddleware` with the composed `next` delegate prepended to its constructor and binds its `Invoke`/`InvokeAsync` to `PipelineDelegate<TContext>`.

---

## Cross-references

- Repo-wide index: [`EXTENSION_POINTS.md`](../../../../EXTENSION_POINTS.md).
- Constitutional basis: §2.22.1.
