import React from "react";
import { flushSync } from "react-dom";
import { createRoot } from "react-dom/client";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { clearApiCapabilityCache } from "../api/capabilities";
import { workflowExecutableExportRelation } from "../api/executableArtifactExport";
import type { WorkflowDefinitionDetails, WorkflowDraft } from "../workflowTypes";
import { setNotifications } from "../workflow-editor/notifications";
import type { WorkflowEditorOperation } from "../workflow-editor/editorTypes";
import { useWorkflowOperations } from "../workflow-editor/useWorkflowOperations";
import {
  selectPublishedExecutable,
  useExecutableArtifactDownload,
  useExecutableArtifactExport,
  type ExecutableArtifactExportState
} from "../workflow-editor/useExecutableArtifactExport";

// Availability gating and the failure branches of the "Export artifact" action (studio #493).

type Operations = ReturnType<typeof useWorkflowOperations>;

let mounted: { root: ReturnType<typeof createRoot>; container: HTMLDivElement } | null = null;
let downloads: { name: string; blob: Blob }[] = [];
const notify = vi.fn(() => ({ id: "export-confirmation", update() {}, dismiss() {} }));
const objectUrlApi = URL as unknown as {
  createObjectURL?: (blob: Blob) => string;
  revokeObjectURL?: (url: string) => void;
};

beforeEach(() => {
  notify.mockClear(); setNotifications({ notify, track: work => work });
  downloads = [];
  const blobsByUrl = new Map<string, Blob>();
  // jsdom defines no object-URL API at all, so it is installed rather than spied on.
  objectUrlApi.createObjectURL = (blob: Blob) => {
    const url = `blob:${blobsByUrl.size + 1}`;
    blobsByUrl.set(url, blob);
    return url;
  };
  objectUrlApi.revokeObjectURL = () => undefined;
  vi.spyOn(HTMLAnchorElement.prototype, "click").mockImplementation(function click(this: HTMLAnchorElement) {
    downloads.push({ name: this.download, blob: blobsByUrl.get(this.href)! });
  });
});

afterEach(() => {
  setNotifications(undefined);
  vi.restoreAllMocks();
  delete objectUrlApi.createObjectURL;
  delete objectUrlApi.revokeObjectURL;
  clearApiCapabilityCache();
  if (!mounted) return;
  flushSync(() => mounted!.root.unmount());
  mounted.container.remove();
  mounted = null;
});

describe("executable artifact export availability", () => {
  it("offers the definition's published artifact as the export target", async () => {
    const fixture = renderAvailability();
    await flushUpdates();

    expect(fixture.current()).toMatchObject({
      status: "ready",
      supported: true,
      target: {
        versionId: "version-1",
        definitionId: "definition-1",
        artifactVersion: "1.0.0",
        artifactId: "artifact-1"
      }
    });
  });

  it("hides the action when the server does not advertise the relation", async () => {
    const fixture = renderAvailability({ advertiseExport: false });
    await flushUpdates();

    expect(fixture.current()).toEqual({
      status: "unavailable",
      supported: false,
      target: null,
      publishedTarget: {
        versionId: "version-1",
        definitionId: "definition-1",
        artifactVersion: "1.0.0",
        artifactId: "artifact-1"
      }
    });
    // Live testing still needs the normal Runtime published list when artifact export is unsupported.
    expect(fixture.getJson.mock.calls.map(([url]) => url)).toContain("/runtime/workflows/executables?scope=Published");
  });

  it("reports no target for a workflow that was never published", async () => {
    const fixture = renderAvailability({ executables: [] });
    await flushUpdates();

    expect(fixture.current()).toMatchObject({ status: "ready", supported: true, target: null });
  });

  it("hides the action rather than erroring when the probe fails", async () => {
    const fixture = renderAvailability({ failExecutables: true });
    await vi.waitFor(async () => {
      await flushUpdates();
      expect(fixture.current()).toEqual({ status: "unavailable", supported: false, target: null, publishedTarget: null });
    });
  });

  it("exports this definition's most recently published artifact", () => {
    const executables = [
      publishedExecutable({ artifactId: "artifact-old", versionId: "version-old", artifactVersion: "1.0.0", publishedAt: "2026-07-01T00:00:00Z" }),
      publishedExecutable({ artifactId: "artifact-new", versionId: "version-new", artifactVersion: "2.0.0", publishedAt: "2026-08-01T00:00:00Z" }),
      publishedExecutable({ artifactId: "artifact-other", versionId: "version-other", artifactVersion: "9.0.0", publishedAt: "2026-09-01T00:00:00Z", definitionId: "definition-2" })
    ];

    // Newest publish of this definition wins; another definition's newer artifact is not a candidate.
    expect(selectPublishedExecutable(executables, "definition-1")?.versionId).toBe("version-new");
    expect(selectPublishedExecutable(executables, "definition-3")).toBeNull();
    expect(selectPublishedExecutable([], "definition-1")).toBeNull();
  });
});

