# Extension points — Mediator domain

The per-domain catalog (framework §2.22.1). Anchored at `Elsa.Mediator` — the composition root where `MediatorFeature` builds the command and request pipelines. Command and request dispatch share **one** unified pipeline/context/middleware/invoker mechanism (`MessagePipeline`, `IMessageContext`, `IMessageMiddleware`, `HandlerInvokerMiddleware`); the `ICommand*`/`IRequest*` families are thin intent-signalling contracts over it. Two sections apply.

---

## Implementable contributor interfaces

The contributor-facing handler contracts are unchanged (review verdict: KEEP). Handlers are registered under **both** their closed generic dispatch interface (what the invoker resolves — so only the one relevant handler is constructed per dispatch) and a non-generic marker (kept for scan/registration-count checks). The registration helpers do this for you.

### `ICommandHandler<TCommand>` / `ICommandHandler<TCommand, TResult>` *(Core — `Elsa.Mediator.Core`)*
- **Kind:** Contributor (handles a specific command type — one handler per command).
- **Signature:** `Task<Unit> Handle(TCommand command, CancellationToken cancellationToken)` (the `<TCommand>` form is `ICommandHandler<TCommand, Unit>`) / `Task<TResult> Handle(TCommand command, CancellationToken cancellationToken)`.
- **Register:** `services.AddCommandHandlersFrom(assembly)` (recommended — registers every closed generic, including the inherited `ICommandHandler<TCommand, Unit>` the invoker resolves, plus the marker). For a single manual registration, target the **closed two-arg** dispatch interface: `services.AddScoped<ICommandHandler<MyCommand, Unit>, MyHandler>()`.
- **Dispatched by:** `HandlerInvokerMiddleware` inside the `CommandPipeline`. The pipeline resolves the single registered closed `ICommandHandler<TCommand, TResult>` at dispatch time and invokes it through a cached compiled delegate.

**Known implementations (shipped):** every command handler in the application — these are application-layer implementations, not domain-fixed. Each command type has exactly one handler; search for `ICommandHandler<` in the codebase for the full list.

### `IRequestHandler<TRequest, TResponse>` *(Core — `Elsa.Mediator.Core`)*
- **Kind:** Contributor (handles a specific request type — one handler per request, returns a response).
- **Signature:** `Task<TResponse> Handle(TRequest request, CancellationToken cancellationToken)`.
- **Register:** `services.AddRequestHandlersFrom(assembly)` (recommended) or `services.AddRequestHandler<MyHandler, MyRequest, MyResponse>()` / `services.AddScoped<IRequestHandler<MyRequest, MyResponse>, MyHandler>()`.
- **Dispatched by:** `HandlerInvokerMiddleware` inside the `RequestPipeline`.

**Known implementations (shipped):** similarly application-defined; search for `IRequestHandler<` for the full list.

---

## Implementable pipeline middleware

### `IMessageMiddleware` *(Core — `Elsa.Mediator.Core`)*
- **Kind:** Pipeline middleware for the unified command/request dispatch pipeline (replaces the former separate `ICommandMiddleware` / `IRequestMiddleware`).
- **Signature:** `ValueTask Invoke(IMessageContext context)` — the composed `next` delegate (`PipelineDelegate<IMessageContext>`) is injected via the constructor.
- **Register:** `UseMiddleware<IMessageContext, TMiddleware>()` inside a `MessagePipeline` subclass's `CreateDefaultPipeline()`/`Setup()`. Pipelines are built once and cached.
- **Shipped implementations:** `HandlerInvokerMiddleware` (resolves + invokes the single handler via a compiled, per-closed-handler-type cached delegate), `LoggingMiddleware` (command pipeline only).

**Dispatch context:** `IMessageContext` / `MessageContext<TResult>` *(Core — `Elsa.Mediator.Core`)* carries the message, the handler open-generic family (`typeof(ICommandHandler<,>)` or `typeof(IRequestHandler<,>)`), the result type, the message-kind noun (used in handler-resolution error text), the caller's `IServiceProvider`, and the result slot.

**Pipeline base:** `MessagePipeline` *(Core — `Elsa.Mediator.Core`)*, subclassed by `CommandPipeline` (invoker + logging) and `RequestPipeline` (invoker only). Both are registered as singletons by `MediatorFeature`.

> **`Setup()` semantic = REPLACE.** Each call to `MessagePipeline.Setup()` composes a **fresh** pipeline from a new `PipelineBuilder<IMessageContext>`, discarding any prior composition. This is the one documented Setup semantic across the codebase's pipelines (command, request, and event). It is safe because nothing outside the domain calls `Setup()` — the default composition is built once and cached on first dispatch.

For the base `IMiddleware` shape and the shared builder, see [`Elsa.Pipelines.Core/EXTENSION_POINTS.md`](../Elsa.Pipelines.Core/EXTENSION_POINTS.md).

---

## Cross-references

- Repo-wide index: [`../../EXTENSION_POINTS.md`](../../EXTENSION_POINTS.md).
- Constitutional basis: §2.6.1 + §2.22.1.
