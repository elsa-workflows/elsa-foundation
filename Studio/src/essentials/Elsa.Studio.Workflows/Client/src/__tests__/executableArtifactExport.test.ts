import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { ApiCapabilityUnavailableError, clearApiCapabilityCache } from "../api/capabilities";
import {
  describeWorkflowExecutableExportFailure,
  exportWorkflowExecutableClosure,
  isWorkflowExecutableExportAvailable,
  readContentDispositionFileName,
  workflowExecutableExportRelation
} from "../api/executableArtifactExport";
import {
  buildExecutableArtifactFileName,
  downloadExecutableArtifactJson,
  downloadWorkflowJson
} from "../workflowSerialization";

// The closure is opaque to Studio: these tests pin that it is requested at the advertised link and saved
// exactly as the server produced it (studio #493, foundation #1304).

const closure = {
  formatVersion: "1.0.0",
  rootArtifactId: "artifact-root",
  artifacts: [{ identity: { artifactId: "artifact-root", definitionId: "orders", artifactVersion: "1.4.0" } }]
};

let createdBlobs: Blob[] = [];
let anchors: HTMLAnchorElement[] = [];
const objectUrlApi = URL as unknown as {
  createObjectURL?: (blob: Blob) => string;
  revokeObjectURL?: (url: string) => void;
};

beforeEach(() => {
  createdBlobs = [];
  anchors = [];
  // jsdom defines no object-URL API at all, so it is installed rather than spied on.
  objectUrlApi.createObjectURL = (blob: Blob) => {
    createdBlobs.push(blob);
    return `blob:${createdBlobs.length}`;
  };
  objectUrlApi.revokeObjectURL = () => undefined;
  vi.spyOn(HTMLAnchorElement.prototype, "click").mockImplementation(function click(this: HTMLAnchorElement) {
    anchors.push(this);
  });
});

afterEach(() => {
  vi.restoreAllMocks();
  delete objectUrlApi.createObjectURL;
  delete objectUrlApi.revokeObjectURL;
  clearApiCapabilityCache();
});

describe("executable artifact export client", () => {
  it("GETs the closure at the advertised publishing link for the published version", async () => {
    const { context, getJson } = createContext();

    const exported = await exportWorkflowExecutableClosure(context, "version-1");

    expect(getJson).toHaveBeenLastCalledWith(
      "/publishing/workflows/version-1/executable-export",
      { signal: undefined });
    // Returned untouched: no export payload is built and nothing is reshaped.
    expect(exported.closure).toEqual(closure);
    // No header-capable client: the caller reconstructs the name.
    expect(exported.fileName).toBeNull();
  });

  it("takes the download name from the server's Content-Disposition when the client exposes headers", async () => {
    const { context, getJsonWithHeaders } = createHeaderAwareContext(
      'attachment; filename="orders-1.4.0-closure.json"');

    const exported = await exportWorkflowExecutableClosure(context, "version-1");

    expect(getJsonWithHeaders).toHaveBeenLastCalledWith(
      "/publishing/workflows/version-1/executable-export",
      { signal: undefined });
    expect(exported.fileName).toBe("orders-1.4.0-closure.json");
    expect(exported.closure).toEqual(closure);
  });

  it("reports no name when the header is stripped, which is what a host that does not expose it does", async () => {
    // Content-Disposition is not CORS-safelisted: without Access-Control-Expose-Headers the browser
    // hands back a 200 and a valid body with the header simply absent.
    const { context } = createHeaderAwareContext(null);

    await expect(exportWorkflowExecutableClosure(context, "version-1"))
      .resolves.toMatchObject({ fileName: null });
  });

  it("escapes the version id into the templated link", async () => {
    const { context, getJson } = createContext();

    await exportWorkflowExecutableClosure(context, "wf-orders:1.4.0");

    expect(getJson.mock.calls.at(-1)?.[0]).toBe("/publishing/workflows/wf-orders%3A1.4.0/executable-export");
  });

  it("reports the capability as available only when the publishing capability advertises the relation", async () => {
    const advertised = createContext();
    await expect(isWorkflowExecutableExportAvailable(advertised.context)).resolves.toBe(true);

    const older = createContext({ advertiseExport: false });
    await expect(isWorkflowExecutableExportAvailable(older.context)).resolves.toBe(false);

    const runtimeless = createContext({ publishingCapability: false });
    await expect(isWorkflowExecutableExportAvailable(runtimeless.context)).resolves.toBe(false);
  });

  it("fails without a request when the server does not advertise the relation", async () => {
    const { context, getJson } = createContext({ advertiseExport: false });

    await expect(exportWorkflowExecutableClosure(context, "version-1"))
      .rejects.toBeInstanceOf(ApiCapabilityUnavailableError);
    expect(getJson.mock.calls.map(([url]) => url)).toEqual(["/capabilities"]);
  });
});

