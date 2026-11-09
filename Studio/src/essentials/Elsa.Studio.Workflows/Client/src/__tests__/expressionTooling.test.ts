import { afterEach, describe, expect, it, vi } from "vitest";
import {
  authSessionEndedEvent,
  expressionToolingAuthorizationRevokedEvent,
  expressionToolingAuthorizationRestoredEvent,
  StudioHttpError,
  type StudioEndpointContext
} from "@elsa-workflows/studio-sdk";
import { clearApiCapabilityCache } from "../api/capabilities";
import { createExpressionToolingClient } from "../expression-tooling/expressionToolingClient";

afterEach(clearApiCapabilityCache);

const document = {
  id: "draft-1:activity-1:Text:JavaScript",
  uri: "elsa-expression://draft-1/activity-1/Text/JavaScript",
  draftId: "draft-1",
  activityId: "activity-1",
  propertyKey: "Text",
  expressionType: "JavaScript",
  source: "getName()",
  sourceVersion: 3
};

const links = [
  { rel: "expression-tooling-descriptors", href: "design/workflows/expression-tooling/descriptors" },
  { rel: "expression-tooling-context", href: "design/workflows/expression-tooling/context" },
  { rel: "expression-tooling-symbols", href: "design/workflows/expression-tooling/symbols" },
  { rel: "expression-tooling-completions", href: "design/workflows/expression-tooling/completions" },
  { rel: "expression-tooling-hover", href: "design/workflows/expression-tooling/hover" },
  { rel: "expression-tooling-validate", href: "design/workflows/expression-tooling/validate" }
];

const contextPayload = {
  contractVersion: { major: 1, minor: 0 },
  document: {
    workflowDraftId: "draft-1",
    nodeId: "activity-1",
    propertyKey: "Text",
    expressionType: "JavaScript",
    documentRevision: "3"
  },
  contextRevision: "context-1",
  symbolCatalogRevision: "catalog-1",
  permissionRevision: "permission-1",
  policyFingerprint: "policy-1",
  expectedResultType: "System.String",
  expectedResultShape: {
    kind: "Scalar",
    displayName: "String",
    isNullable: false,
    hasLazyChildren: false
  },
  rootSymbols: [
    {
      symbolId: "input-1",
      name: "customer",
      kind: "WorkflowInput",
      documentation: "Current customer.",
      valueShape: {
        kind: "Object",
        displayName: "Customer",
        isNullable: false,
        members: [{
          name: "address",
          shape: {
            kind: "Object",
            displayName: "Address",
            isNullable: false,
            members: [{
              name: "city",
              shape: { kind: "Scalar", displayName: "String", isNullable: false }
            }]
          }
        }]
      }
    },
    { symbolId: "function-1", name: "getName", kind: "Function", signatures: [{ display: "getName()" }] }
  ],
  capabilities: {
    supportsCompletions: true,
    supportsHover: true,
    supportsValidation: true,
    supportsSymbolPaging: true,
    supportsLazyMembers: false,
    maximumSymbols: 500
  }
};

const activityContextPayload = { ...contextPayload, document: { ...contextPayload.document, workflowDraftId: "", owner: { kind: "activity-draft", id: "draft-1" } } };

function outcome(payload: unknown, state: unknown = "Success", documentRevision = "3", contextRevision = "context-1") {
  return {
    result: {
      state,
      contractVersion: { major: 1, minor: 0 },
      documentRevision,
      contextRevision,
      payload
    }
  };
}

function numericOutcome(
  payload: unknown,
  state: number,
  documentRevision = "3",
  contextRevision = "context-1",
  major = 1
) {
  return {
    result: {
      state,
      contractVersion: { major, minor: 0 },
      documentRevision,
      contextRevision,
      payload
    }
  };
}