describe("manual editor export confirmation lifecycle", () => {
  it("downloads design JSON through the shared notifier without replacing editing status", async () => {
    const fixture = renderOperations(); fixture.writeStatus("Autosaved");
    fixture.current().exportJson();
    expect(downloads).toHaveLength(1);
    expect(downloads[0].name).toBe("Orders.json");
    expect(JSON.parse(await readBlobText(downloads[0].blob))).toMatchObject({ name: "Orders", definitionId: "definition-1", state: { rootActivity: { nodeId: "root", activityVersionId: "root-v1" } }, layout: [] });
    expect(notify).toHaveBeenCalledWith({ level: "success", title: "Exported workflow as JSON." });
    expect(fixture.setStatus).toHaveBeenLastCalledWith("Autosaved");
  });

  it.each(["scope", "retained visit"] as const)("finishes accepted artifact download but suppresses retired %s feedback and clears only owned progress", async change => {
    let finish!: (value: typeof closurePayload) => void;
    const deferred = new Promise<typeof closurePayload>(resolve => { finish = resolve; });
    const fixture = renderOperations({ deferred, retained: change === "retained visit" });
    const work = fixture.current().exportExecutableArtifact();
    await vi.waitFor(() => expect(fixture.getJson.mock.calls.some(([url]) => url.includes("/executable-export"))).toBe(true));
    expect(downloads).toHaveLength(0); expect(notify).not.toHaveBeenCalled();
    expect(fixture.setOperation).toHaveBeenLastCalledWith("exportingArtifact");
    if (change === "scope") fixture.render({ next: true });
    else { fixture.render(undefined, "hidden"); fixture.render(); }
    finish(closurePayload); await work;
    expect(downloads).toHaveLength(1); expect(notify).not.toHaveBeenCalled();
    if (change === "retained visit") {
      expect(fixture.setOperation).toHaveBeenLastCalledWith("idle");
      expect(fixture.setStatus).toHaveBeenLastCalledWith("");
    }
  });

  it("retains the owned artifact export failure after a tab visit retires", async () => {
    let fail!: (error: Error) => void;
    const fixture = renderOperations({ retained: true, deferred: new Promise((_resolve, reject) => { fail = reject; }) });
    const work = fixture.current().exportExecutableArtifact();
    await vi.waitFor(() => expect(fixture.getJson.mock.calls.some(([url]) => url.includes("/executable-export"))).toBe(true));
    fixture.render(undefined, "hidden"); fixture.render();
    fail(new Error("Export unavailable.")); await work;
    expect(fixture.setError.mock.calls.at(-1)?.[0]).toMatchObject({ message: expect.stringContaining("Export unavailable.") });
    expect(fixture.setOperation).toHaveBeenLastCalledWith("idle"); expect(downloads).toHaveLength(0); expect(notify).not.toHaveBeenCalled();
  });

  it("preserves newer editing status and operation while an accepted artifact export finishes", async () => {
    let finish!: (value: typeof closurePayload) => void;
    const fixture = renderOperations({ deferred: new Promise(resolve => { finish = resolve; }) });
    const work = fixture.current().exportExecutableArtifact();
    await vi.waitFor(() => expect(fixture.getJson.mock.calls.some(([url]) => url.includes("/executable-export"))).toBe(true));
    expect(downloads).toHaveLength(0); expect(notify).not.toHaveBeenCalled();
    expect(fixture.setOperation).toHaveBeenLastCalledWith("exportingArtifact");
    fixture.writeStatus("Autosaved"); fixture.writeOperation("saving");
    finish(closurePayload); await work;
    expect(downloads).toHaveLength(1);
    expect(fixture.setStatus).toHaveBeenLastCalledWith("Autosaved");
    expect(fixture.setOperation).toHaveBeenLastCalledWith("saving");
  });
});