describe("executable artifact export failures", () => {
  it("classifies an unknown version as not found", () => {
    const failure = describeWorkflowExecutableExportFailure(httpError(404, {
      status: 404,
      detail: "Cannot export workflow definition version 'version-1': no executable source reference of any scope exists for it."
    }));

    expect(failure.kind).toBe("notFound");
    expect(failure.status).toBe(404);
    expect(failure.missingArtifactIds).toEqual([]);
  });

  it("separates a bodyless 404 from a version with nothing to export", () => {
    // Nothing on the server cross-checks an advertised href against a mapped route, so an advertised
    // relation can 404 with no problem detail at all. That is not "this version has nothing".
    expect(describeWorkflowExecutableExportFailure(Object.assign(new Error("Request failed with 404."), { status: 404 })).kind)
      .toBe("endpointUnavailable");
  });

  it("classifies a never-published version as the publish-first conflict", () => {
    const failure = describeWorkflowExecutableExportFailure(httpError(409, {
      status: 409,
      detail: "Cannot export workflow definition version 'version-1': it has no Published source reference. Only 'TestRun'-scope reference(s) exist, and those are expiring, engine-local snapshots that are not portable.",
      errors: [{ name: "generalErrors", reason: "Cannot export workflow definition version 'version-1': it has no Published source reference." }]
    }));

    expect(failure.kind).toBe("notPublished");
    expect(failure.missingArtifactIds).toEqual([]);
  });

  it("names every missing dependency from the standard error collection", () => {
    const failure = describeWorkflowExecutableExportFailure(httpError(409, {
      status: 409,
      detail: "Cannot export workflow definition version 'version-1': the closure rooted at 'artifact-root' is incomplete.",
      errors: [
        { name: "generalErrors", reason: "Dependency artifact 'artifact-child-1' is missing from the executable store." },
        { name: "generalErrors", reason: "Dependency artifact 'artifact-child-2' is missing from the executable store." }
      ]
    }));

    expect(failure.kind).toBe("incompleteClosure");
    expect(failure.missingArtifactIds).toEqual(["artifact-child-1", "artifact-child-2"]);
  });

  it("reads the missing dependencies out of the dictionary error shape too", () => {
    const failure = describeWorkflowExecutableExportFailure(httpError(409, {
      status: 409,
      title: "One or more errors occurred.",
      errors: {
        generalErrors: [
          "Dependency artifact 'artifact-child-1' is missing from the executable store.",
          "Dependency artifact 'artifact-child-1' is missing from the executable store."
        ]
      }
    }));

    expect(failure.kind).toBe("incompleteClosure");
    expect(failure.missingArtifactIds).toEqual(["artifact-child-1"]);
  });

  it("falls back to the summary sentence when the error collection is absent", () => {
    const failure = describeWorkflowExecutableExportFailure(httpError(409, {
      status: 409,
      detail: "Cannot export workflow definition version 'version-1': the closure rooted at 'artifact-root' is incomplete. Unresolved dependency artifact(s): 'artifact-child-1@sha256-aaa' (declared by 'artifact-root', not in the executable store)."
    }));

    expect(failure.kind).toBe("incompleteClosure");
    expect(failure.missingArtifactIds).toEqual(["artifact-child-1"]);
  });

  it("separates a misconfigured engine from a store fault", () => {
    expect(describeWorkflowExecutableExportFailure(httpError(500, {
      status: 500,
      detail: "Workflow artifact export is not available on this engine."
    })).kind).toBe("engineMisconfigured");

    expect(describeWorkflowExecutableExportFailure(httpError(500, {
      status: 500,
      detail: "Cannot export workflow definition version 'version-1': the stored executable dependency graph contains a cycle (a -> b -> a), which no content-addressed artifact can legitimately form."
    })).kind).toBe("engineFault");
  });

  it("keeps an unrecognised failure describable", () => {
    const failure = describeWorkflowExecutableExportFailure(new Error("Network request failed."));

    expect(failure.kind).toBe("unknown");
    expect(failure.status).toBeUndefined();
    expect(failure.message).toBe("Network request failed.");
  });
});

