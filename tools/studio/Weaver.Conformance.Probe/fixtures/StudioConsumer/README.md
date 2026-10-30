# Package-only Studio application

`consumers.prepare_consumers(run)` replaces `__STUDIO_REFERENCES__` with exact retained product/infrastructure PackageReferences. The application selects public Hosting, Hosting.Nuplane, Dashboard, Workflows and Weaver.Workflows APIs. It has no resource package, concrete resource type, frontend source link or frontend build step.

The fixed Studio HostId is `weaver-conformance3438`. Authentication is enabled and the integration driver supplies the real owned backend URL through `configure_runtime`; normal native identity/session introspection remains authoritative. The native directory feed, coordinated store, installs and lock file all live under this consumer's private published root. Resource RCL packages arrive through that feed after the common baseline, rather than changing this application's references or Program.

Public `AddElsaStudioNuplane`/`MapElsaStudioNuplane` provide normal package/feature administration. Automatic shell refresh uses Enabled=true, ChangedOrPending and AutoReload=true. The driver must explicitly select enrollment for the owned store. Permission-protected generation/reconciliation observations use the actual shell registry and native admin services. No host is started by preparation.

`run.profiles["studio"]` exposes `published`, `assembly`, `env`, `audit`, `runtimeFeed`, `nativeStore`, `appsettings` and `shells`. Configuration/origin/auth setup, process lifecycle and native module transitions belong to the integration driver. Both provider profiles reuse this same published application; they do not rebuild it. Common baseline/final hashes and frontend-denial audit bytes are retained by the consumer helper and validated separately from runtime cases.