describe("executable artifact export operation", () => {
  it("downloads the server closure verbatim under the endpoint's file name", async () => {
    const fixture = renderOperations();

    await fixture.current().exportExecutableArtifact();
    await flushUpdates();

    expect(fixture.getJson.mock.calls.map(([url]) => url)).toEqual([
      "/capabilities",
      "/publishing/workflows/version-1/executable-export"
    ]);
    expect(downloads).toHaveLength(1);
    expect(downloads[0].name).toBe("definition-1-1.0.0-closure.json");
    expect(JSON.parse(await readBlobText(downloads[0].blob))).toEqual(closurePayload);
    expect(notify).toHaveBeenCalledWith({ level: "success", title: "Exported executable artifact as definition-1-1.0.0-closure.json." });
    expect(fixture.setStatus).toHaveBeenLastCalledWith("");
    expect(fixture.setError.mock.calls.map(([value]) => value)).toEqual([""]);
    // Nothing is saved, promoted, or published by an export.
    expect(fixture.saveDraft).not.toHaveBeenCalled();
  });

  it("saves under the name the server chose when Content-Disposition is readable", async () => {
    const fixture = renderOperations({ contentDisposition: 'attachment; filename="definition-1-2.1.0-closure.json"' });

    await fixture.current().exportExecutableArtifact();
    await flushUpdates();

    // The published artifact says 1.0.0; the server's header wins over the reconstruction.
    expect(downloads[0].name).toBe("definition-1-2.1.0-closure.json");
  });

  it("says so when the exported artifact carried no identity to name the file from", async () => {
    const fixture = renderOperations({
      publishedExecutable: {
        versionId: "version-1",
        definitionId: "",
        artifactVersion: null,
        artifactId: null
      }
    });

    await fixture.current().exportExecutableArtifact();
    await flushUpdates();

    expect(downloads[0].name).toBe("workflow-unversioned-closure.json");
    expect(fixture.setStatus.mock.calls.at(-1)?.[0]).toContain("carried no definition id or version");
    expect(notify).toHaveBeenCalledWith({ level: "success", title: "Exported executable artifact as workflow-unversioned-closure.json." });
  });

  it("asks for a publish instead of calling the endpoint when nothing is published", async () => {
    const fixture = renderOperations({ publishedExecutable: null });

    await fixture.current().exportExecutableArtifact();
    await flushUpdates();

    expect(fixture.getJson).not.toHaveBeenCalled();
    expect(downloads).toEqual([]);
    expect(fixture.setError.mock.calls.at(-1)?.[0]).toMatchObject({
      message: expect.stringContaining("Publish this workflow before exporting")
    });
  });

  it("offers a publish-first affordance for the never-published conflict", async () => {
    const fixture = renderOperations({
      failWith: {
        status: 409,
        detail: "Cannot export workflow definition version 'version-1': it has no Published source reference. Only 'TestRun'-scope reference(s) exist, and those are expiring, engine-local snapshots that are not portable."
      }
    });

    await fixture.current().exportExecutableArtifact();
    await flushUpdates();

    expect(downloads).toEqual([]);
    const error = fixture.setError.mock.calls.at(-1)?.[0];
    expect(error).toMatchObject({ status: 409, message: expect.stringContaining("Publish this workflow before exporting") });
    expect(error.detail).toContain("no Published source reference");
    expect(fixture.setStatus).toHaveBeenLastCalledWith("");
    expect(notify).not.toHaveBeenCalled();
  });

  it("surfaces every missing dependency artifact id for an incomplete closure", async () => {
    const fixture = renderOperations({
      failWith: {
        status: 409,
        detail: "Cannot export workflow definition version 'version-1': the closure rooted at 'artifact-1' is incomplete.",
        errors: [
          { name: "generalErrors", reason: "Dependency artifact 'artifact-child-1' is missing from the executable store." },
          { name: "generalErrors", reason: "Dependency artifact 'artifact-child-2' is missing from the executable store." }
        ]
      }
    });

    await fixture.current().exportExecutableArtifact();
    await flushUpdates();

    expect(downloads).toEqual([]);
    const error = fixture.setError.mock.calls.at(-1)?.[0];
    expect(error.message).toContain("dependency closure is incomplete");
    // The server's own per-dependency entries are the list; they are shown as they arrived.
    expect(error.detail).toContain("Dependency artifact 'artifact-child-1' is missing from the executable store.");
    expect(error.detail).toContain("Dependency artifact 'artifact-child-2' is missing from the executable store.");
  });

  it("reports an unknown version without downloading anything", async () => {
    const fixture = renderOperations({
      failWith: { status: 404, detail: "Cannot export workflow definition version 'version-1': no executable source reference of any scope exists for it." }
    });

    await fixture.current().exportExecutableArtifact();
    await flushUpdates();

    expect(downloads).toEqual([]);
    expect(fixture.setError.mock.calls.at(-1)?.[0]).toMatchObject({
      status: 404,
      message: expect.stringContaining("nothing to export for this workflow version")
    });
  });

  it("reports a store fault without downloading anything", async () => {
    const fixture = renderOperations({
      failWith: { status: 500, detail: "Cannot export workflow definition version 'version-1': the engine failed to read the executable store." }
    });

    await fixture.current().exportExecutableArtifact();
    await flushUpdates();

    expect(downloads).toEqual([]);
    expect(fixture.setError.mock.calls.at(-1)?.[0]).toMatchObject({
      status: 500,
      message: expect.stringContaining("could not produce the executable artifact")
    });
  });

  it("names the engine composition when no export delivery is configured", async () => {
    const fixture = renderOperations({
      failWith: { status: 500, detail: "Workflow artifact export is not available on this engine." }
    });

    await fixture.current().exportExecutableArtifact();
    await flushUpdates();

    expect(downloads).toEqual([]);
    expect(fixture.setError.mock.calls.at(-1)?.[0]).toMatchObject({
      message: expect.stringContaining("no export delivery configured")
    });
  });

  it("does nothing while another editor operation is in flight", async () => {
    const fixture = renderOperations({ busy: true });

    await fixture.current().exportExecutableArtifact();
    await flushUpdates();

    expect(fixture.getJson).not.toHaveBeenCalled();
    expect(fixture.setError).not.toHaveBeenCalled();
    expect(downloads).toEqual([]);
  });
});