function createContext(postJson?: ReturnType<typeof vi.fn>, activity = false) {
  const getJson = vi.fn(async (url: string) => {
    if (url === "/capabilities") {
      return { capabilities: [{ id: activity ? "activity-expressions.tooling.v1" : "expressions.tooling.v1", contractVersion: "1",
        links: activity ? links.map(link => ({ ...link, href: link.href.replace("design/workflows/", "design/activities/") })) : links }] };
    }
    if (url.endsWith("/descriptors")) {
      return outcome([{
        expressionType: "JavaScript",
        moduleId: "Elsa.Expressions.JavaScript",
        moduleVersion: "1.0.0.0",
        contractVersion: { major: 1, minor: 0 },
        capabilities: contextPayload.capabilities
      }], "Success", "descriptors", "descriptors");
    }
    throw new Error(`Unexpected GET ${url}`);
  });
  const post = postJson ?? vi.fn(async (url: string, body?: { documentRevision?: string; contextRevision?: string }) => {
    const documentRevision = body?.documentRevision ?? "3";
    const contextRevision = body?.contextRevision ?? "context-1";
    if (url.endsWith("/context")) return outcome(activity ? activityContextPayload : contextPayload, "Success", documentRevision);
    if (url.endsWith("/symbols")) {
      return outcome({ items: [{ label: "customer", documentation: "Current customer.", kind: "WorkflowInput" }] }, "Success", documentRevision, contextRevision);
    }
    if (url.endsWith("/completions")) {
      return outcome({ items: [{ label: "getName", insertText: "getName()", kind: "Function" }] }, "Success", documentRevision, contextRevision);
    }
    if (url.endsWith("/hover")) return outcome({ contents: "Gets the current name." }, "Success", documentRevision, contextRevision);
    if (url.endsWith("/validate")) return outcome({ diagnostics: [] }, "SupportedEmpty", documentRevision, contextRevision);
    throw new Error(`Unexpected POST ${url}`);
  });
  return {
    context: {
      baseUrl: `test://expression-tooling-${Math.random()}`,
      http: { getJson, postJson: post }
    } as unknown as StudioEndpointContext,
    getJson,
    postJson: post
  };
}

function client(context: StudioEndpointContext, authorizationScope?: string) {
  return createExpressionToolingClient(context, {
    backend: context.baseUrl,
    subject: "author-1",
    tenantId: "tenant-1"
  }, authorizationScope);
}

