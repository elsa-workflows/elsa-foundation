# Publishing Authorization and Collectibility Contract

## Authorization matrix

Representative read, manage, tenant/resource-sensitive activity publication, and provider-payload/construction routes plus a retained FastEndpoints canary must cover:

- anonymous caller → 401;
- authenticated trusted caller without action → 403;
- exact read/manage grant → allowed only on matching routes;
- configured implication → evaluator result without adding implication to Publishing's catalog;
- evaluator wildcard → allowed without wildcard in endpoint metadata;
- normalized trusted external claim → expected result;
- authenticated untrusted or ambiguous identities → fail closed;
- absent/mismatched tenant or denied resource → 403 under exact, implied, and wildcard grants;
- cancellation propagation and evaluator/resource-handler fault behavior;
- denial before mediator, store, compiler, publisher, authorizer-dependent service, or test-run operation executes.

Every endpoint carries exactly one standard permission policy naming only `workflow-publishing.read` or `workflow-publishing.manage`, plus Publishing ownership and Minimal authoring metadata.

## Three-cycle owner lifecycle

Each cycle must load the implementation in a collectible context, configure the real feature, map all 23 routes, run authentication/authorization, invoke representative catalog/preflight/policy/publication/slot/test-run delegates, bind and serialize through the owner source-generated context, and generate native API Explorer/OpenAPI in alternating order. It then removes the endpoint generation, invalidates API Explorer, drains/cancels owned work, disposes scopes/providers/stores/test-run resources, unloads, and verifies bounded weak-reference collection.

Track weak references for the load context, owner assembly, feature, mapper/handler types, endpoint delegates/metadata, JSON context/resolver/type info, OpenAPI provider/document, DI provider/scopes, Publishing stores/publishers/compilers/authorization adapters, and workflow/activity test-run/background resources.

The test must also prove deliberate unsafe metadata is rejected before publication and a failed candidate preserves the previous endpoint/document generation. It may not use production forced GC, sleeps/timed eviction, private cache mutation, hidden operations, reflection serialization fallback, or process-memory heuristics as evidence.
