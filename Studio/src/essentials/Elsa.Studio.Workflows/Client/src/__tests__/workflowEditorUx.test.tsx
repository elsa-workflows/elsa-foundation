import { flushSync } from "react-dom";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { StudioAiContributionApi } from "@elsa-workflows/studio-sdk";
import {
  describeWorkflowError,
  normalizeWorkflowError
} from "../workflow-editor/editorHelpers";
import {
  hasNonCollapsedTextSelection,
  isActivityClipboardShortcutBlocked,
  isWorkflowEditorKeyboardTarget,
  isWorkflowEditorOverlayOpen
} from "../workflow-editor/activityClipboardShortcuts";
import { WorkflowAlert } from "../workflow-editor/WorkflowAlert";
import { WorkflowRuntimePanel } from "../workflow-editor/editorPanels";
import { useAiProviderAvailability } from "../workflow-editor/useAiProviderAvailability";
import { formatActivityVersion } from "../workflowFormatting";
import type { WorkflowTestRunView } from "../workflowTypes";

let root: Root;
let container: HTMLDivElement;

beforeEach(() => {
  container = document.createElement("div");
  document.body.appendChild(container);
  root = createRoot(container);
});

afterEach(() => {
  flushSync(() => root.unmount());
  container.remove();
});

describe("formatActivityVersion", () => {
  it("splits a behavioral-hash version into a short label and full exact form", () => {
    const result = formatActivityVersion("1.0.0+892535311ec55c1b9930041e93cee112b2494283");
    expect(result.short).toBe("1.0.0");
    expect(result.full).toBe("1.0.0+892535311ec55c1b9930041e93cee112b2494283");
    expect(result.hasHash).toBe(true);
  });

  it("passes through a plain semantic version unchanged", () => {
    const result = formatActivityVersion("2.1.0");
    expect(result.short).toBe("2.1.0");
    expect(result.full).toBe("2.1.0");
    expect(result.hasHash).toBe(false);
  });
});

describe("describeWorkflowError", () => {
  it("lifts ProblemDetails detail, traceId, status, and code off a StudioHttpError-shaped failure", () => {
    const error = Object.assign(new Error("Publish blocked."), {
      status: 409,
      payload: { detail: "Publish blocked.", traceId: "00-abc123-def456-01", code: "VF-ACT-003", status: 409 }
    });
    const result = describeWorkflowError(error, "fallback");
    expect(result.message).toBe("Publish blocked.");
    expect(result.detail).toBe("Publish blocked.");
    expect(result.traceId).toBe("00-abc123-def456-01");
    expect(result.status).toBe(409);
    expect(result.code).toBe("VF-ACT-003");
  });

  it("falls back to the provided message when the error carries none", () => {
    expect(describeWorkflowError({}, "Could not save.").message).toBe("Could not save.");
    expect(describeWorkflowError(new Error("boom"), "fallback").message).toBe("boom");
  });

  it("adds safe expression diagnostic code, path, and range to structured error detail", () => {
    const error = Object.assign(new Error("Publication blocked."), {
      status: 422,
      payload: {
        detail: "Expression validation failed.",
        diagnostics: [{
          code: "LQ2001",
          message: "Unknown filter.",
          authoredPath: "activities.email.inputs.body",
          range: { start: { line: 0, character: 8 }, end: { line: 0, character: 14 } }
        }]
      }
    });

    expect(describeWorkflowError(error, "fallback").detail).toBe(
      "Expression validation failed.\n[LQ2001] activities.email.inputs.body · line 1, column 9: Unknown filter.");
  });
});

describe("normalizeWorkflowError", () => {
  it("clears on empty string and wraps non-empty strings", () => {
    expect(normalizeWorkflowError("")).toBeNull();
    expect(normalizeWorkflowError("   ")).toBeNull();
    expect(normalizeWorkflowError("Save failed.")).toEqual({ message: "Save failed." });
  });

  it("passes structured errors through and clears empty-message objects", () => {
    const structured = { message: "Save failed.", traceId: "trace-1" };
    expect(normalizeWorkflowError(structured)).toBe(structured);
    expect(normalizeWorkflowError({ message: "" })).toBeNull();
  });
});

