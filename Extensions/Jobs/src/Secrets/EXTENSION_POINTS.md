# Jobs Secrets contributions

`AddJobSecrets(Action<JobSecretsOptions>)` is an optional DI contribution. The
[Core data policy](../Core/IJobDataPolicy.cs) owns the runtime hooks; the
[Secrets resolver](../../../../Essentials/src/Secrets/Core/Contracts/ISecretValueResolver.cs) owns
point-of-use resolution. This adapter adds no durable authority or provider registration.

`JobSecretDeclaration` and `JobSecretReferenceSlot` are immutable host declarations keyed by logical
handler and input schema. The catalog freezes them at registration and contributes one scoped
`IJobDataPolicy`, shared with that scope's `IJobSecrets`. The worker begins it with accepted context
before constructing the handler. The same captured policy instances validate the actual invocation's
progress and candidate result; a reporter never resolves a new unbound policy scope.

`IJobSecrets.ResolveAsync(SecretReference, CancellationToken)` reuses the selected Secrets resolver and
accepted tenant. It exposes no caller tenant, provider error text, global lookup or cross-scope cache.
Admission remains pure and producer-only composition needs no value resolver. Policy refusal has a
fixed Core exception and becomes a nonretryable null-payload worker result or preadmission rejection.

See [binding and disclosure limits](README.md) and the feature's
[acceptance matrix](../../../../specs/1218-standalone-jobs/acceptance-matrix.md) before claiming readiness.
