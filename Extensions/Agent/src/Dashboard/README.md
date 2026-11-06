# Dashboard authoring with Weaver

`StudioDashboardWeaver` contributes one provider-independent authoring implementation over the existing Agent engine. The shell explicitly depends on `FoundationAgentApi` for its advertised bootstrap/session HTTP transport. The host composes `StudioDashboardApi`, its native query owners and durable resource store, canonical `IDashboardActorContext`, the generic Weaver services and one configured Agent provider. The existing [Copilot adapter](../GitHubCopilot/README.md) owns its options and configuration. This module has no provider SDK or credential retrieval code.

The installed context source admits one versioned native question draft and current authorized dataset declarations. It checks the authentication subject against the conversation owner separately from the canonical user used for resource ACLs and query scopes. Saved resources require current read access, disclosed definitions and the exact saved revision. New draft authoring requires current question read and create rights; saved authoring requires the resource's current edit capability.

The three read-only tools discover declarations, propose a validated declarative question, or ask a bounded clarification. Preparations and retained evidence are revalidated against current source support, resource access and the originating resource, editor generation and working revision. Context, guidance, tool arguments and proposals have explicit byte bounds. Oversized discovery requires selecting one dataset in the ordinary editor.

Tool results contain definitions and declarations. They contain no native query result rows or aggregate values. The native authoring facade runs preview as the current canonical viewer after the generic engine returns an authorized proposal; the result is returned only to the native caller. Studio's generic Weaver result renderer separately uses the native preview API. Neither path writes, shares or publishes a saved resource. Use and Save remain explicit native actions.

The [backend tests](../../tests/Dashboard/Tests/DashboardWeaverTests.cs) run the registered generic collector, registry, tool invoker, session store and turn engine with the native query service and a migrated owned SQLite resource store. Only the model, installed data source, identity and host authorization boundaries are deterministic fixtures. These tests establish backend contract behavior and data-disclosure boundaries; they are not live-model or composed Studio UX acceptance.

## Cross-domain contributions

`DashboardWeaverFeature.ConfigureServices` registers `DashboardAuthoringContextProvider` through `TryAddEnumerable<IAgentContextProvider>` for the generic `AgentContextCollecting` / `CollectAgentContext` fan-in. It admits Dashboard-owned declarations/drafts and re-admits retained evidence under current native authority.

`DashboardDatasetTool`, `DashboardQuestionTool` and `DashboardClarificationTool` register through `TryAddEnumerable<IAgentTool>` for `AgentToolsCollecting` / `CollectAgentTools`. Their common `DashboardAuthoringTool` also implements `IAgentToolAuthorization`; the existing invoker/executor calls that companion with current host policy, with no second authorization registry. The [canonical Agent catalog](../Elsa.Agent/EXTENSION_POINTS.md) owns the public signatures, lifetimes and event payload/publication contracts. Dashboard adds no capability fan-in or model orchestration loop.

The owning Studio Dashboard module registers context/prompts/result renderers with the public Studio SDK; its native editor owns explicit Use/Undo/Save and current binding. Actual previews stay outside provider transcripts. Generic Core, host and Copilot adapter contain no Dashboard dataset branches.