describe("isWorkflowEditorKeyboardTarget", () => {
  it("treats nested code-editor content as editor-owned keyboard input", () => {
    const editor = document.createElement("section");
    editor.dataset.studioCodeEditor = "true";
    const nestedToken = document.createElement("span");
    editor.appendChild(nestedToken);

    expect(isWorkflowEditorKeyboardTarget(nestedToken)).toBe(true);
  });

  it("recognizes native and contenteditable inputs without swallowing canvas shortcuts elsewhere", () => {
    const input = document.createElement("input");
    const editable = document.createElement("div");
    editable.setAttribute("contenteditable", "true");
    const button = document.createElement("button");

    expect(isWorkflowEditorKeyboardTarget(input)).toBe(true);
    expect(isWorkflowEditorKeyboardTarget(editable)).toBe(true);
    expect(isWorkflowEditorKeyboardTarget(button)).toBe(false);
    expect(isWorkflowEditorKeyboardTarget(null)).toBe(false);
    expect(isWorkflowEditorKeyboardTarget(window)).toBe(false);
    expect(isWorkflowEditorKeyboardTarget(document)).toBe(false);
  });

  it("walks the keydown composedPath through a shadow tree into the code editor", () => {
    const editor = document.createElement("section");
    editor.dataset.studioCodeEditor = "true";
    const host = document.createElement("div");
    editor.appendChild(host);
    document.body.appendChild(editor);
    const inner = document.createElement("span");
    host.attachShadow({ mode: "open" }).appendChild(inner);

    let blocked: boolean | undefined;
    const listener = (event: KeyboardEvent) => {
      blocked = isActivityClipboardShortcutBlocked(event, editor);
    };
    window.addEventListener("keydown", listener, true);
    inner.dispatchEvent(new KeyboardEvent("keydown", {
      key: "d",
      ctrlKey: true,
      bubbles: true,
      composed: true
    }));
    window.removeEventListener("keydown", listener, true);

    expect(blocked).toBe(true);
    editor.remove();
  });

  it("treats Monaco and text-node targets as editor-owned without reading document.activeElement", () => {
    const monaco = document.createElement("div");
    monaco.className = "monaco-editor";
    const inputArea = document.createElement("textarea");
    inputArea.className = "inputarea";
    monaco.appendChild(inputArea);
    document.body.appendChild(monaco);

    const editor = document.createElement("section");
    editor.dataset.studioCodeEditor = "true";
    const token = document.createElement("span");
    token.appendChild(document.createTextNode("buyer.email"));
    editor.appendChild(token);
    document.body.appendChild(editor);

    const previouslyFocused = document.createElement("button");
    document.body.appendChild(previouslyFocused);
    previouslyFocused.focus();

    expect(isWorkflowEditorKeyboardTarget(inputArea)).toBe(true);
    expect(isWorkflowEditorKeyboardTarget(token.firstChild)).toBe(true);
    expect(document.activeElement).toBe(previouslyFocused);

    monaco.remove();
    editor.remove();
    previouslyFocused.remove();
  });
});

