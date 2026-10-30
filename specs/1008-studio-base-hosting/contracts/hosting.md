# Hosting contracts

The [accepted contract](../../../docs/reports/studio-hosting-contract.md#public-composition-api-to-implement)
is canonical. This leaf implements its base AddElsaStudio / StudioBuilder.ConfigureShells / UseElsaStudio /
MapElsaStudio composition and StudioHostingOptions. Auth/bridge internals follow in #3358, optional adapters
in #3359/#3360 and prebuilt browser payload ownership in #3361. This is not final program delivery.

Refinement accepted on #3357: StudioBuilder.ConfigurationMutationGate returns a public
StudioConfigurationMutationGate with `ValueTask<IAsyncDisposable> AcquireAsync(CancellationToken = default)`.
It exposes neither the underlying semaphore nor public release/dispose. Constructor is internal; registration
owns the single instance and callers borrow it across whole coordinated changes.

HTTP paths, response JSON and the `window.__ELSA_STUDIO_RUNTIME__` global remain compatible. Manifest endpoints
have priority over shell catchalls; unknown `/_elsa` paths remain 404. Runtime configuration omits all server
credentials and the server-only backend URL. Host static responses and fallback require revalidation.

Caller framework callbacks run after defaults. A callback replacing the resolver pipeline owns the replacement;
plain module-assembly selection retains the selected-shell fallback. StudioApi must be explicitly enabled in
the selected blueprint, including host-owned configuration providers.
