import { isLosslessNumber } from "lossless-json";
import { StudioHttpError, type StudioHttpClient, type StudioRequestInit } from "@elsa-workflows/studio-sdk";
import { afterEach, describe, expect, it, vi } from "vitest";
import {
  cancelEtlRun,
  classifyEtlApiError,
  clearEtlApiCapabilityCache,
  EtlApiError,
  getEtlApiCapability,
  getEtlDefinition,
  listEtlBindings,
  listEtlDefinitions,
  listEtlRuns,
  updateEtlDraft
} from "../etlApi";
import { formatEtlJsonNumber, type EtlApiCapabilityDocument, type EtlJsonNumber } from "../etlTypes";

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

function capabilities(options: { designVersion?: string } = {}): EtlApiCapabilityDocument {
  return {
    capabilities: [
      {
        id: "elsa.api.etl-design",
        contractVersion: options.designVersion ?? "1",
        links: designRelations.map(([rel, href]) => ({ rel, href }))
      },
      {
        id: "elsa.api.etl-runtime",
        contractVersion: "1",
        links: runtimeRelations.map(([rel, href]) => ({ rel, href }))
      }
    ]
  };
}
