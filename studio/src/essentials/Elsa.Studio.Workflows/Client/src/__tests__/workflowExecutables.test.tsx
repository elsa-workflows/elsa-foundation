import { flushSync } from "react-dom";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { StudioWeaverRegistry, StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { StudioHttpError } from "@elsa-workflows/studio-sdk";
import { listExecutables } from "../api/runtime";
import { listDefinitions } from "../api/workflowDesign";
import { getPublicationSlotLifecycleSupport, listPublicationSlots, unpublishReviewedSlot } from "../api/publishing";
import type { WorkflowDefinitionSummary, WorkflowExecutableReference, WorkflowExecutableSummary } from "../workflowTypes";
import { WorkflowExecutables } from "../workflow-editor/WorkflowExecutables";
import { updateWorkflowDefinitionIdInUrl } from "../workflow-editor/workflowDefinitionUrl";
import { publishedSlot } from "./fixtures/publicationSlots";

vi.mock("../api/runtime", () => ({ listExecutables: vi.fn() }));
vi.mock("../api/workflowDesign", () => ({ listDefinitions: vi.fn() }));
vi.mock("../api/publishing", () => ({
  describeActivationSource: vi.fn(),
  getPublicationSlotLifecycleSupport: vi.fn(),
  listPublicationSlots: vi.fn(),
  restorePublicationSlot: vi.fn(),
  unpublishReviewedSlot: vi.fn(),
  unpublishSlot: vi.fn()
}));
vi.mock("../workflow-editor/useExecutableWorkflowRun", () => ({
  createExecutableWorkflowRunFeedback: () => ({}),
  createExecutableWorkflowRunTarget: () => null,
  useExecutableWorkflowRun: () => ({ pending: null, runningArtifactId: null, request: vi.fn(), confirm: vi.fn(), cancel: vi.fn() })
}));
vi.mock("../workflow-editor/useExecutableArtifactExport", () => ({
  useExecutableArtifactDownload: () => ({ supported: false, exportingArtifactId: null, exportArtifact: vi.fn() })
}));

let root: Root;
let container: HTMLDivElement;

beforeEach(() => {
  container = document.createElement("div");
  document.body.appendChild(container);
  root = createRoot(container);
  vi.mocked(getPublicationSlotLifecycleSupport).mockResolvedValue({ unpublish: false, unpublishReviewed: true, restore: false });
  vi.mocked(listPublicationSlots).mockResolvedValue({ available: true, slots: [] });
  vi.mocked(unpublishReviewedSlot).mockResolvedValue({});
});

afterEach(() => {
  flushSync(() => root.unmount());
  container.remove();
  window.history.replaceState({}, "", "/");
  vi.clearAllMocks();
});

describe("WorkflowExecutables", () => {
  it("refreshes the current scope without replacing results or resetting pagination and filters", async () => {
    const items = Array.from({ length: 30 }, (_, index) => ({ ...executableSummary(), artifactId: `artifact-${index}` }));
    vi.mocked(listExecutables).mockResolvedValue(items);
    vi.mocked(listDefinitions).mockResolvedValue({ definitions: [definitionSummary("Nightly orders")], page: 1, pageSize: 100, totalCount: 1 });
    const onFilterChange = vi.fn();
    flushSync(() => root.render(<WorkflowExecutables context={{ baseUrl: "https://server.example" } as StudioEndpointContext} ai={emptyAi()} runInputEditors={[]} definitionFilter="Nightly" onDefinitionFilterChange={onFilterChange} />));
    await flushAsyncUpdates();

    const scope = container.querySelector<HTMLSelectElement>('select[aria-label="Executable reference scope"]')!;
    flushSync(() => {
      scope.value = "all";
      scope.dispatchEvent(new Event("change", { bubbles: true }));
    });
    await flushAsyncUpdates();
    flushSync(() => container.querySelector<HTMLButtonElement>('button[aria-label="Next page"]')!.click());
    expect(container.textContent).toContain("Page 2 of");
    const table = container.querySelector('[role="table"]');
    let resolveRefresh!: (items: WorkflowExecutableSummary[]) => void;
    vi.mocked(listExecutables).mockReturnValueOnce(new Promise(resolve => { resolveRefresh = resolve; }));
    const refresh = container.querySelector<HTMLButtonElement>('button[aria-label="Refresh executables"]')!;
    flushSync(() => { refresh.click(); refresh.click(); });

    expect(listExecutables).toHaveBeenCalledTimes(3);
    expect(listExecutables).toHaveBeenLastCalledWith(expect.anything(), { scope: "all", includeRetired: false });
    expect(refresh.disabled).toBe(true);
    expect(container.querySelector('[role="table"]')).toBe(table);
    expect(container.textContent).toContain("Page 2 of");
    resolveRefresh(items);
    await flushAsyncUpdates();
    expect(refresh.disabled).toBe(false);
    expect(scope.value).toBe("all");
    expect(container.textContent).toContain("Page 2 of");
    expect(onFilterChange).not.toHaveBeenCalled();
  });

  it("retains results on refresh failure but removes them when access is denied", async () => {
    vi.mocked(listExecutables).mockResolvedValue([executableSummary()]);
    vi.mocked(listDefinitions).mockResolvedValue({ definitions: [definitionSummary("Nightly orders")], page: 1, pageSize: 100, totalCount: 1 });
    flushSync(() => root.render(<WorkflowExecutables context={{} as StudioEndpointContext} ai={emptyAi()} runInputEditors={[]} definitionFilter={null} onDefinitionFilterChange={() => {}} />));
    await flushAsyncUpdates();
    const refresh = container.querySelector<HTMLButtonElement>('button[aria-label="Refresh executables"]')!;

    vi.mocked(listExecutables).mockRejectedValueOnce(new Error("Temporarily offline"));
    flushSync(() => refresh.click());
    await flushAsyncUpdates();
    expect(container.textContent).toContain("Temporarily offline");
    expect(container.querySelector('[role="table"]')).not.toBeNull();

    vi.mocked(listExecutables).mockRejectedValueOnce(new StudioHttpError(403, "Access denied"));
    flushSync(() => refresh.click());
    await flushAsyncUpdates();
    expect(container.textContent).toContain("Access denied");
    expect(container.querySelector('[role="table"]')).toBeNull();
    expect(refresh.disabled).toBe(false);
  });

  it("shows a name-loading state before name filters can match, then exposes source metadata", async () => {
    const executable = executableSummary();
    type DefinitionListResult = Awaited<ReturnType<typeof listDefinitions>>;
    let resolveDefinitions!: (response: DefinitionListResult) => void;
    const definitions = new Promise<DefinitionListResult>(resolve => { resolveDefinitions = resolve; });
    vi.mocked(listExecutables).mockResolvedValue([executable]);
    vi.mocked(listDefinitions).mockReturnValue(definitions);

    flushSync(() => root.render(<WorkflowExecutables
      context={{} as StudioEndpointContext}
      ai={emptyAi()}
      runInputEditors={[]}
      definitionFilter="Nightly orders"
      onDefinitionFilterChange={() => {}}
    />));
    await flushAsyncUpdates();

    expect(listDefinitions).toHaveBeenCalledTimes(1);
    expect(container.textContent).toContain("Loading workflow names");
    expect(container.textContent).not.toContain("No executables match this definition filter.");

    resolveDefinitions({ definitions: [definitionSummary("Nightly orders")], page: 1, pageSize: 100, totalCount: 1 });
    await definitions;
    await flushAsyncUpdates();

    expect(container.textContent).toContain("Nightly orders");
    expect(container.querySelector(".wf-executable-workflow-name")?.getAttribute("title")).toBe("Open source workflow: Nightly orders");
    expect(container.querySelector('footer[aria-label="Workflow executable pagination"]')?.textContent).toContain("Showing 1-1 of 1");

    const identifiersTrigger = container.querySelector<HTMLButtonElement>('button[aria-label="Identifiers for Nightly orders"]');
    expect(identifiersTrigger).not.toBeNull();
    identifiersTrigger!.click();
    flushSync(() => {});

    const identifiersDialog = document.body.querySelector('[role="dialog"][aria-label="Workflow executable identifiers for Nightly orders"]');
    expect(identifiersDialog?.textContent).toContain("Definition version");
    expect(identifiersDialog?.textContent).toContain("Version 12.4");
    expect(container.textContent).not.toContain("Loading workflow names");
  });

  it("keeps long workflow names on one line and offers an empty-state action", async () => {
    const longName = "Customer-onboarding-and-KYC-refresh-for-regulated-markets-with-manual-exception-review-and-multi-region-escalation";
    vi.mocked(listExecutables).mockResolvedValue([executableSummary()]);
    vi.mocked(listDefinitions).mockResolvedValue({ definitions: [definitionSummary(longName)], page: 1, pageSize: 100, totalCount: 1 });

    flushSync(() => root.render(<WorkflowExecutables
      context={{} as StudioEndpointContext}
      ai={emptyAi()}
      runInputEditors={[]}
      definitionFilter={null}
      onDefinitionFilterChange={() => {}}
    />));
    await flushAsyncUpdates();

    const name = container.querySelector(".wf-executable-workflow-name");
    expect(name?.textContent).toBe(longName);
    expect(name?.getAttribute("title")).toBe(`Open source workflow: ${longName}`);
    expect(container.querySelector(".studio-collection-table")).not.toBeNull();
    expect(container.textContent).toContain("Inspect");
    expect(container.textContent).toContain("Run");
    expect(Array.from(container.querySelectorAll(".wf-row-actions button"), button => button.textContent?.replace(/\s+/g, " ").trim()))
      .toEqual(expect.arrayContaining(["Inspect", "Run"]));
    expect(container.querySelector(".wf-row-actions .studio-overflow-menu")).toBeNull();

    const identifiers = container.querySelector(".wf-executable-identifiers");
    expect(identifiers?.querySelector(".wf-list-primary-id")).not.toBeNull();
    expect(identifiers?.querySelector(`button[aria-label="Identifiers for ${longName}"]`)).not.toBeNull();

    const published = container.querySelector<HTMLTimeElement>(".wf-executable-published time");
    const publishedAt = new Date("2026-10-18T00:00:00Z");
    expect(published?.querySelector(".studio-timestamp-date")?.textContent).toBe(new Intl.DateTimeFormat(undefined, { dateStyle: "medium" }).format(publishedAt));
    expect(published?.querySelector(".studio-timestamp-time")?.textContent).toBe(new Intl.DateTimeFormat(undefined, { timeStyle: "short" }).format(publishedAt));
    expect(published?.getAttribute("title")).toBe(new Intl.DateTimeFormat(undefined, { dateStyle: "full", timeStyle: "long" }).format(publishedAt));
  });

  it("offers publication management choices for distinct published workflow references", async () => {
    const firstDefinitionId = "workflow/opaque ?#";
    const secondDefinitionId = "workflow/shared";
    const executable = executableSummary({
      definitionId: "legacy-primary-source",
      references: [
        executableReference({ sourceReferenceId: "published-first", definitionId: firstDefinitionId }),
        executableReference({ sourceReferenceId: "published-first-again", definitionId: firstDefinitionId }),
        executableReference({ sourceReferenceId: "published-second", definitionId: secondDefinitionId }),
        executableReference({ sourceReferenceId: "test-run-only", scope: "TestRun", definitionId: "test-run-definition" })
      ]
    });
    vi.mocked(listExecutables).mockResolvedValue([executable]);
    vi.mocked(listDefinitions).mockResolvedValue({ definitions: [
      definitionSummary("Orders", firstDefinitionId),
      definitionSummary("Orders", secondDefinitionId),
      definitionSummary("Test-run source", "test-run-definition")
    ], page: 1, pageSize: 100, totalCount: 3 });
    window.history.replaceState({}, "", "/workflows/executables?backend=selected");

    flushSync(() => root.render(<WorkflowExecutables
      context={{} as StudioEndpointContext}
      ai={emptyAi()}
      runInputEditors={[]}
      definitionFilter={null}
      onDefinitionFilterChange={() => {}}
      canManagePublications
    />));
    await flushAsyncUpdates();

    const row = container.querySelector<HTMLElement>('[aria-label="Inspect executable artifact-1"]')!;
    const trigger = row.querySelector<HTMLButtonElement>('button[aria-label^="Actions for"]')!;
    flushSync(() => trigger.click());
    await flushAsyncUpdates();

    const menu = document.body.querySelector<HTMLElement>('[role="menu"]')!;
    const manageChoices = Array.from(menu.querySelectorAll<HTMLButtonElement>("button"))
      .filter(button => button.textContent?.includes("Manage publication"));
    expect(manageChoices.map(button => button.textContent?.replace(/\s+/g, " ").trim())).toEqual([
      "Manage publication · Orders (workflow/opaque ?#)",
      "Manage publication · Orders (workflow/shared)"
    ]);

    flushSync(() => manageChoices[0].click());
    expect(window.location.pathname).toBe("/workflows/definitions");
    expect(new URLSearchParams(window.location.search)).toEqual(new URLSearchParams("?backend=selected&definition=workflow%2Fopaque+%3F%23&panel=artifacts"));
  });

  it("preserves backend selection and removes the panel request for ordinary definition navigation", () => {
    const source = new URL("https://studio.example/workflows/definitions?backend=selected&definition=old#canvas");
    const managed = updateWorkflowDefinitionIdInUrl(source, "workflow/opaque ?#", "artifacts");
    expect(`${managed.pathname}${managed.search}${managed.hash}`).toBe(
      "/workflows/definitions?backend=selected&definition=workflow%2Fopaque+%3F%23&panel=artifacts#canvas"
    );

    const ordinary = updateWorkflowDefinitionIdInUrl(managed, "another-definition");
    expect(ordinary.searchParams.get("definition")).toBe("another-definition");
    expect(ordinary.searchParams.has("panel")).toBe(false);
    expect(ordinary.searchParams.get("backend")).toBe("selected");

    const backToList = updateWorkflowDefinitionIdInUrl(ordinary, null);
    expect(backToList.searchParams.has("definition")).toBe(false);
    expect(backToList.searchParams.has("panel")).toBe(false);
    expect(backToList.searchParams.get("backend")).toBe("selected");
  });

  it("does not offer publication management for test-run-only, unmappable, or runtime-only rows", async () => {
    const testRunOnly = executableSummary({
      artifactId: "test-run-artifact",
      references: [executableReference({ scope: "TestRun", definitionId: "test-definition" })]
    });
    const unmappable = executableSummary({
      artifactId: "unmappable-artifact",
      references: [executableReference({ definitionId: "   " })]
    });
    const published = executableSummary({ artifactId: "runtime-only-artifact", references: [executableReference()] });
    vi.mocked(listExecutables).mockResolvedValue([testRunOnly, unmappable, published]);
    vi.mocked(listDefinitions).mockResolvedValue({ definitions: [definitionSummary("Nightly orders")], page: 1, pageSize: 100, totalCount: 1 });

    flushSync(() => root.render(<WorkflowExecutables
      context={{} as StudioEndpointContext}
      ai={emptyAi()}
      runInputEditors={[]}
      definitionFilter={null}
      onDefinitionFilterChange={() => {}}
      canManagePublications
    />));
    await flushAsyncUpdates();

    for (const artifactId of ["test-run-artifact", "unmappable-artifact"]) {
      const row = container.querySelector<HTMLElement>(`[aria-label="Inspect executable ${artifactId}"]`)!;
      expect(row.querySelector(".studio-overflow-menu")).toBeNull();
    }

    vi.mocked(listExecutables).mockResolvedValue([published]);
    flushSync(() => root.render(<WorkflowExecutables
      context={{} as StudioEndpointContext}
      ai={emptyAi()}
      runInputEditors={[]}
      definitionFilter={null}
      onDefinitionFilterChange={() => {}}
      canManagePublications={false}
    />));
    await flushAsyncUpdates();

    const runtimeRow = container.querySelector<HTMLElement>('[aria-label="Inspect executable runtime-only-artifact"]')!;
    expect(runtimeRow.querySelector(".studio-overflow-menu")).toBeNull();
  });

  it("unpublishes the sole live slot serving the exact artifact and refreshes the list", async () => {
    const definitionId = "workflow/live";
    const executable = executableSummary({
      artifactId: "artifact-target",
      definitionId: "legacy-primary-source",
      references: [
        executableReference({ sourceReferenceId: "published-first", definitionId }),
        executableReference({ sourceReferenceId: "published-duplicate", definitionId }),
        executableReference({ sourceReferenceId: "test-run", scope: "TestRun", definitionId: "test-run-only" })
      ]
    });
    const liveMatch = publicationSlotFor("production", "artifact-target", definitionId, 17);
    const otherArtifact = publicationSlotFor("old-slot", "artifact-old", definitionId, 18);
    const foreignSlot = { ...publicationSlotFor("foreign-slot", "artifact-target", definitionId, 19), activeActivationId: "foreign-activation", sourceKind: "artifact-reconciliation", publication: null };
    vi.mocked(listPublicationSlots).mockResolvedValue({ available: true, slots: [liveMatch, otherArtifact, foreignSlot] });

    const menu = await openExecutableRowActions(executable, { [definitionId]: "Live workflow" });
    clickMenuAction(menu, "Unpublish…");
    await flushAsyncUpdates();

    const dialog = document.body.querySelector<HTMLElement>('[role="dialog"]')!;
    expect(dialog.textContent).toContain("Live workflow");
    expect(dialog.textContent).toContain("Slot production");
    expect(dialog.textContent).toContain("Existing runs and other publication slots are unaffected.");
    expect(dialog.querySelectorAll('input[type="radio"]')).toHaveLength(1);
    expect(dialog.textContent).not.toContain("old-slot");
    expect(dialog.textContent).not.toContain("foreign-slot");
    clickDialogConfirm();
    await flushAsyncUpdates();

    expect(unpublishReviewedSlot).toHaveBeenCalledWith(expect.anything(), definitionId, "production", 17);
    expect(listPublicationSlots).toHaveBeenCalledTimes(1);
    expect(listExecutables).toHaveBeenCalledTimes(2);
    expect(document.body.querySelector('[role="dialog"]')).toBeNull();
  });

  it("requires an explicit choice when multiple live slots serve one shared artifact", async () => {
    const firstDefinitionId = "workflow/one";
    const secondDefinitionId = "workflow/two";
    const executable = executableSummary({ artifactId: "shared-artifact", references: [
      executableReference({ sourceReferenceId: "published-one", definitionId: firstDefinitionId }),
      executableReference({ sourceReferenceId: "published-one-again", definitionId: firstDefinitionId }),
      executableReference({ sourceReferenceId: "published-two", definitionId: secondDefinitionId })
    ] });
    vi.mocked(listPublicationSlots).mockImplementation(async (_context, definitionId) => ({
      available: true,
      slots: [publicationSlotFor(definitionId === firstDefinitionId ? "blue" : "green", "shared-artifact", definitionId, definitionId === firstDefinitionId ? 5 : 8)]
    }));

    const menu = await openExecutableRowActions(executable, { [firstDefinitionId]: "First workflow", [secondDefinitionId]: "Second workflow" });
    clickMenuAction(menu, "Unpublish…");
    await flushAsyncUpdates();

    const dialog = document.body.querySelector<HTMLElement>('[role="dialog"]')!;
    const choices = Array.from(dialog.querySelectorAll<HTMLInputElement>('input[type="radio"]'));
    expect(choices).toHaveLength(2);
    expect(choices.every(choice => !choice.checked)).toBe(true);
    const confirm = dialog.querySelector<HTMLButtonElement>('button[type="submit"]')!;
    expect(confirm.disabled).toBe(true);
    choices[1].click();
    expect(confirm.disabled).toBe(false);
    clickDialogConfirm();
    await flushAsyncUpdates();

    expect(unpublishReviewedSlot).toHaveBeenCalledWith(expect.anything(), secondDefinitionId, "green", 8);
    expect(listPublicationSlots).toHaveBeenCalledTimes(2);
  });

  it("cancels a reviewed unpublish without sending a command or refreshing", async () => {
    const definitionId = "workflow/cancel";
    const executable = executableSummary({ artifactId: "artifact-cancel", references: [executableReference({ definitionId })] });
    vi.mocked(listPublicationSlots).mockResolvedValue({ available: true, slots: [publicationSlotFor("default", "artifact-cancel", definitionId, 2)] });

    const menu = await openExecutableRowActions(executable);
    clickMenuAction(menu, "Unpublish…");
    await flushAsyncUpdates();
    const dialog = document.body.querySelector<HTMLElement>('[role="dialog"]')!;
    flushSync(() => Array.from(dialog.querySelectorAll<HTMLButtonElement>("button")).find(button => button.textContent?.trim() === "Cancel")!.click());

    expect(unpublishReviewedSlot).not.toHaveBeenCalled();
    expect(listExecutables).toHaveBeenCalledTimes(1);
  });

  it("does not allow unpublishing when no live slot serves the artifact", async () => {
    const definitionId = "workflow/no-match";
    const executable = executableSummary({ artifactId: "artifact-unmatched", references: [executableReference({ definitionId })] });
    vi.mocked(listPublicationSlots).mockResolvedValue({ available: true, slots: [publicationSlotFor("default", "another-artifact", definitionId, 3)] });

    const menu = await openExecutableRowActions(executable);
    clickMenuAction(menu, "Unpublish…");
    await flushAsyncUpdates();

    const dialog = document.body.querySelector<HTMLElement>('[role="dialog"]')!;
    expect(dialog.textContent).toContain("No live publication slots currently serve this artifact.");
    expect(dialog.querySelector<HTMLButtonElement>('button[type="submit"]')!.disabled).toBe(true);
    expect(unpublishReviewedSlot).not.toHaveBeenCalled();
  });

  it("shows slot-read failures and offers a retry without enabling unpublish", async () => {
    const definitionId = "workflow/read-error";
    const executable = executableSummary({ artifactId: "artifact-read-error", references: [executableReference({ definitionId })] });
    vi.mocked(listPublicationSlots)
      .mockResolvedValueOnce({ available: false, reason: "Runtime activation slot reads are unavailable." })
      .mockResolvedValueOnce({ available: true, slots: [publicationSlotFor("default", "artifact-read-error", definitionId, 4)] });

    const menu = await openExecutableRowActions(executable);
    clickMenuAction(menu, "Unpublish…");
    await flushAsyncUpdates();

    let dialog = document.body.querySelector<HTMLElement>('[role="dialog"]')!;
    expect(dialog.textContent).toContain("Runtime activation slot reads are unavailable.");
    expect(dialog.querySelector<HTMLButtonElement>('button[type="submit"]')!.disabled).toBe(true);
    flushSync(() => Array.from(dialog.querySelectorAll<HTMLButtonElement>("button")).find(button => button.textContent?.includes("Reload slots"))!.click());
    await flushAsyncUpdates();

    dialog = document.body.querySelector<HTMLElement>('[role="dialog"]')!;
    expect(dialog.querySelectorAll('input[type="radio"]')).toHaveLength(1);
    expect(listPublicationSlots).toHaveBeenCalledTimes(2);
  });

  it("hides reviewed unpublish when the backend does not advertise that command", async () => {
    const definitionId = "workflow/legacy";
    vi.mocked(getPublicationSlotLifecycleSupport).mockResolvedValue({ unpublish: true, unpublishReviewed: false, restore: false });
    const executable = executableSummary({ references: [executableReference({ definitionId })] });

    const menu = await openExecutableRowActions(executable);

    expect(Array.from(menu.querySelectorAll<HTMLButtonElement>("button"), button => button.textContent?.trim())).toContain("Manage publication");
    expect(Array.from(menu.querySelectorAll<HTMLButtonElement>("button"), button => button.textContent?.trim())).not.toContain("Unpublish…");
  });

  it("reloads the reviewed slot after a stale response and requires a fresh confirmation", async () => {
    const definitionId = "workflow/stale";
    const executable = executableSummary({ artifactId: "artifact-stale", references: [executableReference({ definitionId })] });
    vi.mocked(listPublicationSlots)
      .mockResolvedValueOnce({ available: true, slots: [publicationSlotFor("default", "artifact-stale", definitionId, 6)] })
      .mockResolvedValueOnce({ available: true, slots: [publicationSlotFor("default", "artifact-stale", definitionId, 7)] });
    vi.mocked(unpublishReviewedSlot)
      .mockRejectedValueOnce(new StudioHttpError(409, "The publication slot changed."))
      .mockResolvedValue({});

    const menu = await openExecutableRowActions(executable);
    clickMenuAction(menu, "Unpublish…");
    await flushAsyncUpdates();
    clickDialogConfirm();
    await flushAsyncUpdates();

    const dialog = document.body.querySelector<HTMLElement>('[role="dialog"]')!;
    expect(dialog.textContent).toContain("changed after you reviewed it");
    const freshChoice = dialog.querySelector<HTMLInputElement>('input[type="radio"]')!;
    expect(freshChoice.checked).toBe(false);
    expect(dialog.querySelector<HTMLButtonElement>('button[type="submit"]')!.disabled).toBe(true);
    freshChoice.click();
    clickDialogConfirm();
    await flushAsyncUpdates();

    expect(unpublishReviewedSlot).toHaveBeenNthCalledWith(1, expect.anything(), definitionId, "default", 6);
    expect(unpublishReviewedSlot).toHaveBeenNthCalledWith(2, expect.anything(), definitionId, "default", 7);
    expect(listPublicationSlots).toHaveBeenCalledTimes(2);
    expect(listExecutables).toHaveBeenCalledTimes(2);
    expect(document.body.querySelector('[role="dialog"]')).toBeNull();
  });

  it("shows a primary empty-state action and preserves the backend when opening definitions", async () => {
    window.history.replaceState({}, "", "/workflows/executables?backend=runtime");
    vi.mocked(listExecutables).mockResolvedValue([]);
    vi.mocked(listDefinitions).mockResolvedValue({ definitions: [], page: 1, pageSize: 100, totalCount: 0 });

    flushSync(() => root.render(<WorkflowExecutables
      context={{} as StudioEndpointContext}
      ai={emptyAi()}
      runInputEditors={[]}
      definitionFilter={null}
      onDefinitionFilterChange={() => {}}
    />));
    await flushAsyncUpdates();

    expect(container.textContent).toContain("No workflow executables");
    expect(container.textContent).toContain("Open workflow definitions");
    expect(container.querySelector('footer[aria-label="Workflow executable pagination"]')?.textContent).toContain("Showing 0-0 of 0");
    const open = Array.from(container.querySelectorAll("button")).find(button => button.textContent === "Open workflow definitions");
    expect(open).toBeTruthy();
    open!.click();
    expect(window.location.pathname).toBe("/workflows/definitions");
    expect(window.location.search).toBe("?backend=runtime");
  });

  it("inspects from plain cells while preserving source links, identifier actions and selected text", async () => {
    const listPath = "/workflows/executables?backend=runtime";
    window.history.replaceState({}, "", listPath);
    vi.mocked(listExecutables).mockResolvedValue([executableSummary()]);
    vi.mocked(listDefinitions).mockResolvedValue({ definitions: [definitionSummary("Nightly orders")], page: 1, pageSize: 100, totalCount: 1 });
    flushSync(() => root.render(<WorkflowExecutables context={{} as StudioEndpointContext} ai={emptyAi()} runInputEditors={[]} definitionFilter={null} onDefinitionFilterChange={() => {}} />));
    await flushAsyncUpdates();

    for (const selector of [".wf-executable-name", ".wf-executable-identifiers", ".wf-executable-version-cell", ".wf-executable-root", ".wf-executable-published"]) {
      window.history.replaceState({}, "", listPath);
      container.querySelector<HTMLElement>(selector)!.click();
      expect(window.location.pathname).toBe("/workflows/executables/artifact-1");
      expect(window.location.search).toBe("?backend=runtime");
    }

    window.history.replaceState({}, "", listPath);
    const sourceLink = container.querySelector<HTMLAnchorElement>("a.wf-executable-workflow-name")!;
    expect(new URL(sourceLink.href).searchParams.get("backend")).toBe("runtime");
    sourceLink.click();
    expect(window.location.pathname).toBe("/workflows/definitions");
    expect(new URLSearchParams(window.location.search).get("definition")).toBe("definition-1");

    window.history.replaceState({}, "", listPath);
    container.querySelector<HTMLButtonElement>('button[aria-label="Identifiers for Nightly orders"]')!.click();
    flushSync(() => {});
    expect(window.location.pathname).toBe("/workflows/executables");
    expect(document.body.querySelector('[role="dialog"][aria-label="Workflow executable identifiers for Nightly orders"]')).not.toBeNull();
    document.body.querySelector<HTMLElement>(".wf-definition-identifiers-popover")!.click();
    expect(window.location.pathname).toBe("/workflows/executables");

    const identifier = container.querySelector<HTMLElement>(".studio-copyable-identifier__value")!;
    const range = document.createRange();
    range.selectNodeContents(identifier);
    const selection = window.getSelection()!;
    selection.removeAllRanges();
    selection.addRange(range);
    identifier.click();
    expect(window.location.pathname).toBe("/workflows/executables");
    selection.removeAllRanges();
  });
});

function executableSummary(overrides: Partial<WorkflowExecutableSummary> = {}): WorkflowExecutableSummary {
  return {
    artifactId: "artifact-1",
    artifactVersion: "12.4",
    artifactHash: "sha256:artifact-hash",
    definitionId: "definition-1",
    definitionVersionId: "definition-version-1",
    sourceKind: "WorkflowDefinitionVersion",
    sourceId: "source-1",
    sourceVersion: "12.4",
    createdAt: "2026-10-18T00:00:00Z",
    rootActivityType: "Elsa.Workflows.WriteLine",
    rootActivityVersion: "1.0.0",
    nodeCount: 1,
    resumeTargetCount: 0,
    references: [],
    ...overrides
  };
}

function executableReference(overrides: Partial<WorkflowExecutableReference> = {}): WorkflowExecutableReference {
  return {
    sourceReferenceId: "reference-1",
    artifactId: "artifact-1",
    definitionId: "definition-1",
    definitionVersionId: "definition-version-1",
    artifactVersion: "12.4",
    createdAt: "2026-10-18T00:00:00Z",
    scope: "Published",
    ...overrides
  };
}

function definitionSummary(name: string, id = "definition-1"): WorkflowDefinitionSummary {
  return {
    id,
    name,
    createdAt: "2026-10-18T00:00:00Z",
    lastModifiedAt: "2026-10-18T00:00:00Z",
    versionCount: 1
  };
}

function emptyAi() {
  return {
    promptActions: { list: () => [] },
    dispatchPrompt: () => {}
  } as unknown as StudioWeaverRegistry;
}

async function openExecutableRowActions(executable: WorkflowExecutableSummary, names: Record<string, string> = {}) {
  const definitionIds = Array.from(new Set([executable.definitionId, ...(executable.references ?? []).map(reference => reference.definitionId)]))
    .filter((id): id is string => Boolean(id?.trim()));
  vi.mocked(listExecutables).mockResolvedValue([executable]);
  vi.mocked(listDefinitions).mockResolvedValue({
    definitions: definitionIds.map(id => definitionSummary(names[id] ?? id, id)),
    page: 1,
    pageSize: 100,
    totalCount: definitionIds.length
  });
  flushSync(() => root.render(<WorkflowExecutables
    context={{ baseUrl: "https://server.example" } as StudioEndpointContext}
    ai={emptyAi()}
    runInputEditors={[]}
    definitionFilter={null}
    onDefinitionFilterChange={() => {}}
    canManagePublications
  />));
  await flushAsyncUpdates();
  const trigger = container.querySelector<HTMLButtonElement>('button[aria-label^="Actions for"]')!;
  flushSync(() => trigger.click());
  await flushAsyncUpdates();
  return document.body.querySelector<HTMLElement>('[role="menu"]')!;
}

function publicationSlotFor(slotName: string, artifactId: string, definitionId: string, revision: number) {
  const slot = publishedSlot(slotName, { artifactId, definitionId });
  return { ...slot, definitionId, slotId: `${definitionId}:${slotName}`, revision };
}

function clickMenuAction(menu: HTMLElement, label: string) {
  const action = Array.from(menu.querySelectorAll<HTMLButtonElement>("button")).find(button => button.textContent?.includes(label));
  if (!action) throw new Error(`Menu action '${label}' was not rendered.`);
  flushSync(() => action.click());
}

function clickDialogConfirm() {
  const dialog = document.body.querySelector<HTMLElement>('[role="dialog"]');
  const confirm = dialog?.querySelector<HTMLButtonElement>('button[type="submit"]');
  if (!confirm) throw new Error("Dialog confirmation button was not rendered.");
  flushSync(() => confirm.click());
}

async function flushAsyncUpdates() {
  for (let i = 0; i < 3; i += 1) await new Promise<void>(resolve => setTimeout(resolve, 0));
  flushSync(() => {});
}
