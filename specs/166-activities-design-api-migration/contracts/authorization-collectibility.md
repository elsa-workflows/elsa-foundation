# Activities Design Authorization and Collectibility Contract

## Authorization

- Each route exposes exactly one catalog-owned read or manage action through standard authorization metadata.
- Wildcard and implication remain evaluator-level compatibility and never appear as module-owned policy actions.
- Anonymous and authenticated-untrusted callers fail closed; a trusted caller without the action is denied.
- Exact, implied, and wildcard grants succeed only when tenant/resource policy succeeds.
- Provider-authoring and provider-payload reads remain distinct inner resource decisions and are proven by payload
  present/redacted plus provider/store invocation assertions.
- A retained FastEndpoints canary and representative Minimal routes use the same policy provider/evaluator.

## Collectibility

Three real cycles MUST, in the same generation:

1. load and configure the public non-sealed feature;
2. map all owner routes and validate stable lifetime metadata;
3. execute authorization and representative catalog, authoring, availability, dependency, and upgrade delegates;
4. bind and serialize through the owner source-generated JSON context;
5. generate native API Explorer/OpenAPI from the mapped endpoints;
6. drain/remove endpoints, dispose scopes/provider, unload the implementation context; and
7. prove endpoint delegates, feature/mapper/context types, auth/provider/store services, serializer state, and
   owner assembly/load context are no longer strongly reachable.

The proof MUST NOT clear private/global caches, sleep for expiry, omit OpenAPI, force GC in production, or accept
process-memory stability as collectibility evidence.
