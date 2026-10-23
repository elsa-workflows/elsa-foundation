import React from "react";
import { flushSync } from "react-dom";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { StudioHttpError, type StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { Elsa3ReusableImportPage } from "../Elsa3ReusableImportPage";
import {
  analyzeElsa3ReusableCollection,
  applyElsa3ReusableImport,
  expandElsa3ReusableSelection,
  getElsa3ReusableImportReceipt,
  uploadElsa3ReusableCollection
} from "../api/elsa3ReusableImport";
import type {
  Elsa3MigrationDiagnostic,
  Elsa3ReusableImportAnalysisPage,
  Elsa3ReusableImportItem,
  Elsa3ReusableImportReceipt
} from "../elsa3ReusableImportTypes";

vi.mock("../api/elsa3ReusableImport", () => ({
  uploadElsa3ReusableCollection: vi.fn(),
  analyzeElsa3ReusableCollection: vi.fn(),
  expandElsa3ReusableSelection: vi.fn(),
  applyElsa3ReusableImport: vi.fn(),
  getElsa3ReusableImportReceipt: vi.fn()
}));

const context = { baseUrl: "", http: {} } as StudioEndpointContext;
let active: { root: Root; container: HTMLDivElement } | null = null;

beforeEach(() => {
  vi.clearAllMocks();
});

afterEach(() => {
  if (!active) return;
  const current = active;
  active = null;
  flushSync(() => current.root.unmount());
  current.container.remove();
});

describe("Elsa 3 reusable import workbench", () => {
  it("reviews a valid subset, retains server-expanded closure, and reconciles a lost Apply response", async () => {
    const owner = item("owner", "owner-v1", {
      isReusable: true,
      activityDefinitionId: "activity-owner",
      activityDefinitionVersionId: "activity-owner-v1",
      activityTypeKey: "elsa3.workflow.owner",
      dependencies: [{
        ownerSourceVersionId: "owner-v1",
        targetSourceDefinitionId: "dependency",
        targetSourceVersionId: "dependency-v1",
        targetActivityDefinitionVersionId: "activity-dependency-v1",
        nodeId: "reuse-node"
      }],
      rewrites: [{
        ownerSourceVersionId: "owner-v1",
        nodeId: "reuse-node",
        targetSourceVersionId: "dependency-v1",
        targetActivityDefinitionVersionId: "activity-dependency-v1"
      }]
    });
    const dependency = item("dependency", "dependency-v1", {
      isReusable: true,
      activityDefinitionId: "activity-dependency",
      activityDefinitionVersionId: "activity-dependency-v1",
      activityTypeKey: "elsa3.workflow.dependency"
    });
    const cycle = diagnostic("ELS3-REUSABLE-DEPENDENCY-CYCLE", {
      cycle: ["invalid-cycle-v1", "invalid-cycle-v2"]
    });
    const invalid = item("invalid-cycle", "invalid-cycle-v1", {
      canApply: false,
      diagnostics: [cycle]
    });
    vi.mocked(analyzeElsa3ReusableCollection).mockResolvedValue(page([owner, dependency, invalid], [cycle]));
    vi.mocked(expandElsa3ReusableSelection).mockResolvedValue({
      collectionHandle: "collection",
      planId: "plan",
      requestedSourceVersionIds: ["owner-v1"],
      expandedSourceVersionIds: ["owner-v1", "dependency-v1"],
      addedDependencySourceVersionIds: ["dependency-v1"],
      isReady: true,
      diagnostics: []
    });
    vi.mocked(applyElsa3ReusableImport).mockRejectedValue(new Error("connection closed"));
    vi.mocked(getElsa3ReusableImportReceipt).mockResolvedValue(receipt("Applied"));
    const navigate = vi.fn();

    const container = render(<Elsa3ReusableImportPage context={context} navigate={navigate} />);
    input(container, "Authorized collection handle", "collection");
    click(button(container, "Analyze immutable collection"));
    await waitFor(() => expect(container.textContent).toContain("owner-v1"));
    expect(checkbox(container, "Select invalid-cycle exact source version 1").disabled).toBe(true);

    click(checkbox(container, "Select owner exact source version 1"));
    await waitFor(() => expect(container.textContent).toContain("Required dependency"));
    click(button(container, "Review exact import"));
    expect(container.textContent).toContain("Activity Definitions2");
    expect(container.textContent).toContain("Activity Definition versions2");
    expect(container.textContent).toContain("Exact reusable rewrites1");

    click(button(container, "Apply reviewed import"));
    await waitFor(() => expect(container.textContent).toContain("Durable import receipt"));
    expect(applyElsa3ReusableImport).toHaveBeenCalledTimes(1);
    expect(getElsa3ReusableImportReceipt).toHaveBeenCalledTimes(1);
    expect(vi.mocked(applyElsa3ReusableImport).mock.calls[0]?.[3]).toEqual([
      "dependency-v1",
      "owner-v1"
    ]);

    click(button(container, "Open Activity Definition version"));
    expect(navigate).toHaveBeenCalledWith(
      "/workflows/activity-definitions?definition=activity-owner&section=versions&version=activity-owner-v1"
    );
  });

  it("retries the identical idempotent Apply when receipt lookup is not found", async () => {
    const owner = item("owner", "owner-v1", { isReusable: true });
    vi.mocked(analyzeElsa3ReusableCollection).mockResolvedValue(page([owner]));
    vi.mocked(expandElsa3ReusableSelection).mockResolvedValue({
      collectionHandle: "collection",
      planId: "plan",
      requestedSourceVersionIds: ["owner-v1"],
      expandedSourceVersionIds: ["owner-v1"],
      addedDependencySourceVersionIds: [],
      isReady: true,
      diagnostics: []
    });
    vi.mocked(applyElsa3ReusableImport)
      .mockRejectedValueOnce(new Error("connection closed"))
      .mockResolvedValueOnce(receipt("AlreadyImported"));
    vi.mocked(getElsa3ReusableImportReceipt).mockRejectedValue(new StudioHttpError(
      404,
      "Not found",
      null,
      { errorCode: "elsa3.import.not-found" }
    ));

    const container = render(<Elsa3ReusableImportPage context={context} navigate={vi.fn()} />);
    input(container, "Authorized collection handle", "collection");
    click(button(container, "Analyze immutable collection"));
    await waitFor(() => expect(container.textContent).toContain("owner-v1"));
    click(checkbox(container, "Select owner exact source version 1"));
    await waitFor(() => expect(container.textContent).toContain("ready for final review"));
    click(button(container, "Review exact import"));
    click(button(container, "Apply reviewed import"));

    await waitFor(() => expect(container.textContent).toContain("Already imported"));
    expect(applyElsa3ReusableImport).toHaveBeenCalledTimes(2);
    expect(vi.mocked(applyElsa3ReusableImport).mock.calls[0]?.[4])
      .toBe(vi.mocked(applyElsa3ReusableImport).mock.calls[1]?.[4]);
    expect(vi.mocked(applyElsa3ReusableImport).mock.calls[1]?.[3]).toEqual(["owner-v1"]);
  });

  it("clears retained analysis when exact-closure access is lost", async () => {
    const owner = item("protected-owner", "protected-owner-v1");
    vi.mocked(analyzeElsa3ReusableCollection).mockResolvedValue(page([owner]));
    vi.mocked(expandElsa3ReusableSelection).mockRejectedValue(new StudioHttpError(
      403,
      "Forbidden",
      null,
      { detail: "secret protected-owner-v1 must never remain visible" }
    ));

    const container = render(<Elsa3ReusableImportPage context={context} navigate={vi.fn()} />);
    input(container, "Authorized collection handle", "collection");
    click(button(container, "Analyze immutable collection"));
    await waitFor(() => expect(container.textContent).toContain("protected-owner-v1"));
    click(checkbox(container, "Select protected-owner exact source version 1"));

    await waitFor(() => expect(container.textContent).toContain("Retained analysis and receipt details were cleared"));
    expect(container.textContent).not.toContain("protected-owner-v1");
    expect(container.textContent).not.toContain("secret");
    expect((container.querySelector("input[placeholder='Authorized collection handle']") as HTMLInputElement).value).toBe("");
  });

  it("cancels side-effect-free analysis and ignores a late response", async () => {
    let resolveAnalysis!: (value: Elsa3ReusableImportAnalysisPage) => void;
    vi.mocked(analyzeElsa3ReusableCollection).mockImplementation(() =>
      new Promise(resolve => {
        resolveAnalysis = resolve;
      })
    );

    const container = render(<Elsa3ReusableImportPage context={context} navigate={vi.fn()} />);
    input(container, "Authorized collection handle", "collection");
    click(button(container, "Analyze immutable collection"));
    click(button(container, "Cancel analysis"));
    resolveAnalysis(page([item("late-owner", "late-owner-v1")]));

    await waitFor(() => expect(container.textContent).toContain("Side-effect-free analysis cancelled"));
    expect(container.textContent).not.toContain("late-owner-v1");
  });

  it("ignores a late upload response after explicit cancellation", async () => {
    let resolveUpload!: (value: ReturnType<typeof uploadResult>) => void;
    vi.mocked(uploadElsa3ReusableCollection).mockImplementation(() =>
      new Promise(resolve => {
        resolveUpload = resolve;
      })
    );
    const container = render(<Elsa3ReusableImportPage context={context} navigate={vi.fn()} />);
    selectFile(container, {
      name: "elsa3.json",
      size: 2,
      text: () => Promise.resolve("[]")
    } as File);
    click(button(container, "Upload once"));
    await waitFor(() => expect(uploadElsa3ReusableCollection).toHaveBeenCalledTimes(1));
    click(button(container, "Cancel upload"));
    resolveUpload(uploadResult());

    await waitFor(() => expect(container.textContent).toContain("Upload cancelled"));
    expect(container.textContent).not.toContain("late-handle");
    expect(container.textContent).not.toContain("Handle accepted");
  });

  it("freezes reviewed inputs and ignores an old Apply receipt after forced input invalidation", async () => {
    const owner = item("owner", "owner-v1", { isReusable: true });
    let resolveApply!: (value: Elsa3ReusableImportReceipt) => void;
    vi.mocked(analyzeElsa3ReusableCollection).mockResolvedValue(page([owner]));
    vi.mocked(expandElsa3ReusableSelection).mockResolvedValue({
      collectionHandle: "collection",
      planId: "plan",
      requestedSourceVersionIds: ["owner-v1"],
      expandedSourceVersionIds: ["owner-v1"],
      addedDependencySourceVersionIds: [],
      isReady: true,
      diagnostics: []
    });
    vi.mocked(applyElsa3ReusableImport).mockImplementation(() =>
      new Promise(resolve => {
        resolveApply = resolve;
      })
    );

    const container = render(<Elsa3ReusableImportPage context={context} navigate={vi.fn()} />);
    input(container, "Authorized collection handle", "collection");
    click(button(container, "Analyze immutable collection"));
    await waitFor(() => expect(container.textContent).toContain("owner-v1"));
    click(checkbox(container, "Select owner exact source version 1"));
    await waitFor(() => expect(container.textContent).toContain("ready for final review"));
    click(button(container, "Review exact import"));
    click(button(container, "Apply reviewed import"));

    const handleInput = container.querySelector("input[placeholder='Authorized collection handle']") as HTMLInputElement;
    expect(handleInput.disabled).toBe(true);
    expect(checkbox(container, "Select owner exact source version 1").disabled).toBe(true);
    expect((button(container, "Review exact import") as HTMLButtonElement).disabled).toBe(true);
    expect((button(container, "Activity Definitions") as HTMLButtonElement).disabled).toBe(true);

    // Simulate an external host invalidating the controlled field despite the UI freeze.
    handleInput.disabled = false;
    input(container, "Authorized collection handle", "fresh-collection");
    resolveApply(receipt("Applied"));

    await waitFor(() => expect(handleInput.value).toBe("fresh-collection"));
    expect(container.textContent).toContain("Fresh analysis is required");
    expect(container.textContent).not.toContain("Durable import receipt");
  });

  it.each([
    [409, "elsa3.import.identity-collision", "An exact generated identity collided."],
    [422, "elsa3.import.validation-failed", "The reviewed plan is stale."]
  ])("reports an authoritative atomic %s rejection without claiming an ambiguous outcome", async (status, errorCode, detail) => {
    const owner = item("owner", "owner-v1", { isReusable: true });
    vi.mocked(analyzeElsa3ReusableCollection).mockResolvedValue(page([owner]));
    vi.mocked(expandElsa3ReusableSelection).mockResolvedValue({
      collectionHandle: "collection",
      planId: "plan",
      requestedSourceVersionIds: ["owner-v1"],
      expandedSourceVersionIds: ["owner-v1"],
      addedDependencySourceVersionIds: [],
      isReady: true,
      diagnostics: []
    });
    vi.mocked(applyElsa3ReusableImport).mockRejectedValue(new StudioHttpError(
      status,
      detail,
      null,
      { errorCode, detail, diagnostics: [] }
    ));

    const container = render(<Elsa3ReusableImportPage context={context} navigate={vi.fn()} />);
    input(container, "Authorized collection handle", "collection");
    click(button(container, "Analyze immutable collection"));
    await waitFor(() => expect(container.textContent).toContain("owner-v1"));
    click(checkbox(container, "Select owner exact source version 1"));
    await waitFor(() => expect(container.textContent).toContain("ready for final review"));
    click(button(container, "Review exact import"));
    click(button(container, "Apply reviewed import"));

    await waitFor(() => expect(container.textContent).toContain("guarantees that no partial Design documents were written"));
    expect(getElsa3ReusableImportReceipt).not.toHaveBeenCalled();
  });
});

function render(ui: React.ReactElement) {
  const container = document.createElement("div");
  document.body.appendChild(container);
  const root = createRoot(container);
  flushSync(() => root.render(ui));
  active = { root, container };
  return container;
}

function input(container: HTMLElement, placeholder: string, value: string) {
  const element = [...container.querySelectorAll("input")]
    .find(candidate => candidate.getAttribute("placeholder") === placeholder) as HTMLInputElement | undefined;
  if (!element) throw new Error(`Input '${placeholder}' not found.`);
  const setter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, "value")?.set;
  flushSync(() => {
    setter?.call(element, value);
    element.dispatchEvent(new Event("input", { bubbles: true }));
    element.dispatchEvent(new Event("change", { bubbles: true }));
  });
}