describe("activity clipboard shortcut gating", () => {
  it("blocks shortcuts while a dialog or menu is open", () => {
    const designer = document.createElement("section");
    const canvas = document.createElement("div");
    designer.appendChild(canvas);
    document.body.appendChild(designer);

    expect(isActivityClipboardShortcutBlocked(canvas, designer)).toBe(false);

    const dialog = document.createElement("div");
    dialog.setAttribute("role", "dialog");
    dialog.getBoundingClientRect = () => ({
      x: 0, y: 0, top: 0, left: 0, right: 240, bottom: 160, width: 240, height: 160, toJSON: () => ({})
    });
    const dialogButton = document.createElement("button");
    dialog.appendChild(dialogButton);
    designer.appendChild(dialog);

    expect(isWorkflowEditorOverlayOpen(designer)).toBe(true);
    expect(isActivityClipboardShortcutBlocked(dialogButton, designer)).toBe(true);
    expect(isActivityClipboardShortcutBlocked(canvas, designer)).toBe(true);

    dialog.style.display = "none";
    expect(isWorkflowEditorOverlayOpen(designer)).toBe(false);
    expect(isActivityClipboardShortcutBlocked(canvas, designer)).toBe(false);

    dialog.remove();
    designer.remove();
  });

  it("does not treat a menu outside the designer as an open designer overlay", () => {
    const designer = document.createElement("section");
    const canvas = document.createElement("div");
    designer.appendChild(canvas);
    document.body.appendChild(designer);

    const shellMenu = document.createElement("div");
    shellMenu.setAttribute("role", "menu");
    shellMenu.getBoundingClientRect = () => ({
      x: 0, y: 0, top: 0, left: 0, right: 180, bottom: 80, width: 180, height: 80, toJSON: () => ({})
    });
    document.body.appendChild(shellMenu);

    expect(isWorkflowEditorOverlayOpen(designer)).toBe(false);
    expect(isActivityClipboardShortcutBlocked(canvas, designer)).toBe(false);

    const portalMenu = document.createElement("div");
    portalMenu.className = "wf-editor-actions-popover";
    portalMenu.setAttribute("role", "menu");
    portalMenu.getBoundingClientRect = () => ({
      x: 0, y: 0, top: 0, left: 0, right: 200, bottom: 120, width: 200, height: 120, toJSON: () => ({})
    });
    document.body.appendChild(portalMenu);

    expect(isWorkflowEditorOverlayOpen(designer)).toBe(true);
    expect(isActivityClipboardShortcutBlocked(canvas, designer)).toBe(true);

    shellMenu.remove();
    portalMenu.remove();
    designer.remove();
  });

  it("blocks shortcuts whose target is outside the designer", () => {
    const designer = document.createElement("section");
    const outside = document.createElement("button");
    document.body.appendChild(designer);
    document.body.appendChild(outside);

    expect(isActivityClipboardShortcutBlocked(outside, designer)).toBe(true);
    expect(isActivityClipboardShortcutBlocked(document.body, designer)).toBe(false);
    expect(isActivityClipboardShortcutBlocked(window, designer)).toBe(false);
    expect(isActivityClipboardShortcutBlocked(document, designer)).toBe(false);

    designer.remove();
    outside.remove();
  });

  it("detects a non-collapsed text selection", () => {
    const paragraph = document.createElement("p");
    paragraph.textContent = "Validation failed";
    document.body.appendChild(paragraph);
    const range = document.createRange();
    range.selectNodeContents(paragraph);
    const selection = window.getSelection();
    selection?.removeAllRanges();
    selection?.addRange(range);

    expect(hasNonCollapsedTextSelection()).toBe(true);

    selection?.removeAllRanges();
    expect(hasNonCollapsedTextSelection()).toBe(false);
    paragraph.remove();
  });
});

describe("WorkflowAlert", () => {
  it("renders the message, distinct detail, status, code, a copyable traceId, and a dismiss control", () => {
    let dismissed = false;
    flushSync(() => root.render(
      <WorkflowAlert
        error={{ message: "The test run could not be dispatched.", detail: "Root activity is missing.", traceId: "00-trace-01", status: 500, code: "VF-ACT-003" }}
        onDismiss={() => { dismissed = true; }}
        onCopied={() => {}}
        onCopyFailed={() => {}}
      />
    ));

    expect(container.querySelector(".wf-alert-message")?.textContent).toBe("The test run could not be dispatched.");
    expect(container.querySelector(".wf-alert-detail")?.textContent).toBe("Root activity is missing.");
    expect(container.querySelector(".wf-alert-status")?.textContent).toBe("HTTP 500");
    expect(container.querySelector(".wf-alert-code")?.textContent).toBe("VF-ACT-003");
    expect(container.querySelector(".wf-alert-trace code")?.textContent).toBe("00-trace-01");
    expect(container.querySelector("button[aria-label='Copy trace id']")).not.toBeNull();

    const dismiss = container.querySelector<HTMLButtonElement>("button[aria-label='Dismiss error']")!;
    flushSync(() => dismiss.click());
    expect(dismissed).toBe(true);
  });

  it("hides the detail line when it merely repeats the message", () => {
    flushSync(() => root.render(
      <WorkflowAlert
        error={{ message: "Publish blocked.", detail: "Publish blocked." }}
        onDismiss={() => {}}
        onCopied={() => {}}
        onCopyFailed={() => {}}
      />
    ));
    expect(container.querySelector(".wf-alert-detail")).toBeNull();
    expect(container.querySelector(".wf-alert-trace")).toBeNull();
  });
});