describe("artifact list export action", () => {
  it("exports the clicked artifact's own definition version", async () => {
    const fixture = renderArtifactDownload();
    await flushUpdates();

    expect(fixture.current().supported).toBe(true);

    await fixture.current().exportArtifact(artifactRow("artifact-2", "version-2", "2.0.0"));
    await flushUpdates();

    // The row's version id addresses the endpoint — no slot lookup stands between the click and the call.
    expect(fixture.getJson.mock.calls.map(([url]) => url)).toContain("/publishing/workflows/version-2/executable-export");
    expect(downloads).toHaveLength(1);
    expect(downloads[0].name).toBe("definition-1-2.0.0-closure.json");
    expect(fixture.onExported.mock.calls.at(-1)?.[0]).toBe("definition-1-2.0.0-closure.json");
    expect(fixture.onFailed).not.toHaveBeenCalled();
  });

  it("finishes an owned download without publishing a toast after its page has gone away", async () => {
    const fixture = renderArtifactDownload({ hold: true });
    await flushUpdates();
    const inFlight = fixture.current().exportArtifact(artifactRow("artifact-2", "version-2", "2.0.0"));
    await flushUpdates();
    flushSync(() => mounted!.root.render(<p>Another page</p>));
    fixture.release();
    await inFlight;
    expect(downloads).toHaveLength(1);
    expect(fixture.onExported).toHaveBeenCalledTimes(1);
    expect(fixture.onExported.mock.calls[0][2]()).toBe(false);
    expect(notify).not.toHaveBeenCalled();
    expect(fixture.onFailed).not.toHaveBeenCalled();
  });

  it("keeps a list export refusal inline after its retained visit retires", async () => {
    const fixture = renderArtifactDownload({ hold: true, retained: true, failWith: { status: 409, detail: "Dependency closure is incomplete." } });
    await flushUpdates();
    const work = fixture.current().exportArtifact(artifactRow("artifact-2", "version-2", "2.0.0"));
    await vi.waitFor(() => expect(fixture.getJson.mock.calls.some(([url]) => url.endsWith("/executable-export"))).toBe(true));
    fixture.render("hidden"); fixture.render(); fixture.release(); await work;
    expect(fixture.onFailed.mock.calls.at(-1)?.[0]).toContain("Dependency closure is incomplete.");
    expect(fixture.onExported).not.toHaveBeenCalled(); expect(downloads).toHaveLength(0);
  });

  it("clears an owned list export failure when its retry succeeds after the visit retires", async () => {
    const fixture = renderArtifactDownload({ hold: true, retained: true });
    await flushUpdates();
    let attempts = 0;
    fixture.getJson.mockImplementation(async (url: string) => {
      if (url === "/capabilities") return capabilities(true);
      if (++attempts === 1) throw new Error("Earlier export failed.");
      await pending; return closurePayload;
    });
    let finish!: () => void;
    const pending = new Promise<void>(resolve => { finish = resolve; });
    let inlineError = "";
    fixture.onFailed.mockImplementation(message => { inlineError = message; });
    fixture.onExported.mockImplementation((_file, _artifact, canNotify: () => boolean) => { inlineError = ""; if (canNotify()) notify(); });
    await fixture.current().exportArtifact(artifactRow("artifact-2", "version-2", "2.0.0"));
    expect(inlineError).toContain("Earlier export failed.");
    await flushUpdates();
    const retry = fixture.current().exportArtifact(artifactRow("artifact-2", "version-2", "2.0.0"));
    await vi.waitFor(() => expect(attempts).toBe(2));
    fixture.render("hidden"); fixture.render(); finish(); await retry;
    expect(downloads).toHaveLength(1);
    expect(inlineError).toBe("");
    expect(fixture.onExported).toHaveBeenCalledTimes(1);
    expect(notify).not.toHaveBeenCalled();
  });

  it("reports the in-flight artifact so the list can disable its rows", async () => {
    const fixture = renderArtifactDownload({ hold: true });
    await flushUpdates();

    const inFlight = fixture.current().exportArtifact(artifactRow("artifact-2", "version-2", "2.0.0"));
    await flushUpdates();
    expect(fixture.current().exportingArtifactId).toBe("artifact-2");

    // A second click while one export is in flight is ignored rather than queued.
    await fixture.current().exportArtifact(artifactRow("artifact-3", "version-3", "3.0.0"));
    expect(fixture.getJson.mock.calls.filter(([url]) => url.endsWith("/executable-export"))).toHaveLength(1);

    fixture.release();
    await inFlight;
    await flushUpdates();
    expect(fixture.current().exportingArtifactId).toBeNull();
  });

  it("renders no action at all when the server does not advertise the relation", async () => {
    const fixture = renderArtifactDownload({ advertiseExport: false });
    await flushUpdates();

    expect(fixture.current().supported).toBe(false);
  });

  it("hands the list a flattened failure message and downloads nothing", async () => {
    const fixture = renderArtifactDownload({
      failWith: {
        status: 409,
        detail: "Cannot export workflow definition version 'version-2': the closure rooted at 'artifact-2' is incomplete.",
        errors: [{ name: "generalErrors", reason: "Dependency artifact 'artifact-child-1' is missing from the executable store." }]
      }
    });
    await flushUpdates();

    await fixture.current().exportArtifact(artifactRow("artifact-2", "version-2", "2.0.0"));
    await flushUpdates();

    expect(downloads).toEqual([]);
    expect(fixture.onExported).not.toHaveBeenCalled();
    const message = fixture.onFailed.mock.calls.at(-1)?.[0] as string;
    expect(message).toContain("dependency closure is incomplete");
    expect(message).toContain("Dependency artifact 'artifact-child-1' is missing from the executable store.");
  });
});

