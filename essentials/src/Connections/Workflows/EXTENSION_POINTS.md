# Connections Workflows Extension Points

## Override

- `IHttpConnectionApplicator`: this feature replaces the unbound default from `ActivitiesHttp` with `HttpConnectionApplicator`, which resolves the bound http connection for the execution partition and applies the matching `IHttpRequestAuthenticator`. Revealed credentials are registered with `IRuntimeSecretMask`.