describe("expression tooling transport", () => {
  it("uses the activity-owned capability and activity draft location for every operation", async () => {
    const api = createContext(undefined, true);
    const tooling = createExpressionToolingClient(api.context, { backend: api.context.baseUrl, subject: "author", tenantId: "tenant" }, "activity-session", "activity-draft");
    await expect(tooling.describe()).resolves.toMatchObject({ state: "ready" });
    const resolved = await tooling.getAuthoringContext(document, { rootSymbols: [{ name: "injected" }] });
    expect(resolved.state).toBe("ready");
    await tooling.getCatalog(document, resolved.data!);
    await tooling.getCompletions(document, resolved.data!, { line: 0, column: 1 });
    await tooling.getHover(document, resolved.data!, { line: 0, column: 1 });
    await tooling.validate(document, resolved.data!);
    expect(api.postJson).toHaveBeenCalledTimes(5);
    for (const [path, body] of api.postJson.mock.calls) {
      expect(path).toMatch(/^\/?design\/activities\/expression-tooling\//);
      expect(body).toMatchObject({ activityDraftId: "draft-1", nodeId: "activity-1", propertyKey: "Text" });
      expect(body).not.toHaveProperty("workflowDraftId");
      expect(body).not.toHaveProperty("rootSymbols");
    }
    tooling.dispose();
  });

  it("does not fall back to workflow tooling when activity capability is absent", async () => {
    const api = createContext();
    const tooling = createExpressionToolingClient(api.context, { backend: api.context.baseUrl, subject: "author", tenantId: "tenant" }, "activity-session", "activity-draft");
    await expect(tooling.getAuthoringContext(document, {})).resolves.toMatchObject({ state: "unavailable" });
    expect(api.postJson).not.toHaveBeenCalled();
    tooling.dispose();
  });

  it("retains typed producer identity without parsing an opaque symbol ID", async () => {
    const binding = { kind: "ActivityResult", producerNodeId: "producer:with:colons", projectionKey: "result:key", isOptional: false };
    const api = createContext(vi.fn(async () => outcome({ ...activityContextPayload, rootSymbols: [{
      symbolId: "opaque:does:not:encode:the:producer", name: "Earlier sum", kind: "ActivityResult", parameterBinding: binding
    }] })), true);
    const tooling = createExpressionToolingClient(api.context, { backend: api.context.baseUrl, subject: "author", tenantId: "tenant" }, "session", "activity-draft");
    const resolved = await tooling.getAuthoringContext(document, {});
    expect(resolved.data?.visibleActivityOutputs).toEqual([expect.objectContaining({ parameterBinding: binding })]);
    tooling.dispose();
  });

  it.each([
    { kind: "Literal", value: "private" },
    { kind: "ActivityResult", producerNodeId: "", projectionKey: "result" },
    { kind: "ActivityResult", producerNodeId: "producer", projectionKey: null },
    { kind: "ActivityResult", producerNodeId: "producer", projectionKey: "result", isOptional: "false" },
    ["ActivityResult", "producer", "result"]
  ])("refuses malformed or unknown-kind result reference metadata: %j", async parameterBinding => {
    const api = createContext(vi.fn(async () => outcome({ ...contextPayload, rootSymbols: [{
      symbolId: "opaque", name: "Unavailable result", kind: "ActivityResult", parameterBinding
    }] })));
    const resolved = await client(api.context).getAuthoringContext(document, {});
    expect(resolved.data?.visibleActivityOutputs[0]?.parameterBinding).toBeUndefined();
  });

  it.each([100, 500])("finds a selectable typed result after %i input/variable symbols using context pages", async preceding => {
    const symbols = Array.from({ length: preceding }, (_, index) => ({ symbolId: `variable:${index}`, name: `Variable${index}`, kind: "Variable" }));
    const binding = { kind: "ActivityResult", producerNodeId: "producer:colon", projectionKey: "result:key", isOptional: false };
    const all = [...symbols, { symbolId: "opaque:result", name: "Earlier sum", kind: "ActivityResult", parameterBinding: binding }];
    const post = vi.fn(async (_path: string, request: { skip?: number; take?: number }) => outcome({ ...activityContextPayload,
      rootSymbols: all.slice(request.skip ?? 0, (request.skip ?? 0) + (request.take ?? 100)) }));
    const api = createContext(post, true);
    const tooling = createExpressionToolingClient(api.context, { backend: api.context.baseUrl, subject: "author", tenantId: "tenant" }, "session", "activity-draft");
    const resolved = await tooling.getAuthoringContext(document, {});
    expect(resolved.data?.visibleActivityOutputs).toEqual([expect.objectContaining({ parameterBinding: binding })]);
    expect(resolved.data?.rootSymbols).toHaveLength(preceding + 1);
    expect(post.mock.calls[0][1]).toMatchObject({ take: 500, skip: 0 });
    if (preceding === 500) expect(post.mock.calls[1][1]).toMatchObject({ take: 500, skip: 500, contextRevision: "context-1" });
    tooling.dispose();
  });

  it.each(["Unauthorized", "Stale", "Canceled"])("discards all earlier context pages when a later page is %s", async state => {
    const page = Array.from({ length: 500 }, (_, index) => ({ symbolId: `symbol${index}`, name: `Symbol${index}`, kind: "Variable" }));
    const post = vi.fn(async (_path: string, request: { skip: number }) => request.skip === 0
      ? outcome({ ...contextPayload, rootSymbols: page }) : outcome(undefined, state));
    const api = createContext(post);
    const resolved = await client(api.context).getAuthoringContext(document, {});
    expect(resolved.state).toBe(state.toLowerCase());
    expect(resolved.data).toBeUndefined();
    expect(post).toHaveBeenCalledTimes(2);
  });

  it("cancels between context pages without sending another page or exposing partial metadata", async () => {
    const controller = new AbortController();
    const page = Array.from({ length: 500 }, (_, index) => ({ symbolId: `symbol${index}`, name: `Symbol${index}`, kind: "Variable" }));
    const post = vi.fn(async () => { controller.abort(); return outcome({ ...contextPayload, rootSymbols: page }); });
    const api = createContext(post);
    const resolved = await client(api.context).getAuthoringContext(document, {}, controller.signal);
    expect(resolved.state).toBe("canceled");
    expect(resolved.data).toBeUndefined();
    expect(post).toHaveBeenCalledTimes(1);
  });

  it("discards a malformed symbol page instead of mistaking filtered rows for catalog termination", async () => {
    const roots = Array.from({ length: 500 }, (_, index) => ({ symbolId: `symbol${index}`, name: `Symbol${index}`, kind: "Variable" }));
    const api = createContext(vi.fn(async () => outcome({ ...contextPayload, rootSymbols: [...roots.slice(0, -1), { symbolId: "missing-name" }] })));
    const resolved = await client(api.context).getAuthoringContext(document, {});
    expect(resolved.state).toBe("incompatible");
    expect(resolved.data).toBeUndefined();
  });

  it("rejects a repeated actual result tuple across pages while preserving distinct opaque identities", async () => {
    const binding = { kind: "ActivityResult", producerNodeId: "producer", projectionKey: "result", isOptional: false };
    const page = [{ symbolId: "first-id", name: "Earlier", kind: "ActivityResult", parameterBinding: binding },
      ...Array.from({ length: 499 }, (_, index) => ({ symbolId: `v${index}`, name: `V${index}`, kind: "Variable" }))];
    const api = createContext(vi.fn(async (_path: string, request: { skip: number }) => outcome({ ...contextPayload,
      rootSymbols: request.skip === 0 ? page : [{ symbolId: "another-id", name: "Repeated", kind: "ActivityResult", parameterBinding: binding }] })));
    const resolved = await client(api.context).getAuthoringContext(document, {});
    expect(resolved.state).toBe("incompatible");
    expect(resolved.data).toBeUndefined();
  });

  it("refuses another document owner or location on a later context page", async () => {
    const page = Array.from({ length: 500 }, (_, index) => ({ symbolId: `v${index}`, name: `V${index}`, kind: "Variable" }));
    const api = createContext(vi.fn(async (_path: string, request: { skip: number }) => outcome({ ...contextPayload,
      document: request.skip === 0 ? contextPayload.document : { ...contextPayload.document, workflowDraftId: "another-draft" },
      rootSymbols: request.skip === 0 ? page : [] })));
    const resolved = await client(api.context).getAuthoringContext(document, {});
    expect(resolved.state).toBe("incompatible");
    expect(resolved.data).toBeUndefined();
  });

  it("refuses changed revision metadata between context pages", async () => {
    const page = Array.from({ length: 500 }, (_, index) => ({ symbolId: `symbol${index}`, name: `Symbol${index}`, kind: "Variable" }));
    const api = createContext(vi.fn(async (_path: string, request: { skip: number }) => outcome({ ...contextPayload,
      rootSymbols: request.skip === 0 ? page : [], permissionRevision: request.skip === 0 ? "permission-1" : "permission-2" })));
    const resolved = await client(api.context).getAuthoringContext(document, {});
    expect(resolved).toMatchObject({ state: "stale" });
    expect(resolved.data).toBeUndefined();
  });

  it("bounds context paging and reports unavailable instead of claiming a truncated empty result list", async () => {
    const page = Array.from({ length: 500 }, (_, index) => ({ symbolId: `symbol${index}`, name: `Symbol${index}`, kind: "Variable" }));
    const post = vi.fn(async () => outcome({ ...contextPayload, rootSymbols: page }));
    const api = createContext(post);
    const resolved = await client(api.context).getAuthoringContext(document, {});
    expect(resolved.state).toBe("unavailable");
    expect(resolved.data).toBeUndefined();
    expect(post).toHaveBeenCalledTimes(11);
  });

  it("keeps metadata caches distinct for authoritative locations even when client document IDs collide", async () => {
    const api = createContext();
    const tooling = client(api.context);
    const context = { version: "context-1", workflowInputs: [], visibleVariables: [], visibleActivityOutputs: [] };
    await tooling.getCatalog(document, context);
    await tooling.getCatalog({ ...document, activityId: "another:node", propertyKey: "another:key" }, context);
    await tooling.getCatalog(document, context);
    expect(api.postJson).toHaveBeenCalledTimes(2);
    expect(api.postJson.mock.calls[1][1]).toMatchObject({ nodeId: "another:node", propertyKey: "another:key" });
    tooling.dispose();
  });

  it("describes provider-owned capabilities before a document context is requested", async () => {
    const api = createContext();

    await expect(client(api.context).describe()).resolves.toMatchObject({
      state: "ready",
      data: [{
        expressionType: "JavaScript",
        moduleId: "Elsa.Expressions.JavaScript",
        moduleVersion: "1.0.0.0",
        contractMinVersion: 1,
        contractMaxVersion: 1,
        capabilities: {
          highlighting: false,
          completion: true,
          hover: true,
          signatures: false,
          formatting: false,
          localDiagnostics: false,
          semanticValidation: true
        }
      }]
    });
  });

  it("uses the additive Foundation capability and caches only permission-scoped symbol metadata", async () => {
    const api = createContext();
    const tooling = client(api.context);

    const contextResult = await tooling.getAuthoringContext(document, { ignoredClientState: true });
    expect(contextResult).toMatchObject({
      state: "ready",
      contextVersion: "context-1",
      data: {
        version: "context-1",
        catalogVersion: "catalog-1",
        permissionRevision: "permission-1",
        hostPolicyRevision: "policy-1",
        capabilities: {
          completion: true,
          hover: true,
          semanticValidation: true
        },
        expectedResultType: "System.String",
        expectedResultShape: {
          kind: "scalar",
          displayName: "String",
          nullable: false
        },
        rootSymbols: [{ name: "customer" }, { name: "getName" }],
        workflowInputs: [{ name: "customer" }]
      }
    });
    await expect(tooling.getCatalog(document, contextResult.data!, "cust")).resolves.toMatchObject({
      state: "ready",
      data: { symbols: [{ name: "customer" }] }
    });
    const customer = contextResult.data!.workflowInputs[0];
    const customerShape = await tooling.getValueShape(document, contextResult.data!, customer.shapeId!);
    expect(customerShape).toMatchObject({
      state: "ready",
      data: {
        kind: "object",
        members: [{ name: "address" }]
      }
    });
    const addressShape = await tooling.getValueShape(
      document,
      contextResult.data!,
      customerShape.data!.members[0].shapeId
    );
    expect(addressShape).toMatchObject({
      state: "ready",
      data: { members: [{ name: "city" }] }
    });
    await tooling.getCatalog(document, contextResult.data!, "cust");

    expect(api.postJson.mock.calls.filter(([url]) => String(url).endsWith("/symbols"))).toHaveLength(1);
    expect(api.postJson).toHaveBeenCalledWith(
      "/design/workflows/expression-tooling/context",
      expect.objectContaining({
        contractVersion: { major: 1, minor: 0 },
        workflowDraftId: "draft-1",
        nodeId: "activity-1",
        documentRevision: "3"
      }),
      { signal: undefined }
    );
  });

  it("keeps same-language catalog requests bound to their explicit document and context revisions", async () => {
    const api = createContext();
    const tooling = client(api.context);
    const secondDocument = {
      ...document,
      id: "draft-1:activity-2:Text:JavaScript",
      uri: "elsa-expression://draft-1/activity-2/Text/JavaScript",
      activityId: "activity-2",
      source: "getOtherName()",
      sourceVersion: 7
    };
    const firstContext = { version: "context-1", workflowInputs: [], visibleVariables: [], visibleActivityOutputs: [] };
    const secondContext = { version: "context-2", workflowInputs: [], visibleVariables: [], visibleActivityOutputs: [] };

    await tooling.getCatalog(document, firstContext, "customer");
    await tooling.getCatalog(secondDocument, secondContext, "customer");
    await tooling.getCatalog(document, firstContext, "customer");
    await tooling.getCatalog(secondDocument, secondContext, "customer");

    const symbolRequests = api.postJson.mock.calls.filter(([url]) => String(url).endsWith("/symbols"));
    expect(symbolRequests).toHaveLength(2);
    expect(symbolRequests.map(([, body]) => body)).toEqual(expect.arrayContaining([
      expect.objectContaining({ nodeId: "activity-1", documentRevision: "3", contextRevision: "context-1", expressionType: "JavaScript" }),
      expect.objectContaining({ nodeId: "activity-2", documentRevision: "7", contextRevision: "context-2", expressionType: "JavaScript" })
    ]));
  });

  it("does not reuse a catalog across permission or Host Policy revisions", async () => {
    const api = createContext();
    const tooling = client(api.context);
    const baseContext = {
      version: "context-1",
      catalogVersion: "catalog-1",
      workflowInputs: [],
      visibleVariables: [],
      visibleActivityOutputs: []
    };

    await tooling.getCatalog(document, { ...baseContext, permissionRevision: "permission-1", hostPolicyRevision: "policy-1" });
    await tooling.getCatalog(document, { ...baseContext, permissionRevision: "permission-2", hostPolicyRevision: "policy-2" });

    expect(api.postJson.mock.calls.filter(([url]) => String(url).endsWith("/symbols"))).toHaveLength(2);
  });

  it("maps a missing optional relation to unavailable without issuing a domain request", async () => {
    const getJson = vi.fn().mockResolvedValue({
      capabilities: [{ id: "expressions.tooling.v1", contractVersion: "1", links: [] }]
    });
    const context = {
      baseUrl: "test://missing-tooling",
      http: { getJson, postJson: vi.fn() }
    } as unknown as StudioEndpointContext;

    await expect(client(context).getAuthoringContext(document, {})).resolves.toMatchObject({ state: "unavailable" });
    expect(getJson).toHaveBeenCalledOnce();
    expect(context.http.postJson).not.toHaveBeenCalled();
  });

  it("maps authorization explicitly and purges cached symbols", async () => {
    const authorizationEnded = vi.fn();
    const toolingAuthorizationRevoked = vi.fn();
    const toolingAuthorizationRestored = vi.fn();
    window.addEventListener(authSessionEndedEvent, authorizationEnded);
    window.addEventListener(expressionToolingAuthorizationRevokedEvent, toolingAuthorizationRevoked);
    window.addEventListener(expressionToolingAuthorizationRestoredEvent, toolingAuthorizationRestored);
    let symbolsCalls = 0;
    const api = createContext(vi.fn(async (url: string) => {
      if (url.endsWith("/context")) return outcome(contextPayload);
      if (url.endsWith("/symbols")) {
        symbolsCalls++;
        if (symbolsCalls === 1) return outcome({ items: [{ label: "customer" }] });
        throw new StudioHttpError(403, "hidden symbol");
      }
      throw new Error(`Unexpected POST ${url}`);
    }));
    const tooling = client(api.context, "workflow-editor-1");

    const contextResult = await tooling.getAuthoringContext(document, {});
    expect(contextResult.data).toBeDefined();
    await expect(tooling.getCatalog(document, contextResult.data!, "cust")).resolves.toMatchObject({ state: "ready" });
    tooling.invalidateAuthorization();
    const refreshedContext = await tooling.getAuthoringContext(document, {});
    await expect(tooling.getCatalog(document, refreshedContext.data!, "cust")).resolves.toMatchObject({ state: "unauthorized" });
    expect(symbolsCalls).toBe(2);
    expect(authorizationEnded).not.toHaveBeenCalled();
    expect(toolingAuthorizationRevoked).toHaveBeenCalledOnce();
    expect((toolingAuthorizationRevoked.mock.calls[0]?.[0] as CustomEvent).detail).toEqual({
      scope: "workflow-editor-1"
    });

    const postCallsAfterRevocation = api.postJson.mock.calls.length;
    await expect(tooling.getAuthoringContext(document, {})).resolves.toMatchObject({ state: "unauthorized" });
    await expect(tooling.validate(document, refreshedContext.data!)).resolves.toMatchObject({ state: "unauthorized" });
    expect(api.postJson).toHaveBeenCalledTimes(postCallsAfterRevocation);

    tooling.restoreAuthorization?.();
    await expect(tooling.validate(document, refreshedContext.data!)).resolves.toMatchObject({ state: "unauthorized" });
    await expect(tooling.getAuthoringContext(document, {})).resolves.toMatchObject({ state: "ready" });
    expect(api.postJson).toHaveBeenCalledTimes(postCallsAfterRevocation + 1);
    expect(toolingAuthorizationRestored).not.toHaveBeenCalled();

    window.removeEventListener(authSessionEndedEvent, authorizationEnded);
    window.removeEventListener(expressionToolingAuthorizationRevokedEvent, toolingAuthorizationRevoked);
    window.removeEventListener(expressionToolingAuthorizationRestoredEvent, toolingAuthorizationRestored);
  });

  it("keeps a restored scope revoked until a fresh authoring context is authorized", async () => {
    const toolingAuthorizationRestored = vi.fn();
    window.addEventListener(expressionToolingAuthorizationRestoredEvent, toolingAuthorizationRestored);
    const api = createContext(vi.fn(async (url: string) => {
      if (url.endsWith("/context")) throw new StudioHttpError(403, "context remains hidden");
      throw new Error(`Unexpected POST ${url}`);
    }));
    const tooling = client(api.context, "workflow-editor-denied");

    await expect(tooling.getAuthoringContext(document, {})).resolves.toMatchObject({ state: "unauthorized" });
    tooling.restoreAuthorization?.();
    await expect(tooling.getAuthoringContext(document, {})).resolves.toMatchObject({ state: "unauthorized" });

    expect(toolingAuthorizationRestored).not.toHaveBeenCalled();
    expect(api.postJson.mock.calls.filter(([url]) => String(url).endsWith("/context"))).toHaveLength(2);
    window.removeEventListener(expressionToolingAuthorizationRestoredEvent, toolingAuthorizationRestored);
  });

  it("forwards source-aware completion, hover, validation, positions, and cancellation signals", async () => {
    const api = createContext();
    const tooling = client(api.context);
    const controller = new AbortController();
    const resolved = await tooling.getAuthoringContext(document, {}, controller.signal);
    const authoringContext = resolved.data!;

    await expect(tooling.getCompletions(
      document,
      authoringContext,
      { line: 0, column: 4 },
      controller.signal
    )).resolves.toMatchObject({
      state: "ready",
      data: { items: [{ label: "getName", insertText: "getName()" }] }
    });
    await expect(tooling.getHover(
      document,
      authoringContext,
      { line: 0, column: 4 },
      controller.signal
    )).resolves.toMatchObject({
      state: "ready",
      data: { contents: "Gets the current name." }
    });
    await expect(tooling.validate(document, authoringContext, controller.signal)).resolves.toMatchObject({
      state: "supported-empty",
      data: { documentId: document.id, sourceVersion: 3, diagnostics: [] }
    });

    expect(api.postJson).toHaveBeenCalledWith(
      "/design/workflows/expression-tooling/completions",
      expect.objectContaining({
        source: "getName()",
        contextRevision: "context-1",
        cursor: { line: 0, character: 4 }
      }),
      { signal: controller.signal }
    );
    expect(api.postJson).toHaveBeenCalledWith(
      "/design/workflows/expression-tooling/hover",
      expect.objectContaining({ position: { line: 0, character: 4 } }),
      { signal: controller.signal }
    );
    expect(api.postJson).toHaveBeenCalledWith(
      "/design/workflows/expression-tooling/validate",
      expect.objectContaining({ source: "getName()" }),
      { signal: controller.signal }
    );
  });

  it("keeps stale and canceled outcomes explicit and data-free", async () => {
    const staleApi = createContext(vi.fn(async () => outcome(null, "Stale")));
    const stale = await client(staleApi.context).getAuthoringContext(document, {});
    expect(stale).toMatchObject({ state: "stale" });
    expect(stale).not.toHaveProperty("data");

    const canceledApi = createContext(vi.fn(async () => {
      throw new DOMException("Canceled", "AbortError");
    }));
    await expect(client(canceledApi.context).getAuthoringContext(document, {})).resolves.toMatchObject({
      state: "canceled"
    });
  });

  it("rejects successful payloads whose document or context revision does not match the request", async () => {
    const api = createContext(vi.fn(async (url: string) => {
      if (url.endsWith("/context")) return outcome(contextPayload, "Success", "3");
      return outcome({ items: [{ label: "stale" }] }, "Success", "2", "old-context");
    }));
    const tooling = client(api.context);
    const resolved = await tooling.getAuthoringContext(document, {});

    await expect(tooling.getCompletions(
      document,
      resolved.data!,
      { line: 0, column: 1 }
    )).resolves.toMatchObject({ state: "stale" });
  });

  it("parses contract-v1 numeric enum values in authoring context and validation payloads", async () => {
    const symbols = Array.from({ length: 9 }, (_, kind) => ({
      symbolId: `wire-${kind}`,
      name: `symbol-${kind}`,
      kind,
      valueShape: {
        kind: kind < 7 ? kind : 0,
        displayName: `Shape${kind}`,
        isNullable: false,
        ...(kind === 3 ? { item: { kind: 1, displayName: "Item", isNullable: false } } : {})
      }
    }));
    const numericContextPayload = {
      ...contextPayload,
      expectedResultShape: { ...contextPayload.expectedResultShape, kind: 1 },
      rootSymbols: symbols
    };
    const api = createContext(vi.fn(async (url: string) => {
      if (url.endsWith("/context")) return numericOutcome(numericContextPayload, 1);
      if (url.endsWith("/validate")) return numericOutcome({
        diagnostics: [
          {
            code: "EXPR001",
            severity: 0,
            message: "Unknown name.",
            range: { start: { line: 0, character: 1 }, end: { line: 0, character: 4 } }
          },
          {
            code: "EXPR002",
            severity: 1,
            message: "Prefer a declared input.",
            range: { start: { line: 0, character: 5 }, end: { line: 0, character: 8 } }
          }
        ]
      }, 0);
      throw new Error(`Unexpected POST ${url}`);
    }));
    const tooling = client(api.context);
    const contextResult = await tooling.getAuthoringContext(document, {});

    expect(contextResult).toMatchObject({
      state: "supported-empty",
      contextVersion: "context-1",
      data: {
        expectedResultShape: { kind: "scalar" },
        rootSymbols: [
          { id: "Variable:wire-0", kind: "value" },
          { id: "WorkflowInput:wire-1", kind: "value" },
          { id: "ActivityResult:wire-2", kind: "value" },
          { id: "Function:wire-3", kind: "function" },
          { id: "Filter:wire-4", kind: "filter" },
          { id: "Tag:wire-5", kind: "tag" },
          { id: "Namespace:wire-6", kind: "namespace" },
          { id: "Member:wire-7", kind: "member" },
          { id: "Extension:wire-8", kind: "function" }
        ],
        workflowInputs: [{ name: "symbol-1" }],
        visibleVariables: [{ name: "symbol-0" }],
        visibleActivityOutputs: [{ name: "symbol-2" }]
      }
    });

    const shapes = await Promise.all(symbols.slice(0, 7).map(async (_symbol, kind) => {
      const shapeId = contextResult.data!.rootSymbols![kind]!.shapeId!;
      return (await tooling.getValueShape(document, contextResult.data!, shapeId)).data?.kind;
    }));
    expect(shapes).toEqual(["unknown", "scalar", "object", "collection", "object", "callable", "object"]);
    await expect(tooling.getValueShape(
      document,
      contextResult.data!,
      contextResult.data!.rootSymbols![3]!.shapeId! + ":item"
    )).resolves.toMatchObject({ state: "ready", data: { kind: "scalar", displayName: "Item" } });

    await expect(tooling.validate(document, contextResult.data!)).resolves.toMatchObject({
      state: "ready",
      data: {
        diagnostics: [
          { severity: "error", code: "EXPR001", message: "Unknown name." },
          { severity: "warning", code: "EXPR002", message: "Prefer a declared input." }
        ]
      }
    });
  });

  it.each([
    [2, "unavailable"],
    [3, "unauthorized"],
    [4, "incompatible"],
    [5, "stale"],
    [6, "canceled"]
  ] as const)("maps numeric outcome state %i to %s", async (wireState, expectedState) => {
    const api = createContext(vi.fn(async () => numericOutcome(undefined, wireState)));
    await expect(client(api.context).getAuthoringContext(document, {})).resolves.toMatchObject({ state: expectedState });
  });

  it.each([7, -1, 1.5, "FutureState"])("keeps unknown outcome state %s incompatible", async wireState => {
    const api = createContext(vi.fn(async () => outcome(contextPayload, wireState)));
    await expect(client(api.context).getAuthoringContext(document, {})).resolves.toMatchObject({ state: "incompatible" });
  });

  it("keeps numeric outcomes bound to the supported major and requested revisions", async () => {
    const incompatibleApi = createContext(vi.fn(async () => numericOutcome(contextPayload, 0, "3", "context-1", 2)));
    await expect(client(incompatibleApi.context).getAuthoringContext(document, {})).resolves.toMatchObject({
      state: "incompatible"
    });

    const staleApi = createContext(vi.fn(async (url: string) => {
      if (url.endsWith("/context")) return numericOutcome(contextPayload, 0);
      return numericOutcome({ items: [{ label: "stale" }] }, 0, "2", "old-context");
    }));
    const tooling = client(staleApi.context);
    const resolved = await tooling.getAuthoringContext(document, {});
    await expect(tooling.getCatalog(document, resolved.data!)).resolves.toMatchObject({ state: "stale" });
  });
});
