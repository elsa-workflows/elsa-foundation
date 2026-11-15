import { isLosslessNumber } from "lossless-json";
import { StudioHttpError, type StudioHttpClient, type StudioRequestInit } from "@elsa-workflows/studio-sdk";
import { afterEach, describe, expect, it, vi } from "vitest";
import {
  cancelEtlRun,
  classifyEtlApiError,
  clearEtlApiCapabilityCache,
  EtlApiError,
  createEtlConnectorDefinition,
  getEtlConnectorDefinition,
  getEtlConnectorVersion,
  getEtlApiCapability,
  getEtlDefinition,
  listEtlConnectorBindings,
  listEtlConnectorDefinitions,
  listEtlConnectorVersions,
  listEtlBindings,
  listEtlDefinitions,
  listEtlRuns,
  listEtlRunConnectorPartitions,
  publishEtlConnectorDefinition,
  updateEtlConnectorDraft,
  validateEtlConnectorDraft,
  updateEtlDraft
} from "../etlApi";
import { formatEtlJsonNumber, type EtlApiCapabilityDocument, type EtlJsonNumber, type EtlPipelineV2 } from "../etlTypes";

const designRelations = [
  ["etl-bindings", "etl/bindings"],
  ["etl-definitions", "etl/definitions"],
  ["etl-definition", "etl/definitions/{id}"],
  ["etl-draft", "etl/definitions/{id}/draft"],
  ["etl-validation", "etl/definitions/{id}/validate"],
  ["etl-publication", "etl/definitions/{id}/publish"],
  ["etl-versions", "etl/definitions/{id}/versions"],
  ["etl-version", "etl/definitions/{id}/versions/{version}"]
] as const;

const runtimeRelations = [
  ["etl-runs", "etl/runs"],
  ["etl-run", "etl/runs/{id}"],
  ["etl-run-partitions", "etl/runs/{id}/partitions"],
  ["etl-run-cancel", "etl/runs/{id}/cancel"]
] as const;

const runtimeConnectorPartitionRelation = ["etl-run-connector-partitions", "etl/runs/{id}/connector-partitions"] as const;

const connectorDesignRelations = [
  ["etl-v2-bindings", "etl/v2/bindings"],
  ["etl-v2-definitions", "etl/v2/definitions"],
  ["etl-v2-definition", "etl/v2/definitions/{id}"],
  ["etl-v2-draft", "etl/v2/definitions/{id}/draft"],
  ["etl-v2-validation", "etl/v2/definitions/{id}/validate"],
  ["etl-v2-publication", "etl/v2/definitions/{id}/publish"],
  ["etl-v2-versions", "etl/v2/definitions/{id}/versions"],
  ["etl-v2-version", "etl/v2/definitions/{id}/versions/{version}"]
] as const;