function selectFile(container: HTMLElement, file: File) {
  const element = container.querySelector<HTMLInputElement>("input[type=file]");
  if (!element) throw new Error("File input not found.");
  Object.defineProperty(element, "files", { configurable: true, value: [file] });
  flushSync(() => element.dispatchEvent(new Event("change", { bubbles: true })));
}

function button(container: HTMLElement, label: string) {
  const element = [...container.querySelectorAll("button")]
    .find(candidate => candidate.textContent?.includes(label));
  if (!element) throw new Error(`Button '${label}' not found.`);
  return element;
}

function checkbox(container: HTMLElement, label: string) {
  const element = container.querySelector<HTMLInputElement>(`input[type=checkbox][aria-label="${label}"]`);
  if (!element) throw new Error(`Checkbox '${label}' not found.`);
  return element;
}

function click(element: Element) {
  flushSync(() => element.dispatchEvent(new MouseEvent("click", { bubbles: true })));
}

async function waitFor(assertion: () => void) {
  let lastError: unknown;
  for (let attempt = 0; attempt < 100; attempt++) {
    try {
      assertion();
      return;
    } catch (error) {
      lastError = error;
      await new Promise(resolve => setTimeout(resolve, 0));
    }
  }
  throw lastError;
}

function item(
  sourceDefinitionId: string,
  sourceVersionId: string,
  overrides: Partial<Elsa3ReusableImportItem> = {}
): Elsa3ReusableImportItem {
  return {
    sourceDefinitionId,
    sourceVersionId,
    sourceVersion: 1,
    sourceFingerprint: `fingerprint-${sourceVersionId}`,
    isReusable: false,
    workflowDefinitionId: `workflow-${sourceVersionId}`,
    workflowVersionId: `workflow-version-${sourceVersionId}`,
    activityDefinitionId: null,
    activityDefinitionVersionId: null,
    activityTypeKey: null,
    dependencies: [],
    rewrites: [],
    directStarts: [],
    diagnostics: [],
    canApply: true,
    ...overrides
  };
}

