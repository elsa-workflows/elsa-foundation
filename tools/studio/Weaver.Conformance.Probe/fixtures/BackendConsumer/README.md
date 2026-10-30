# Package-only backend application

`consumers.prepare_consumers(run)` replaces `__BACKEND_REFERENCES__` with exact PackageReferences from the retained candidate identities and `run.pin(...)` for public infrastructure. This baseline references both real provider adapters, the public Agent/Identity/Preferences contracts and native authoring services. Neither resource package nor the protocol fixture is an application reference. The actual protocol archive is staged in the native runtime feed.

The application uses public CShells/Nuplane registration, native ModularityApi endpoints, normal Identity/SQLite module wiring and a separate application-owned SQLite/OpenIddict TokenDb. It contains no Workbench helpers or private management mapping. The root Nuplane ingress/coordinator/admin instances are shared into shell scopes. Native-loaded factories share the baseline provider implementation assemblies, in addition to Core/CShells contracts, so they replace the actual adapter's public factory interface.

Root service validation has application-owned feature-store/reload guards that throw on use. The selected ModularityApi feature replaces these with its real shell-owned implementations. The generation observation requires those exact owning types, records their assembly identities and refuses any root guard invocation; these guards never simulate configuration reads or writes.

Preparation builds/publishes only. Before starting the published applications, the integration driver calls:

```python
configure_runtime(run, backend_url=owned_backend_origin,
                  studio_url=owned_studio_origin,
                  admin_username=private_username, admin_password=private_password,
                  provider="copilot", enroll=True)
```

This writes exact CORS/return origins, native initial-admin seed settings and the first-party issuer into private runtime JSON. No password is written to a public receipt. `Qualification:EnrollOwnedStore=true` is an explicit authorization for this owned store's enrollment. The only native credentials are obtained later through normal login cookie and `/_elsa/identity/token`; the consumer does not mint fixture tokens.

`select_protocol_profile(run, "copilot" | "anthropic")` changes only JSON after the driver has stopped its own backend. Exactly one actual provider feature and one corresponding native-loaded fixture feature are enabled; both adapter binaries remain unchanged. CommandFile and provider TraceFile are distinct absolute paths under the published private protocol directory. The driver atomically updates `{commandId,toolName,arguments}` before each new explicit request and keeps it fixed throughout review/continuation.

The application enrolls the isolated coordinated store, starts, and observes actual initial reconciliation before printing `WEAVER_BACKEND_CONSUMER_READY`. Permission-protected `POST /__qualification/reconcile` uses the actual shared Nuplane admin service. `GET /__qualification/generation` reports the actual shell identity, scoped context/tool contributors and shared contract identities/locations. These are fixture observations; native services still own feature configuration, membership and effects.

The optional `Qualification:DiagnosticGateDirectory` pauses immediately before startup and on a startup exception, before application disposal. It must be a fresh absolute directory below the owned content root. Each stage writes `<stage>.ready.json` and waits for `<stage>.release`, for at most 60 seconds. This records the normal public membership observation and typed admission failure without touching native locks. `configure_runtime` clears this setting; ordinary qualification runs with the gate off.

`run.profiles["backend"]` exposes `published`, `assembly`, `env`, `audit`, `runtimeFeed`, `nativeStore`, `appsettings`, `shells`, `commandFile` and `traceFiles` to the integration driver. `freeze_consumer_baseline` runs after real startup and before resource installation. `capture_consumer_after` runs after final restored observations; it refuses changed/missing common DLL/browser files. Build/lock/asset/audit receipts remain separate from runtime case acceptance.