function artifactRow(artifactId: string, versionId: string, artifactVersion: string) {
  return publishedExecutable({
    artifactId,
    versionId,
    artifactVersion,
    publishedAt: "2026-08-01T00:00:00Z"
  }) as unknown as import("../workflowTypes").WorkflowExecutableSummary;
}

function renderArtifactDownload(options: {
  advertiseExport?: boolean;
  failWith?: Record<string, unknown>;
  hold?: boolean;
  retained?: boolean;
} = {}) {
  let release = () => undefined as void;
  const held = new Promise<void>(resolve => { release = () => resolve(); });
  const getJson = vi.fn(async (url: string) => {
    if (url === "/capabilities") return capabilities(options.advertiseExport ?? true);
    if (url.endsWith("/executable-export")) {
      if (options.hold) await held;
      if (options.failWith) {
        throw Object.assign(new Error(String(options.failWith.detail)), {
          status: options.failWith.status as number,
          payload: options.failWith
        });
      }
      return closurePayload;
    }
    throw new Error(`Unexpected GET ${url}`);
  });
  const context = {
    baseUrl: `test://artifact-download-${Math.random()}`,
    http: { getJson }
  } as unknown as StudioEndpointContext;
  const onExported = vi.fn((_file: string, _artifact: unknown, canNotify: () => boolean) => { if (canNotify()) notify(); });
  const onFailed = vi.fn();

  let current: ReturnType<typeof useExecutableArtifactDownload> | null = null;
  const container = document.createElement("div");
  document.body.appendChild(container);
  const root = createRoot(container);
  mounted = { root, container };
  const Harness = () => {
    current = useExecutableArtifactDownload({ context, onExported, onFailed });
    return null;
  };
  const render = (mode: "visible" | "hidden" = "visible") => flushSync(() => root.render(options.retained ? <React.Activity mode={mode}><Harness /></React.Activity> : <Harness />));
  render();
  return { current: () => current!, getJson, onExported, onFailed, render, release: () => release() };
}

