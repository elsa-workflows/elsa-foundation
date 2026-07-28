import { afterEach, describe, expect, it, vi } from "vitest";
import { StudioHttpError, type StudioEndpointContext } from "@elsa-workflows/studio-sdk";
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
    documentRevision: "server-revision"
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

function outcome(payload: unknown, state = "Success", documentRevision = "3", contextRevision = "context-1") {
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

function createContext(postJson?: ReturnType<typeof vi.fn>) {
  const getJson = vi.fn(async (url: string) => {
    if (url === "/capabilities") {
      return { capabilities: [{ id: "expressions.tooling.v1", contractVersion: "1", links }] };
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
    if (url.endsWith("/context")) return outcome(contextPayload, "Success", documentRevision);
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

function client(context: StudioEndpointContext) {
  return createExpressionToolingClient(context, {
    backend: context.baseUrl,
    subject: "author-1",
    tenantId: "tenant-1"
  });
}

describe("expression tooling transport", () => {
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
    const tooling = client(api.context);

    const contextResult = await tooling.getAuthoringContext(document, {});
    expect(contextResult.data).toBeDefined();
    await expect(tooling.getCatalog(document, contextResult.data!, "cust")).resolves.toMatchObject({ state: "ready" });
    tooling.invalidateAuthorization();
    const refreshedContext = await tooling.getAuthoringContext(document, {});
    await expect(tooling.getCatalog(document, refreshedContext.data!, "cust")).resolves.toMatchObject({ state: "unauthorized" });
    expect(symbolsCalls).toBe(2);
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
});