describe("ETL Studio API client", () => {
  afterEach(() => {
    vi.restoreAllMocks();
    clearEtlApiCapabilityCache();
  });

  it("preserves full Int64 limits and numeric filter literals when reading and saving a draft", async () => {
    const int64 = "9223372036854775807";
    const decimal = "0.1234567890123456789012345678";
    const pipelineResponse = `{"definitionId":"numbers","revision":2,"lastPublishedVersion":1,"updatedAt":"2026-10-20T10:00:00Z","pipeline":{"limits":{"maxSnapshotBytes":${int64},"maxFields":256},"filters":[{"field":"count","comparison":"equal","value":${int64}},{"field":"amount","comparison":"notEqual","value":${decimal}},{"field":"optional","comparison":"equal","value":null},{"field":"label","comparison":"equal","value":"Ada"},{"field":"enabled","comparison":"equal","value":true}]}}`;
    const context = endpointContext({
      requestResponse: vi.fn(async () => new Response(pipelineResponse, { status: 200 })),
      getJson: async () => capabilities()
    });

    const draft = await getEtlDefinition(context, "numbers/with space");
    expect(context.http.requestResponse).toHaveBeenCalledWith(
      "/studio/v2/etl/definitions/numbers%2Fwith%20space",
      expect.objectContaining({ method: "GET" })
    );
    expect(String(draft.pipeline.limits.maxSnapshotBytes)).toBe(int64);
    expect(draft.pipeline.limits.maxFields).toBe(256);
    const [int64Filter, decimalFilter, nullFilter, stringFilter, boolFilter] = draft.pipeline.filters;
    expect(isLosslessNumber(int64Filter.value)).toBe(true);
    expect(formatEtlJsonNumber(int64Filter.value as EtlJsonNumber)).toBe(int64);
    expect(formatEtlJsonNumber(decimalFilter.value as EtlJsonNumber)).toBe(decimal);
    expect(nullFilter.value).toBeNull();
    expect(stringFilter.value).toBe("Ada");
    expect(boolFilter.value).toBe(true);

    await updateEtlDraft(context, "numbers", { expectedRevision: 2, pipeline: draft.pipeline });
    const requestResponse = context.http.requestResponse!;
    const rawRequest = vi.mocked(requestResponse).mock.calls[1][1];
    expect(rawRequest?.method).toBe("PUT");
    expect(rawRequest?.headers).toBeInstanceOf(Headers);
    const rawBody = String(rawRequest?.body);
    expect(rawBody).toContain(`"maxSnapshotBytes":${int64}`);
    expect(rawBody).toContain(`"value":${int64}`);
    expect(rawBody).toContain(`"value":${decimal}`);
    expect(rawBody).toContain('"value":null');
    expect(rawBody).toContain('"value":"Ada"');
    expect(rawBody).toContain('"value":true');
  });

  it("uses advertised, shell-relative routes and keeps opaque cursors as query values", async () => {
    const context = endpointContext({
      getJson: async (url: string) => url.endsWith("/capabilities") ? capabilities() : { items: [], nextCursor: null }
    });
    const cursor = "opaque+/cursor==";

    await listEtlDefinitions(context, { limit: 17, cursor });
    expect(context.getJsonSpy).toHaveBeenLastCalledWith(
      `/studio/v2/etl/definitions?limit=17&cursor=${encodeURIComponent(cursor)}`,
      { signal: undefined }
    );

    await listEtlRuns(context, { limit: 50 });
    expect(context.getJsonSpy).toHaveBeenLastCalledWith("/studio/v2/etl/runs?limit=50", { signal: undefined });
    await expect(listEtlRuns(context, { limit: 201 })).rejects.toMatchObject({
      status: 400,
      code: "etl.page-limit-invalid"
    });
  });

  it("pages connector partitions through the separately advertised relation", async () => {
    const context = endpointContext({
      getJson: async (url: string) => url.endsWith("/capabilities") ? capabilities() : { items: [], nextCursor: null }
    });
    const cursor = "opaque+/connector-partitions==";
    const controller = new AbortController();

    await listEtlRunConnectorPartitions(context, "run/a ?b", { limit: 17, cursor }, controller.signal);
    expect(context.getJsonSpy).toHaveBeenLastCalledWith(
      `/studio/v2/etl/runs/run%2Fa%20%3Fb/connector-partitions?limit=17&cursor=${encodeURIComponent(cursor)}`,
      { signal: controller.signal }
    );
  });

  it("reports unsupported connector partition progress instead of falling back to file partitions", async () => {
    const context = endpointContext({
      getJson: async (url: string) => url.endsWith("/capabilities")
        ? capabilities({ connectorPartitions: false })
        : { items: [], nextCursor: null }
    });

    await expect(listEtlRunConnectorPartitions(context, "connector-run")).rejects.toMatchObject({
      code: "etl.capability-unavailable"
    });
    expect(context.getJsonSpy).toHaveBeenCalledTimes(1);
  });

  it("uses the advertised v2 capability and pages safe connector bindings and definitions", async () => {
    const context = endpointContext({
      getJson: async (url: string) => url.endsWith("/capabilities")
        ? capabilities()
        : { items: [{ alias: "source", role: "Source", kind: "http" }], nextCursor: null }
    });
    const cursor = "opaque+/connector-cursor==";

    await expect(getEtlApiCapability(context, "design-v2")).resolves.toMatchObject({
      id: "elsa.api.etl-design-v2",
      contractVersion: "1"
    });
    const bindings = await listEtlConnectorBindings(context, { limit: 17, cursor });
    expect(bindings.items[0]).toEqual({ alias: "source", role: "Source", kind: "http" });
    expect(context.getJsonSpy).toHaveBeenLastCalledWith(
      `/studio/v2/etl/v2/bindings?limit=17&cursor=${encodeURIComponent(cursor)}`,
      { signal: undefined }
    );

    await listEtlConnectorDefinitions(context, { limit: 9 });
    expect(context.getJsonSpy).toHaveBeenLastCalledWith("/studio/v2/etl/v2/definitions?limit=9", { signal: undefined });
    await listEtlConnectorVersions(context, "orders/with space", { limit: 5 });
    expect(context.getJsonSpy).toHaveBeenLastCalledWith(
      "/studio/v2/etl/v2/definitions/orders%2Fwith%20space/versions?limit=5",
      { signal: undefined }
    );
  });

  it("reads and saves v2 connector drafts without rounding 64-bit limits or filter values", async () => {
    const pipelineJson = connectorPipelineJson();
    const draftJson = connectorDraftJson(pipelineJson);
    const requestResponse = vi.fn(async (_url: string, _init?: StudioRequestInit) => new Response(draftJson, { status: 200 }));
    const context = endpointContext({ requestResponse });
    const draft = await getEtlConnectorDefinition(context, "orders");

    expect(context.http.requestResponse).toHaveBeenCalledWith(
      "/studio/v2/etl/v2/definitions/orders",
      expect.objectContaining({ method: "GET" })
    );
    expect(draft.pipeline.source.kind).toBe("httpJson");
    expect(draft.pipeline.routes.main).toEqual({ kind: "http", binding: "output" });
    const huge = "9223372036854775807";
    const limits = draft.pipeline.limits;
    const policy = draft.pipeline.http;
    for (const value of [
      limits.maxBatchBytes,
      limits.maxOutstandingRecords,
      limits.maxOutstandingBytes,
      limits.maxLookupRecords,
      limits.maxLookupBytes,
      limits.maxOutputBatchBytes,
      limits.maxRetainedOutputBytes,
      limits.maxSnapshotBytes,
      policy.maxPageBytes,
      policy.maxRetainedInputBytes,
      policy.maxRetainedMetadataBytes
    ]) {
      expect(isLosslessNumber(value)).toBe(true);
      expect(formatEtlJsonNumber(value as EtlJsonNumber)).toBe(huge);
    }
    expect(isLosslessNumber(draft.pipeline.filters[0].value)).toBe(true);
    expect(formatEtlJsonNumber(draft.pipeline.filters[0].value as EtlJsonNumber)).toBe(huge);

    await updateEtlConnectorDraft(context, "orders", { expectedRevision: 2, pipeline: draft.pipeline });
    const call = vi.mocked(requestResponse).mock.calls[1];
    expect(call[0]).toBe("/studio/v2/etl/v2/definitions/orders/draft");
    expect(call[1]?.method).toBe("PUT");
    const rawBody = String(call[1]?.body);
    expect(rawBody).toContain(`"maxRetainedOutputBytes":${huge}`);
    expect(rawBody).toContain(`"maxPageBytes":${huge}`);
    expect(rawBody).toContain(`"maxRetainedInputBytes":${huge}`);
    expect(rawBody).toContain(`"maxRetainedMetadataBytes":${huge}`);
    expect(rawBody).toContain(`"value":${huge}`);
  });

  it("keeps omitted v2 default budgets omitted when a saved draft is updated", async () => {
    const pipelineJson = connectorPipelineJson(false);
    const draftJson = connectorDraftJson(pipelineJson);
    const requestResponse = vi.fn(async (_url: string, _init?: StudioRequestInit) => new Response(draftJson, { status: 200 }));
    const context = endpointContext({ requestResponse });
    const draft = await getEtlConnectorDefinition(context, "orders");

    expect(draft.pipeline.limits.maxRetainedOutputBytes).toBeUndefined();
    expect(draft.pipeline.http.maxRetainedMetadataBytes).toBeUndefined();
    await updateEtlConnectorDraft(context, "orders", { expectedRevision: draft.revision, pipeline: draft.pipeline });
    const body = String(vi.mocked(requestResponse).mock.calls[1][1]?.body);
    expect(body).not.toContain("maxRetainedOutputBytes");
    expect(body).not.toContain("maxRetainedMetadataBytes");
  });

  it("uses the v2 draft, validation, publication, and exact artifact routes", async () => {
    const pipeline = connectorPipeline();
    const token = `etl1:orders:1:${"a".repeat(64)}`;
    const requestResponse = vi.fn(async (url: string, _init?: StudioRequestInit) => {
      if (url.endsWith("/versions/1")) {
        return new Response(JSON.stringify({
          version: { definitionId: "orders", definitionVersion: 1, contentHash: "a".repeat(64), selection: token, publishedAt: null },
          pipeline
        }), { status: 200 });
      }
      return new Response(connectorDraftJson(connectorPipelineJson()), { status: 200 });
    });
    const postJson = vi.fn(async (url: string) => url.endsWith("/validate")
      ? { revision: 3, definitionVersion: 2, contentHash: "b".repeat(64), diagnostics: [] }
      : { status: "published", version: { definitionId: "orders", definitionVersion: 2, contentHash: "b".repeat(64), selection: `etl1:orders:2:${"b".repeat(64)}`, publishedAt: null } });
    const context = endpointContext({ requestResponse, postJson });

    await createEtlConnectorDefinition(context, { id: "orders", pipeline });
    await updateEtlConnectorDraft(context, "orders", { expectedRevision: 1, pipeline });
    await validateEtlConnectorDraft(context, "orders", { expectedRevision: 2 });
    const publication = await publishEtlConnectorDefinition(context, "orders", {
      expectedRevision: 3,
      definitionVersion: 2,
      contentHash: "b".repeat(64)
    });
    const artifact = await getEtlConnectorVersion(context, "orders", 1);

    expect(vi.mocked(requestResponse).mock.calls.map(([url, init]) => [url, init?.method])).toEqual([
      ["/studio/v2/etl/v2/definitions", "POST"],
      ["/studio/v2/etl/v2/definitions/orders/draft", "PUT"],
      ["/studio/v2/etl/v2/definitions/orders/versions/1", "GET"]
    ]);
    expect(vi.mocked(postJson).mock.calls.map(([url]) => url)).toEqual([
      "/studio/v2/etl/v2/definitions/orders/validate",
      "/studio/v2/etl/v2/definitions/orders/publish"
    ]);
    expect(publication.version.selection).toBe(`etl1:orders:2:${"b".repeat(64)}`);
    expect(artifact.version.selection).toBe(token);
    expect(artifact.pipeline.source.kind).toBe("httpJson");
  });

  it("does not share capability documents between HTTP clients at the same base URL", async () => {
    const authenticated = endpointContext();
    const restricted = endpointContext({ getJson: async () => ({ capabilities: [] }) });

    await expect(getEtlApiCapability(authenticated, "design")).resolves.toMatchObject({ id: "elsa.api.etl-design" });
    await expect(getEtlApiCapability(restricted, "design")).rejects.toMatchObject({
      code: "etl.capability-unavailable"
    });
    expect(restricted.getJsonSpy).toHaveBeenCalledTimes(1);
  });

  it("keeps the server's safe binding role strings in their actual wire casing", async () => {
    const context = endpointContext({
      getJson: async (url: string) => url.endsWith("/capabilities")
        ? capabilities()
        : { items: [{ alias: "source", role: "Source" }, { alias: "output", role: "MainOutput" }], nextCursor: null }
    });

    const result = await listEtlBindings(context);
    expect(result.items.map(binding => binding.role)).toEqual(["Source", "MainOutput"]);
  });

  it("posts cancellation without a client-supplied invocation body", async () => {
    const context = endpointContext({ requestJson: vi.fn(async () => ({ cancellationAccepted: false })) });

    await cancelEtlRun(context, "run/a ?b");
    expect(context.requestJsonSpy).toHaveBeenCalledWith(
      "/studio/v2/etl/runs/run%2Fa%20%3Fb/cancel",
      { method: "POST", signal: undefined }
    );
  });

  it("preserves HTTP status and stable problem codes for permission and provider failures", async () => {
    const forbidden = endpointContext({
      getJson: vi.fn()
        .mockResolvedValueOnce(capabilities())
        .mockRejectedValueOnce(new StudioHttpError(403, "forbidden", null, { status: 403, code: "etl.permission-denied" }))
    });
    await expect(listEtlDefinitions(forbidden)).rejects.toMatchObject({ status: 403, code: "etl.permission-denied" });
    expect(classifyEtlApiError(new EtlApiError("forbidden", 403, "etl.permission-denied"))).toBe("forbidden");

    const provider = endpointContext({
      getJson: vi.fn()
        .mockResolvedValueOnce(capabilities())
        .mockRejectedValueOnce(new StudioHttpError(503, "provider", null, { status: 503, code: "etl.provider-unavailable" }))
    });
    try {
      await listEtlDefinitions(provider);
      throw new Error("Expected the provider failure to reject.");
    } catch (error) {
      expect(error).toBeInstanceOf(EtlApiError);
      expect(classifyEtlApiError(error)).toBe("unavailable");
      expect(error).toMatchObject({ status: 503, code: "etl.provider-unavailable" });
    }
  });

  it("retains raw-response permission errors and refuses unsafe transports for pipeline editing", async () => {
    const forbidden = endpointContext({
      getJson: async () => capabilities(),
      requestResponse: vi.fn(async () => new Response(
        JSON.stringify({ status: 403, code: "etl.permission-denied" }),
        { status: 403, headers: { "content-type": "application/problem+json" } }
      ))
    });
    await expect(getEtlDefinition(forbidden, "orders")).rejects.toMatchObject({
      status: 403,
      code: "etl.permission-denied"
    });
    expect(classifyEtlApiError(new EtlApiError("forbidden", 403, "etl.permission-denied"))).toBe("forbidden");

    const noRawTransport = endpointContext({ getJson: async () => capabilities() });
    await expect(getEtlDefinition(noRawTransport, "orders")).rejects.toMatchObject({
      code: "etl.lossless-transport-unavailable"
    });
  });

  it("distinguishes absent and incompatible capability contracts from request failures", async () => {
    const absent = endpointContext({ getJson: async () => ({ capabilities: [] }) });
    await expect(listEtlDefinitions(absent)).rejects.toMatchObject({ code: "etl.capability-unavailable" });
    await expect(listEtlDefinitions(absent)).rejects.toMatchObject({ code: "etl.capability-unavailable" });
    expect(classifyEtlApiError(new EtlApiError("missing", null, "etl.capability-unavailable"))).toBe("unavailable");

    const incompatible = endpointContext({
      getJson: async () => capabilities({ designVersion: "2" })
    });
    await expect(listEtlDefinitions(incompatible)).rejects.toMatchObject({
      code: "etl.capability-version-unsupported"
    });
  });
});