describe("accepted test runs with incidents", () => {
  it("reports accepted dispatch with incidents and links directly to the Run", () => {
    const onOpenRun = vi.fn();
    const testRun: WorkflowTestRunView = {
      testRunId: "test-run-1",
      definitionId: "definition-1",
      definitionVersionId: "draft:1",
      workflowExecutionId: "run-1",
      artifactId: "artifact-1",
      status: "DispatchAccepted",
      commandDispatchStatus: "Accepted",
      incidentCount: 1
    };
    flushSync(() => root.render(<WorkflowRuntimePanel testRun={testRun} onOpenRun={onOpenRun} />));

    expect(container.querySelector(".wf-runtime-card")?.getAttribute("data-state")).toBe("accepted-with-incidents");
    expect(container.textContent).toContain("The test run was accepted and recorded 1 incident");
    const review = [...container.querySelectorAll<HTMLButtonElement>("button")].find(button => button.textContent === "Review incidents");
    expect(review).toBeTruthy();
    flushSync(() => review?.click());
    expect(onOpenRun).toHaveBeenCalledWith("run-1", "issues");
  });

  it("warns from an AcceptedButFaulted receipt even when the incident count is unavailable", () => {
    const onOpenRun = vi.fn();
    const testRun: WorkflowTestRunView = {
      testRunId: "test-run-legacy",
      definitionId: "definition-1",
      definitionVersionId: "draft:1",
      workflowExecutionId: "run-legacy",
      status: "DispatchAccepted",
      commandDispatchStatus: "AcceptedButFaulted"
    };
    flushSync(() => root.render(<WorkflowRuntimePanel testRun={testRun} onOpenRun={onOpenRun} />));

    expect(container.querySelector(".wf-runtime-card")?.getAttribute("data-state")).toBe("accepted-with-incidents");
    expect(container.textContent).toContain("accepted with a runtime issue");
    expect(container.textContent).toContain("Incident details may be unavailable");
    const review = [...container.querySelectorAll<HTMLButtonElement>("button")].find(button => button.textContent === "Review incidents");
    flushSync(() => review?.click());
    expect(onOpenRun).toHaveBeenCalledWith("run-legacy", "issues");
  });

  it("keeps the Run ID link on the default timeline destination", () => {
    const onOpenRun = vi.fn();
    const testRun: WorkflowTestRunView = {
      testRunId: "test-run-generic",
      definitionId: "definition-1",
      definitionVersionId: "draft:1",
      workflowExecutionId: "run-generic",
      status: "DispatchAccepted",
      commandDispatchStatus: "Accepted"
    };
    flushSync(() => root.render(<WorkflowRuntimePanel testRun={testRun} onOpenRun={onOpenRun} />));

    const runLink = [...container.querySelectorAll<HTMLButtonElement>("button")]
      .find(button => button.textContent === "run-generic");
    flushSync(() => runLink?.click());

    expect(onOpenRun).toHaveBeenCalledWith("run-generic");
  });

  it("keeps an ordinary accepted legacy receipt green when no fault evidence is present", () => {
    const testRun: WorkflowTestRunView = {
      testRunId: "test-run-accepted",
      definitionId: "definition-1",
      definitionVersionId: "draft:1",
      status: "DispatchAccepted",
      commandDispatchStatus: "Accepted"
    };
    flushSync(() => root.render(<WorkflowRuntimePanel testRun={testRun} onOpenRun={() => {}} />));

    expect(container.querySelector(".wf-runtime-card")?.getAttribute("data-state")).toBe("accepted");
    expect(container.querySelector(".wf-runtime-incident-warning")).toBeNull();
  });
});

describe("useAiProviderAvailability", () => {
  function createAvailabilityStub(initial: boolean) {
    const listeners = new Set<(available: boolean) => void>();
    let current = initial;
    return {
      get: () => current,
      set(available: boolean) {
        if (available === current) return;
        current = available;
        for (const listener of listeners) listener(available);
      },
      subscribe(listener: (available: boolean) => void) {
        listeners.add(listener);
        listener(current);
        return () => listeners.delete(listener);
      }
    };
  }

  function Probe({ ai }: { ai: StudioAiContributionApi }) {
    const available = useAiProviderAvailability(ai);
    return <span data-testid="value">{available ? "available" : "unavailable"}</span>;
  }

  it("reflects the current value and reacts to provider availability changes", () => {
    const providerAvailability = createAvailabilityStub(false);
    const ai = { providerAvailability } as unknown as StudioAiContributionApi;

    flushSync(() => root.render(<Probe ai={ai} />));
    expect(container.querySelector("[data-testid='value']")?.textContent).toBe("unavailable");

    flushSync(() => providerAvailability.set(true));
    expect(container.querySelector("[data-testid='value']")?.textContent).toBe("available");
  });
});