describe("Content-Disposition parsing", () => {
  it("prefers the RFC 5987 extended form and decodes it", () => {
    expect(readContentDispositionFileName(
      "attachment; filename=\"fallback.json\"; filename*=UTF-8''orders-1.4.0-closure.json"))
      .toBe("orders-1.4.0-closure.json");
  });

  it("accepts an unquoted name", () => {
    expect(readContentDispositionFileName("attachment; filename=orders-1.4.0-closure.json"))
      .toBe("orders-1.4.0-closure.json");
  });

  it("never lets a header value become a path or carry control characters", () => {
    expect(readContentDispositionFileName('attachment; filename="../../etc/passwd"')).toBe("passwd");
    expect(readContentDispositionFileName('attachment; filename="a\r\nb.json"')).toBe("ab.json");
    expect(readContentDispositionFileName('attachment; filename="niño cierre.json"')).toBe("ni-o-cierre.json");
  });

  it("treats a missing or empty name as no name", () => {
    expect(readContentDispositionFileName(null)).toBeNull();
    expect(readContentDispositionFileName("attachment")).toBeNull();
    expect(readContentDispositionFileName('attachment; filename="///"')).toBeNull();
  });
});

describe("executable artifact download", () => {
  it("saves the server JSON verbatim under the endpoint's own file name", async () => {
    downloadExecutableArtifactJson(closure, buildExecutableArtifactFileName({
      definitionId: "orders",
      artifactVersion: "1.4.0"
    }));

    expect(anchors).toHaveLength(1);
    expect(anchors[0].download).toBe("orders-1.4.0-closure.json");
    expect(createdBlobs[0].type).toBe("application/json");
    expect(JSON.parse(await readBlobText(createdBlobs[0]))).toEqual(closure);
    // The anchor is not left behind in the document.
    expect(document.querySelector("a[download]")).toBeNull();
  });

  it("mirrors the server's safe-name rules for hostile identity segments", () => {
    expect(buildExecutableArtifactFileName({
      definitionId: "../orders\"\r\n/evil",
      artifactVersion: "1.4.0-rc 1"
    })).toBe("orders-evil-1.4.0-rc-1-closure.json");
  });

  it("falls back the way the server does when identity segments are missing", () => {
    expect(buildExecutableArtifactFileName({})).toBe("workflow-unversioned-closure.json");
    expect(buildExecutableArtifactFileName({ definitionId: "///", artifactVersion: "   " }))
      .toBe("workflow-unversioned-closure.json");
  });

  it("caps a long identity segment like the server does", () => {
    const fileName = buildExecutableArtifactFileName({ definitionId: "a".repeat(200), artifactVersion: "1" });

    expect(fileName).toBe(`${"a".repeat(96)}-1-closure.json`);
  });

  it("leaves the design definition export naming untouched", () => {
    downloadWorkflowJson({ definitionId: "orders", state: {}, layout: [], activityPresentation: [] }, "My Workflow");

    expect(anchors[0].download).toBe("My-Workflow.json");
  });
});

function createContext(options: { advertiseExport?: boolean; publishingCapability?: boolean } = {}) {
  const advertiseExport = options.advertiseExport ?? true;
  const publishingCapability = options.publishingCapability ?? true;
  const getJson = vi.fn(async (url: string) => {
    if (url === "/capabilities") {
      return {
        capabilities: publishingCapability
          ? [{
            id: "elsa.api.publishing",
            contractVersion: "1",
            links: [
              { rel: "workflow-publish", href: "publishing/workflows/{versionId}/publish", templated: true },
              ...(advertiseExport
                ? [{
                  rel: workflowExecutableExportRelation,
                  href: "publishing/workflows/{versionId}/executable-export",
                  templated: true
                }]
                : [])
            ]
          }]
          : []
      };
    }
    if (url.endsWith("/executable-export")) return closure;
    throw new Error(`Unexpected GET ${url}`);
  });
  const context = {
    baseUrl: `test://export-${createdBlobs.length}-${Math.random()}`,
    http: { getJson }
  } as unknown as StudioEndpointContext;
  return { context, getJson };
}

/** jsdom's Blob exposes no `text()`, so the saved bytes are read back through a FileReader. */
function readBlobText(blob: Blob): Promise<string> {
  return new Promise<string>((resolve, reject) => {
    const reader = new FileReader();
    reader.onerror = () => reject(reader.error ?? new Error("Could not read the download blob."));
    reader.onload = () => resolve(String(reader.result));
    reader.readAsText(blob);
  });
}

function createHeaderAwareContext(contentDisposition: string | null) {
  const getJsonWithHeaders = vi.fn(async (_url: string) => ({
    value: closure,
    status: 200,
    headers: new Headers(contentDisposition ? { "content-disposition": contentDisposition } : {})
  }));
  const base = createContext();
  const context = {
    baseUrl: base.context.baseUrl,
    http: { getJson: base.getJson, getJsonWithHeaders }
  } as unknown as StudioEndpointContext;
  return { context, getJson: base.getJson, getJsonWithHeaders };
}

function httpError(status: number, payload: Record<string, unknown>) {
  const message = typeof payload.detail === "string" ? payload.detail : `Request failed with ${status}.`;
  return Object.assign(new Error(message), { status, payload });
}