type HttpOverrides = {
  getJson?: (url: string, init?: StudioRequestInit) => unknown | Promise<unknown>;
  requestJson?: (url: string, init?: StudioRequestInit) => unknown | Promise<unknown>;
  requestResponse?: StudioHttpClient["requestResponse"];
  postJson?: (url: string, body: unknown, init?: StudioRequestInit) => unknown | Promise<unknown>;
  putJson?: (url: string, body: unknown, init?: StudioRequestInit) => unknown | Promise<unknown>;
  deleteJson?: (url: string, init?: StudioRequestInit) => unknown | Promise<unknown>;
  postForm?: (url: string, body: FormData, init?: StudioRequestInit) => unknown | Promise<unknown>;
};

function endpointContext(overrides: HttpOverrides = {}) {
  clearEtlApiCapabilityCache();
  const getJsonSpy = vi.fn(overrides.getJson ?? (() => capabilities()));
  const requestJsonSpy = vi.fn(overrides.requestJson ?? (async () => ({})));
  const postJson = overrides.postJson ?? (async () => ({}));
  const putJson = overrides.putJson ?? (async () => ({}));
  const deleteJson = overrides.deleteJson ?? (async () => ({}));
  const postForm = overrides.postForm ?? (async () => ({}));
  const defaultClient: StudioHttpClient = {
    getJson: async <T,>(url: string, init?: StudioRequestInit) => await getJsonSpy(url, init) as T,
    requestJson: async <T,>(url: string, init?: StudioRequestInit) => await requestJsonSpy(url, init) as T,
    postJson: async <T,>(url: string, body: unknown, init?: StudioRequestInit) => await postJson(url, body, init) as T,
    putJson: async <T,>(url: string, body: unknown, init?: StudioRequestInit) => await putJson(url, body, init) as T,
    deleteJson: async <T,>(url: string, init?: StudioRequestInit) => await deleteJson(url, init) as T,
    postForm: async <T,>(url: string, body: FormData, init?: StudioRequestInit) => await postForm(url, body, init) as T,
    requestResponse: overrides.requestResponse
  };
  return Object.assign({
    baseUrl: "https://studio.example/studio/v2/",
    http: defaultClient
  }, { getJsonSpy, requestJsonSpy });
}

