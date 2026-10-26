# Research: Studio base hosting

## Public CShells integration

Decision: use released 0.0.31 APIs at source commit
[b7611aadb49789d3b282749df18ffe68d0eacec5](https://github.com/valence-works/cshells/tree/b7611aadb49789d3b282749df18ffe68d0eacec5),
verified from the installed nuspec/XML and corresponding source by an independent read-only research pass.

- `WithWebRouting` replaces resolver registrations. Configure it first, then a pipeline containing
  `WebRoutingShellResolver` and `UseFallback<StudioSelectedShellFallbackResolver>()`. The fallback returns
  the configured ShellId; no DefaultShellName property exists on these WebRouting options.
- `IShellRegistry.GetBlueprintAsync(name)` returns ProvidedBlueprint; compose its Blueprint to check selected
  identity and explicit EnabledFeatures/DisabledFeatures StudioApi before serving, without activating the shell.
- `IShell.BeginScope()` returns tracked `IShellScope : IAsyncDisposable`. Acquisition may throw
  InvalidOperationException for an already Disposed shell or ObjectDisposedException during a provider race.
  Retry only acquisition once, then propagate failure. Keep the scope across the awaited operation.
- Public `ShareSingletonWithShells<T>` borrows root singleton ownership. No cluster helper or cloned singleton
  container is needed. Resolver strategies are registered singleton by the framework builder.

Alternative rejected: hardcoding Default, returning raw shell.ServiceProvider, catching arbitrary operation
failures for retry, or introducing a local resolver/lifecycle registry.

## Public behavior qualification

Decision: exercise Add/Configure/Use/Map through one test-owned public host factory and real HTTP. Existing
runtime tests call an internal renderer and static tests manually compose middleware; migrate those boundaries
without expanding production visibility. Independent test research identified the current raw-provider manifest
endpoint as the concrete request/reload hazard. Task gates and a scoped disposable can prove retention without sleeps.

Test default and non-default shells with distinguishable decoys, real files (JS/CSS/index), conditional responses,
unknown API404 and secret sentinels. Keep Nuplane asset cases on the optional adapter path. Generic contributed
policy tests establish middleware order here; real Studio bearer/permission/bridge matrix remains #3358.

## Configuration coordination and extraction boundary

Decision: narrow the accepted contract's SemaphoreSlim exposure to an async lease-only coordinator, as recorded
on #3357 after review of #3378. Keep one root instance and alias existing Web writers onto it before optional
extraction. A small private atomic-write helper can remain in Web until its owning adapters move.

Alternative rejected: a second static semaphore, public generic file writer, production InternalsVisibleTo,
or moving auth/Nuplane/theme/frontend implementations into the base package to make extraction convenient.

No unresolved design clarification remains. The accepted contract fixes public composition and package direction;
source verification settles the framework API details.
