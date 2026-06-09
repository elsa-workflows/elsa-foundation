# Extension points — Mediator domain

The per-domain catalog (framework §2.22.1). Anchored at `Elsa.Mediator` — the composition root where `MediatorFeature` builds the command and request pipelines. Two sections apply.

---

## Implementable contributor interfaces

### `ICommandHandler<TCommand>` / `ICommandHandler<TCommand, TResult>` *(Core — `Elsa.Mediator.Core`)*
- **Kind:** Contributor (handles a specific command type — one handler per command).
- **Signature:** `Task Handle(TCommand command, CancellationToken cancellationToken)` / `Task<TResult> Handle(TCommand command, CancellationToken cancellationToken)`.
- **Register:** `services.AddScoped<ICommandHandler<MyCommand>, MyHandler>()` — one registered handler per command type.
- **Dispatched by:** `CommandHandlerInvokerMiddleware` inside the `CommandPipeline`. The pipeline resolves the single registered `ICommandHandler<TCommand>` at dispatch time.

**Known implementations (shipped):** every command handler in the application — these are application-layer implementations, not domain-fixed. Each command type has exactly one handler; search for `ICommandHandler<` in the codebase for the full list.

### `IRequestHandler<TRequest, TResponse>` *(Core — `Elsa.Mediator.Core`)*
- **Kind:** Contributor (handles a specific request type — one handler per request, returns a response).
- **Signature:** `Task<TResponse> Handle(TRequest request, CancellationToken cancellationToken)`.
- **Register:** `services.AddScoped<IRequestHandler<MyRequest, MyResponse>, MyHandler>()`.
- **Dispatched by:** `RequestHandlerInvokerMiddleware` inside the `RequestPipeline`.

**Known implementations (shipped):** similarly application-defined; search for `IRequestHandler<` for the full list.

---

## Implementable pipeline middleware

### `ICommandMiddleware` *(Core — `Elsa.Mediator.Core`)*
- **Kind:** Pipeline middleware for the command pipeline.
- **Signature:** `ValueTask InvokeAsync(CommandContext context, Func<CommandContext, ValueTask> next)`.
- **Register:** `UseMiddleware<TMiddleware>()` inside `CommandPipeline.Setup()`. Pipeline is built at startup.

### `IRequestMiddleware` *(Core — `Elsa.Mediator.Core`)*
- **Kind:** Pipeline middleware for the request pipeline.
- **Signature:** `ValueTask InvokeAsync(RequestContext context, Func<RequestContext, ValueTask> next)`.
- **Register:** `UseMiddleware<TMiddleware>()` inside `RequestPipeline.Setup()`.

For the base `IMiddleware` shape, see [`Elsa.Pipelines.Core/EXTENSION_POINTS.md`](../Elsa.Pipelines.Core/EXTENSION_POINTS.md).

---

## Cross-references

- Repo-wide index: [`../../EXTENSION_POINTS.md`](../../EXTENSION_POINTS.md).
- Constitutional basis: §2.6.1 + §2.22.1.