function capabilities(options: {
  designVersion?: string;
  connectorDesignVersion?: string;
  connectorPartitions?: boolean;
} = {}): EtlApiCapabilityDocument {
  return {
    capabilities: [
      {
        id: "elsa.api.etl-design",
        contractVersion: options.designVersion ?? "1",
        links: designRelations.map(([rel, href]) => ({ rel, href }))
      },
      {
        id: "elsa.api.etl-design-v2",
        contractVersion: options.connectorDesignVersion ?? "1",
        links: connectorDesignRelations.map(([rel, href]) => ({ rel, href }))
      },
      {
        id: "elsa.api.etl-runtime",
        contractVersion: "1",
        links: [
          ...runtimeRelations,
          ...(options.connectorPartitions === false ? [] : [runtimeConnectorPartitionRelation])
        ].map(([rel, href]) => ({ rel, href }))
      }
    ]
  };
}

const maxInt64 = "9223372036854775807";

function connectorPipelineJson(includeDefaultBudgets = true): string {
  const retainedOutputBytes = includeDefaultBudgets ? `"maxRetainedOutputBytes":${maxInt64},` : "";
  const retainedMetadataBytes = includeDefaultBudgets ? `"maxRetainedMetadataBytes":${maxInt64},` : "";
  return `{"source":{"kind":"httpJson","binding":"source","recordsPointer":"/items","fields":[{"name":"id","pointer":"/id","type":"int64","required":true}],"pagination":{"kind":"token","continuationPointer":"/next","queryParameter":"cursor"}},"lookup":null,"routes":{"main":{"kind":"http","binding":"output"},"errors":{"kind":"jsonl","binding":"errors"}},"mapping":[{"name":"id","origin":"source","field":"id"}],"filters":[{"field":"id","comparison":"equal","value":${maxInt64}}],"limits":{"filePartitionCount":2,"maxPartitions":32,"maxRecordBytes":65536,"maxFields":256,"maxBatchRecords":1000,"maxBatchBytes":${maxInt64},"maxOutstandingRecords":${maxInt64},"maxOutstandingBytes":${maxInt64},"maxLookupRecords":${maxInt64},"maxLookupBytes":${maxInt64},"maxOutputBatchBytes":${maxInt64},${retainedOutputBytes}"maxSnapshotBytes":${maxInt64},"maxArtifactBytes":262144},"http":{"maxPageBytes":${maxInt64},"maxPageRecords":10000,"maxJsonDepth":32,"maxContinuationBytes":8192,"maxPagesPerPartition":10000,"maxRetainedInputBytes":${maxInt64},${retainedMetadataBytes}"maxAttempts":5,"deadlineSeconds":900,"requestTimeoutSeconds":30,"initialDelaySeconds":1,"maxDelaySeconds":60}}`;
}