function diagnostic(
  code: string,
  overrides: Partial<Elsa3MigrationDiagnostic> = {}
): Elsa3MigrationDiagnostic {
  return {
    severity: "Error",
    code,
    message: "Invalid cycle member.",
    metadata: {},
    pathSegments: [
      { kind: "SourceVersion", identity: "invalid-cycle-v1", location: "$[2]" },
      { kind: "Node", identity: "reuse-node", location: "$[2].root.activities[0]" }
    ],
    cycle: [],
    isError: true,
    ...overrides
  };
}

function page(
  items: Elsa3ReusableImportItem[],
  diagnostics: Elsa3MigrationDiagnostic[] = []
): Elsa3ReusableImportAnalysisPage {
  return {
    collectionHandle: "collection",
    planId: "plan",
    offset: 0,
    limit: 100,
    processed: items.length,
    total: items.length,
    processedDiagnostics: diagnostics.length,
    totalDiagnostics: diagnostics.length,
    isComplete: true,
    nextOffset: null,
    items,
    diagnostics
  };
}

function receipt(status: "Applied" | "AlreadyImported"): Elsa3ReusableImportReceipt {
  return {
    receiptId: "receipt",
    collectionHandle: "collection",
    planId: "plan",
    idempotencyKey: "operation",
    selectionFingerprint: "sha256:selection",
    accessScope: {
      tenantId: "tenant",
      userId: "user",
      tenantScope: "tenant:tenant"
    },
    status,
    completedAt: "2026-07-19T00:00:00Z",
    sources: [{
      sourceDefinitionId: "owner",
      sourceVersionId: "owner-v1",
      workflowDefinitionId: "wrapper-owner",
      workflowVersionId: "wrapper-owner-v1",
      workflowDisposition: "Created",
      workflowNavigationIdentity: "/design/workflows/definitions/wrapper-owner/versions/wrapper-owner-v1",
      activityDefinitionId: "activity-owner",
      activityDefinitionVersionId: "activity-owner-v1",
      activityDefinitionDisposition: "Created",
      activityVersionDisposition: "Created",
      activityDefinitionNavigationIdentity: "/design/activities/definitions/activity-owner",
      activityVersionNavigationIdentity: "/design/activities/versions/activity-owner-v1"
    }]
  };
}

function uploadResult() {
  return {
    collectionHandle: "late-handle",
    createdAt: "2026-07-19T00:00:00Z",
    expiresAt: "2026-07-19T01:00:00Z",
    sourceVersionCount: 1,
    contentLength: 2
  };
}
