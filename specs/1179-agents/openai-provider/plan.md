# Plan

1. Add `Elsa.Agent.OpenAI`, with the official already-pinned OpenAI SDK and MEAI adapter. Register a scoped model owner and stateless singleton connection-type contributor; use the shared connection guard. Keep endpoint configuration an HTTPS origin and append `/v1` only inside the SDK factory.
2. Include the feature in Workbench assembly discovery and default composition, plus the solution and generated filters.
3. Extend existing composition, authorization, physical request, refusal, cancellation and streaming socket cases to direct OpenAI. Verify its Bearer header and request path/model.
4. Create an authorized local connection and a typed example through normal APIs; publish and execute using the actual runtime. Document the resulting setup and evidence.

Constitution check: framework 2.1/2.7/2.20: SDK adapter stays outside Core; 2.5: scoped services, immutable singleton descriptor, virtual feature configuration; 2.16: provider package exception; 2.22: update contributor catalogs and root index; 2.23: internal model owner, meaningful actual-SDK tests; Elsa E2.6: no design dependency in the provider. No provisional constitution section is relied on or ratified.

Review: adapter follows Anthropic/Azure OpenAI's existing capability and transport ownership. No new Core contract, storage or generalization is needed.