function connectorDraftJson(pipeline: string): string {
  return `{"definitionId":"orders","revision":2,"lastPublishedVersion":1,"pipeline":${pipeline},"updatedAt":"2026-10-20T10:00:00Z"}`;
}

function connectorPipeline(): EtlPipelineV2 {
  return {
    source: {
      kind: "httpJson",
      binding: "source",
      recordsPointer: "/items",
      fields: [{ name: "id", pointer: "/id", type: "int64", required: true }],
      pagination: { kind: "singlePage" }
    },
    lookup: null,
    routes: {
      main: { kind: "http", binding: "output" },
      errors: { kind: "jsonl", binding: "errors" }
    },
    mapping: [{ name: "id", origin: "source", field: "id" }],
    filters: [],
    limits: {
      filePartitionCount: 2,
      maxPartitions: 32,
      maxRecordBytes: 65536,
      maxFields: 256,
      maxBatchRecords: 1000,
      maxBatchBytes: 1_000_000,
      maxOutstandingRecords: 10_000,
      maxOutstandingBytes: 8_000_000,
      maxLookupRecords: 100_000,
      maxLookupBytes: 8_000_000,
      maxOutputBatchBytes: 1_000_000,
      maxSnapshotBytes: 16_000_000,
      maxArtifactBytes: 262144
    },
    http: {
      maxPageBytes: 1_000_000,
      maxPageRecords: 10_000,
      maxJsonDepth: 32,
      maxContinuationBytes: 8192,
      maxPagesPerPartition: 10_000,
      maxRetainedInputBytes: 16_000_000,
      maxAttempts: 5,
      deadlineSeconds: 900,
      requestTimeoutSeconds: 30,
      initialDelaySeconds: 1,
      maxDelaySeconds: 60
    }
  };
}