const closurePayload = {
  formatVersion: "1.0.0",
  rootArtifactId: "artifact-1",
  artifacts: [{ identity: { artifactId: "artifact-1", definitionId: "definition-1", artifactVersion: "1.0.0" } }]
};

/** jsdom's Blob exposes no `text()`, so the saved bytes are read back through a FileReader. */
function readBlobText(blob: Blob): Promise<string> {
  return new Promise<string>((resolve, reject) => {
    const reader = new FileReader();
    reader.onerror = () => reject(reader.error ?? new Error("Could not read the download blob."));
    reader.onload = () => resolve(String(reader.result));
    reader.readAsText(blob);
  });
}

async function flushUpdates() {
  await new Promise(resolve => setTimeout(resolve, 0));
  flushSync(() => undefined);
}

function capabilities(advertiseExport: boolean) {
  return {
    capabilities: [
      {
        id: "elsa.api.publishing",
        contractVersion: "1",
        links: [
          ...(advertiseExport
            ? [{ rel: workflowExecutableExportRelation, href: "publishing/workflows/{versionId}/executable-export", templated: true }]
            : [])
        ]
      },
      {
        id: "elsa.api.runtime",
        contractVersion: "1",
        links: [
          { rel: "workflow-executables", href: "runtime/workflows/executables" },
          { rel: "workflow-activation-slots", href: "runtime/workflows/activation-slots/{definitionId}", templated: true }
        ]
      }
    ]
  };
}

function publishedExecutable(options: { artifactId: string; versionId: string; artifactVersion: string; publishedAt: string; definitionId?: string }) {
  return {
    artifactId: options.artifactId,
    artifactVersion: options.artifactVersion,
    artifactHash: `hash-${options.artifactId}`,
    definitionId: options.definitionId ?? "definition-1",
    definitionVersionId: options.versionId,
    createdAt: options.publishedAt,
    publishedAt: options.publishedAt,
    rootActivityType: "Elsa.Activities.Flowchart.Activities.Flowchart",
    rootActivityVersion: "1.0.0",
    nodeCount: 1,
    resumeTargetCount: 0
  } as never;
}


function renderAvailability(options: { advertiseExport?: boolean; executables?: unknown[]; failExecutables?: boolean } = {}) {
  const getJson = vi.fn(async (url: string) => {
    if (url === "/capabilities") return capabilities(options.advertiseExport ?? true);
    if (url.startsWith("/runtime/workflows/executables")) {
      if (options.failExecutables) throw new Error("executables unavailable");
      return {
        items: options.executables ?? [publishedExecutable({
          artifactId: "artifact-1",
          versionId: "version-1",
          artifactVersion: "1.0.0",
          publishedAt: "2026-08-01T00:00:00Z"
        })]
      };
    }
    throw new Error(`Unexpected GET ${url}`);
  });
  const context = {
    baseUrl: `test://availability-${Math.random()}`,
    http: { getJson }
  } as unknown as StudioEndpointContext;

  let current: ExecutableArtifactExportState | null = null;
  const container = document.createElement("div");
  document.body.appendChild(container);
  const root = createRoot(container);
  mounted = { root, container };
  const Harness = () => {
    current = useExecutableArtifactExport({ context, definitionId: "definition-1" });
    return null;
  };
  flushSync(() => root.render(<Harness />));
  return { current: () => current!, getJson };
}

function renderOperations(options: {
  busy?: boolean;
  contentDisposition?: string;
  publishedExecutable?: { versionId: string; definitionId: string; artifactVersion: string | null; artifactId: string | null } | null;
  failWith?: Record<string, unknown>;
  deferred?: Promise<typeof closurePayload>;
  retained?: boolean;
} = {}) {
  const getJson = vi.fn(async (url: string) => {
    if (url === "/capabilities") return capabilities(true);
    if (url === "/publishing/workflows/version-1/executable-export") {
      if (options.failWith) {
        const status = options.failWith.status as number;
        throw Object.assign(new Error(String(options.failWith.detail ?? `Request failed with ${status}.`)), {
          status,
          payload: options.failWith
        });
      }
      return options.deferred ? await options.deferred : closurePayload;
    }
    throw new Error(`Unexpected GET ${url}`);
  });
  const getJsonWithHeaders = options.contentDisposition
    ? vi.fn(async (url: string) => ({
      value: await getJson(url),
      status: 200,
      headers: new Headers({ "content-disposition": options.contentDisposition! })
    }))
    : undefined;
  const context = {
    baseUrl: `test://export-operation-${Math.random()}`,
    http: { getJson, postJson: vi.fn(), ...(getJsonWithHeaders ? { getJsonWithHeaders } : {}) }
  } as unknown as StudioEndpointContext;
  const saveDraft = vi.fn(async (snapshot: WorkflowDraft) => snapshot);
  const callbacks = {
    flushPendingSave: vi.fn(async () => undefined),
    reload: vi.fn(async () => undefined),
    startTestRun: vi.fn(),
    clearTestRun: vi.fn(),
    setPublishedArtifact: vi.fn(),
    setOperation: vi.fn(),
    setStatus: vi.fn(),
    setError: vi.fn(),
    setActiveRightPanelId: vi.fn(),
    setInspectorCollapsed: vi.fn(),
    setAutosavePaused: vi.fn()
  };

  let current: Operations | null = null;
  let notificationScope: object = context;
  let currentStatus = "";
  let currentOperation: WorkflowEditorOperation = "idle";
  const writeStatus = (update: React.SetStateAction<string>) => { currentStatus = typeof update === "function" ? update(currentStatus) : update; callbacks.setStatus(currentStatus); };
  const writeOperation: Parameters<typeof useWorkflowOperations>[0]["setOperation"] = update => { currentOperation = typeof update === "function" ? update(currentOperation) : update; callbacks.setOperation(currentOperation); };
  const container = document.createElement("div");
  document.body.appendChild(container);
  const root = createRoot(container);
  mounted = { root, container };
  const Harness = () => {
    current = useWorkflowOperations({
      context,
      notificationScope,
      draft: draft(),
      details: details(),
      catalog: [],
      busy: options.busy ?? false,
      publishedExecutable: options.publishedExecutable === undefined
        ? {
          versionId: "version-1",
          definitionId: "definition-1",
          artifactVersion: "1.0.0",
          artifactId: "artifact-1"
        }
        : options.publishedExecutable,
      saveDraft,
      ...callbacks,
      setStatus: writeStatus,
      setOperation: writeOperation
    });
    return null;
  };
  const render = (scope = notificationScope, mode: "visible" | "hidden" = "visible") => {
    notificationScope = scope;
    flushSync(() => root.render(options.retained ? <React.Activity mode={mode}><Harness /></React.Activity> : <Harness />));
  };
  render();
  return { current: () => current!, getJson, saveDraft, ...callbacks, render, writeStatus, writeOperation };
}

function draft(): WorkflowDraft {
  return {
    id: "draft-1",
    definitionId: "definition-1",
    sourceVersionId: "version-1",
    state: { rootActivity: { nodeId: "root", activityVersionId: "root-v1", inputs: [], outputs: [] } },
    layout: [],
    validationErrors: []
  };
}

function details(): WorkflowDefinitionDetails {
  return {
    definition: {
      id: "definition-1",
      name: "Orders",
      createdAt: "2026-07-01T00:00:00Z",
      lastModifiedAt: "2026-07-01T00:00:00Z",
      latestVersion: "1.0.0",
      versionCount: 1
    },
    draft: draft(),
    versions: [{ id: "version-1", version: "1.0.0", createdAt: "2026-07-01T00:00:00Z" }]
  };
}
