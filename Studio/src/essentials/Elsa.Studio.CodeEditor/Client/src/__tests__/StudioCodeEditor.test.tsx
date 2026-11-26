import { acceptCompletion, completionStatus, currentCompletions, startCompletion } from "@codemirror/autocomplete";
import { syntaxTree } from "@codemirror/language";
import type { Extension } from "@codemirror/state";
import { EditorView } from "@codemirror/view";
import { flushSync } from "react-dom";
import { createRoot } from "react-dom/client";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { StudioCodeEditor } from "../StudioCodeEditor";
import { CodeMirrorStudioCodeEditor } from "../engines/CodeMirrorStudioCodeEditor";
import { FallbackCodeEditor } from "../engines/FallbackCodeEditor";
import * as codeMirrorLanguages from "../engines/codeMirrorLanguages";
import { javaScriptLanguageAdapter } from "../languages/javascript";
import { createStudioCodeToolingProjection } from "../toolingProjection";
import type { StudioCodeCompletionRequest, StudioCodeDocument, StudioCodeEditorController, StudioCodeEditorEngineProps, StudioCodeEditorProps } from "../types";

vi.mock("../engines/codeMirrorLanguages", async importOriginal => {
  const original = await importOriginal<typeof codeMirrorLanguages>();
  return { loadCodeMirrorLanguageExtensions: vi.fn(original.loadCodeMirrorLanguageExtensions) };
});

beforeEach(() => {
  window.dispatchEvent(new Event("elsa:auth-session-started"));
});

describe("StudioCodeEditor", () => {
  it("carries the loading textarea selection into the rich editor's first insertion", async () => {
    let resolveEditor!: (editor: { default: typeof CodeMirrorStudioCodeEditor }) => void;
    const loading = new Promise<{ default: typeof CodeMirrorStudioCodeEditor }>(resolve => { resolveEditor = resolve; });
    const register = vi.fn<(controller: StudioCodeEditorController | null) => void>();
    const onChange = vi.fn();
    const { container, unmount } = renderEditor({
      document: codeDocument({ value: "0" }),
      languageAdapter: { language: "javascript", displayName: "JavaScript", loadEditor: () => loading },
      actions: <button type="button">Insert data</button>,
      documentEditing: { historyValue: {}, onController: register },
      onChange
    });

    try {
      const textarea = editorInput(container);
      textarea.focus();
      textarea.setSelectionRange(0, 1, "backward");
      container.querySelector("button")!.focus();
      expect(register).not.toHaveBeenCalled();

      resolveEditor({ default: CodeMirrorStudioCodeEditor });
      await vi.waitFor(() => expect(register).toHaveBeenCalledOnce());
      const controller = register.mock.calls[0]![0]!;
      const captured = controller.capture()!;
      expect(captured).toMatchObject({ source: "0", selection: { anchor: 1, head: 0 } });
      expect(controller.apply({ expected: captured, insert: "args.data", historyValue: {} })).toBe(true);
      expect(onChange).toHaveBeenCalledWith(expect.objectContaining({ value: "args.data" }), expect.objectContaining({
        origin: "insertion"
      }));
    } finally {
      unmount();
    }
  });

  it("does not hand off an untouched fallback cursor during expansion", () => {
    const host = document.createElement("div");
    document.body.append(host);
    const root = createRoot(host);
    const fallbackSelection: NonNullable<StudioCodeEditorEngineProps["fallbackSelection"]> = { current: null };
    const props: StudioCodeEditorEngineProps = {
      document: codeDocument({ value: "args.data" }), profile: "expanded", session: { id: "untouched-fallback" },
      fallbackSelection, readOnly: false, theme: "studio", minHeight: "220px",
      ariaLabel: "Expression", diagnostics: [], onChange() {}
    };
    flushSync(() => root.render(<FallbackCodeEditor {...props} />));
    editorInput(host).focus();
    flushSync(() => root.unmount());
    host.remove();
    expect(fallbackSelection.current).toBeNull();
  });

  it("renders the fallback editor for unsupported languages and emits document changes", () => {
    const document = codeDocument({ language: "liquid", value: "{{ total }}" });
    const onChange = vi.fn();
    const { container, unmount } = renderEditor({ document, onChange });

    const textarea = editorInput(container);
    expect(textarea.value).toBe("{{ total }}");
    expect(textarea.getAttribute("aria-label")).toBe("Global JavaScript function");
    expect(container.querySelector(".studio-code-editor")?.getAttribute("data-language")).toBe("liquid");

    fill(textarea, "{{ subtotal }}");

    expect(onChange).toHaveBeenCalledWith({ ...document, value: "{{ subtotal }}" });
    expect(container.querySelector(".studio-code-editor-rich")).toBeNull();
    unmount();
  });

  it("clears the ready flag on a surviving code-editor root when FallbackCodeEditor unmounts", () => {
    const host = document.createElement("div");
    document.body.appendChild(host);
    const wrapper = document.createElement("section");
    wrapper.dataset.studioCodeEditor = "true";
    host.appendChild(wrapper);
    const mount = document.createElement("div");
    wrapper.appendChild(mount);
    const root = createRoot(mount);
    const props: StudioCodeEditorEngineProps = {
      document: codeDocument({ language: "liquid", value: "{{ total }}" }),
      profile: "expanded",
      session: { id: "fallback-unmount" },
      readOnly: false,
      theme: "studio",
      minHeight: "220px",
      ariaLabel: "Expression",
      diagnostics: [],
      onChange: vi.fn()
    };

    flushSync(() => root.render(<FallbackCodeEditor {...props} />));
    expect(wrapper.getAttribute("data-studio-code-editor-ready")).toBe("true");

    flushSync(() => root.unmount());
    expect(wrapper.isConnected).toBe(true);
    expect(wrapper.hasAttribute("data-studio-code-editor-ready")).toBe(false);
    host.remove();
  });

  it("keeps readonly documents selectable without emitting changes", () => {
    const onChange = vi.fn();
    const { container, unmount } = renderEditor({ document: codeDocument({ language: "liquid" }), readOnly: true, onChange });
    const textarea = editorInput(container);

    expect(textarea.readOnly).toBe(true);
    expect(textarea.getAttribute("aria-readonly")).toBe("true");

    fill(textarea, "changed");

    expect(onChange).not.toHaveBeenCalled();
    unmount();
  });

  it("removes source from an already mounted editor when authorization is revoked", () => {
    const { container, unmount } = renderEditor({
      document: codeDocument({ language: "liquid", value: "sensitive expression" })
    });

    expect(editorInput(container).value).toBe("sensitive expression");
    flushSync(() => window.dispatchEvent(new Event("elsa:auth-session-ended")));

    expect(container.querySelector("textarea")).toBeNull();
    expect(container.textContent).not.toContain("sensitive expression");
    expect(container.textContent).toContain("authorization session changed");
    unmount();
  });

  it("keeps source hidden when an editor remounts in a tooling-revoked workflow scope", () => {
    const sessionKey = "workflow-revoked\u001felsa://expressions/secret";
    const document = codeDocument({ language: "liquid", value: "sensitive expression" });
    const first = renderEditor({ document, sessionKey });

    flushSync(() => window.dispatchEvent(new CustomEvent("elsa:expression-tooling-authorization-revoked", {
      detail: { scope: "workflow-revoked" }
    })));
    expect(first.container.textContent).not.toContain("sensitive expression");
    first.unmount();

    window.dispatchEvent(new Event("elsa:auth-session-started"));
    const second = renderEditor({ document, sessionKey });
    expect(second.container.querySelector("textarea")).toBeNull();
    expect(second.container.textContent).not.toContain("sensitive expression");
    expect(second.container.textContent).toContain("authorization session changed");

    flushSync(() => window.dispatchEvent(new CustomEvent("elsa:expression-tooling-authorization-restored", {
      detail: { scope: "workflow-revoked" }
    })));
    expect(editorInput(second.container).value).toBe("sensitive expression");
    second.unmount();
  });

  it("destroys active and parked rich source immediately when authorization is revoked", async () => {
    const { container, unmount } = renderEditor({
      document: codeDocument({ value: "sensitiveRichExpression" }),
      languageAdapter: javaScriptLanguageAdapter,
      profile: "compact"
    });

    click(container.querySelector<HTMLButtonElement>(".studio-code-editor-preview")!);
    await waitFor(() => !!container.querySelector(".cm-content"));

    window.dispatchEvent(new Event("elsa:auth-session-ended"));

    expect(container.querySelector(".cm-editor")).toBeNull();
    expect(container.textContent).not.toContain("sensitiveRichExpression");
    await waitFor(() => container.textContent?.includes("authorization session changed") ?? false);
    unmount();
  }, 20000);

  it("destroys parked rich source immediately when authorization is revoked", async () => {
    const { container, unmount } = renderEditor({
      document: codeDocument({ value: "parkedSensitiveRichExpression" }),
      languageAdapter: javaScriptLanguageAdapter,
      profile: "compact"
    });

    click(container.querySelector<HTMLButtonElement>(".studio-code-editor-preview")!);
    await waitFor(() => !!container.querySelector(".cm-content"));
    container.querySelector<HTMLElement>(".cm-content")!
      .dispatchEvent(new FocusEvent("blur", { bubbles: true }));
    await waitFor(() => !!container.querySelector(".studio-code-editor-preview"));

    window.dispatchEvent(new Event("elsa:auth-session-ended"));

    expect(container.querySelector(".cm-editor")).toBeNull();
    await waitFor(() => container.textContent?.includes("authorization session changed") ?? false);
    expect(container.textContent).not.toContain("parkedSensitiveRichExpression");
    unmount();
  }, 20000);

  it("renders only diagnostics for the active document", () => {
    const { container, unmount } = renderEditor({
      document: codeDocument({ uri: "elsa://functions/tax.liquid", language: "liquid" }),
      diagnostics: [
        { uri: "elsa://functions/tax.liquid", severity: "warning", code: "LQ001", message: "Check this expression.", startLineNumber: 2, startColumn: 4 },
        { uri: "elsa://functions/other.liquid", severity: "error", code: "LQ999", message: "Wrong document." },
        { severity: "info", message: "General editor hint." }
      ]
    });

    expect(container.textContent).toContain("LQ001");
    expect(container.textContent).toContain("2:4");
    expect(container.textContent).toContain("Check this expression.");
    expect(container.textContent).toContain("General editor hint.");
    expect(container.textContent).not.toContain("Wrong document.");
    expect(container.querySelector(".studio-code-editor-diagnostics")?.getAttribute("role")).toBe("status");
    unmount();
  });

  it("uses language adapter metadata without exposing engine-specific details", () => {
    const { container, unmount } = renderEditor({
      languageAdapter: {
        language: "liquid",
        displayName: "Liquid"
      },
      document: codeDocument({ language: "liquid" })
    });

    expect(container.querySelector(".studio-code-editor-header")?.textContent).toContain("Liquid");
    expect(container.querySelector(".studio-code-editor")?.getAttribute("data-language")).toBe("liquid");
    unmount();
  });

  it("activates a compact preview on focus and keeps pasted newlines while requesting expansion", () => {
    const onChange = vi.fn();
    const onExpand = vi.fn();
    const onNewline = vi.fn();
    const { container, unmount } = renderEditor({
      document: codeDocument({ language: "liquid", value: "{{ total }}" }),
      profile: "compact",
      onChange,
      onExpand,
      onNewline
    });

    const preview = container.querySelector<HTMLButtonElement>(".studio-code-editor-preview")!;
    expect(preview.textContent).toContain("{{ total }}");
    click(preview);

    const textarea = editorInput(container);
    fill(textarea, "{{ total }}\n{{ tax }}");

    expect(onChange).toHaveBeenCalledWith(expect.objectContaining({ value: "{{ total }}\n{{ tax }}" }));
    expect(onNewline).toHaveBeenCalledOnce();
    expect(onExpand).toHaveBeenCalledOnce();
    unmount();
  });

  it("uses Enter to expand, Tab to indent, and both documented shortcuts to leave a compact fallback editor", () => {
    const onExpand = vi.fn();
    const { container, unmount } = renderEditor({
      document: codeDocument({ language: "liquid" }),
      profile: "compact",
      onExpand
    });

    click(container.querySelector<HTMLButtonElement>(".studio-code-editor-preview")!);
    const textarea = editorInput(container);
    const enter = new KeyboardEvent("keydown", { key: "Enter", bubbles: true, cancelable: true });
    textarea.dispatchEvent(enter);
    const tab = new KeyboardEvent("keydown", { key: "Tab", bubbles: true, cancelable: true });
    textarea.dispatchEvent(tab);
    const escape = new KeyboardEvent("keydown", { key: "Escape", bubbles: true, cancelable: true });
    textarea.dispatchEvent(escape);
    const escapeTab = new KeyboardEvent("keydown", { key: "Tab", bubbles: true, cancelable: true });
    textarea.dispatchEvent(escapeTab);
    const controlM = new KeyboardEvent("keydown", { key: "m", ctrlKey: true, bubbles: true, cancelable: true });
    textarea.dispatchEvent(controlM);
    const controlMTab = new KeyboardEvent("keydown", { key: "Tab", bubbles: true, cancelable: true });
    textarea.dispatchEvent(controlMTab);

    expect(onExpand).toHaveBeenCalledOnce();
    expect(enter.defaultPrevented).toBe(true);
    expect(tab.defaultPrevented).toBe(true);
    expect(escapeTab.defaultPrevented).toBe(false);
    expect(controlM.defaultPrevented).toBe(true);
    expect(controlMTab.defaultPrevented).toBe(false);
    unmount();
  });

  it("preserves selected fallback source while indenting and outdenting complete lines", () => {
    const onIndent = vi.fn();
    const indented = renderEditor({
      document: codeDocument({ language: "liquid", value: "one\ntwo" }),
      onChange: onIndent
    });
    const indentInput = editorInput(indented.container);
    indentInput.setSelectionRange(0, indentInput.value.length);
    indentInput.dispatchEvent(new KeyboardEvent("keydown", {
      key: "Tab",
      bubbles: true,
      cancelable: true
    }));
    expect(onIndent).toHaveBeenLastCalledWith(expect.objectContaining({ value: "  one\n  two" }));
    indented.unmount();

    const onOutdent = vi.fn();
    const outdented = renderEditor({
      document: codeDocument({ language: "liquid", value: "  one\n  two" }),
      onChange: onOutdent
    });
    const outdentInput = editorInput(outdented.container);
    outdentInput.setSelectionRange(0, outdentInput.value.length);
    outdentInput.dispatchEvent(new KeyboardEvent("keydown", {
      key: "Tab",
      shiftKey: true,
      bubbles: true,
      cancelable: true
    }));
    expect(onOutdent).toHaveBeenLastCalledWith(expect.objectContaining({ value: "one\ntwo" }));
    outdented.unmount();
  });

  it("lazy-loads the rich editor for JavaScript documents", async () => {
    const { container, unmount } = renderEditor({
      document: codeDocument({ language: "javascript", value: "return total;" }),
      languageAdapter: javaScriptLanguageAdapter,
      theme: "dark"
    });

    expect(editorInput(container).value).toBe("return total;");
    await waitFor(() => !!container.querySelector(".cm-gutters"));

    expect(container.querySelector(".studio-code-editor-rich")).toBeTruthy();
    expect(container.querySelector("[aria-label='Global JavaScript function']")).toBeTruthy();
    expect(container.querySelector(".studio-code-editor-header")?.textContent).toContain("JavaScript");
    expect(container.querySelector(".studio-code-editor")?.getAttribute("data-theme")).toBe("dark");
    expect(container.querySelector(".studio-code-editor")?.getAttribute("data-studio-code-editor")).toBe("true");
    expect(container.querySelector(".studio-code-editor")?.getAttribute("data-studio-code-editor-ready")).toBe("true");
    expect(container.querySelector(".cm-gutters")).toBeTruthy();
    unmount();
  }, 20000);

  it("shows keyboard-accessible signature help in the rich editor", async () => {
    const { container, unmount } = renderEditor({
      document: codeDocument({ value: "formatTotal(" }),
      languageAdapter: javaScriptLanguageAdapter,
      signatureProvider: async () => ({ label: "formatTotal(value)", documentation: { markdown: "Formats a total." } })
    });

    await waitFor(() => !!container.querySelector(".studio-code-editor-signature"));

    const signature = container.querySelector(".studio-code-editor-signature")!;
    expect(signature.textContent).toContain("formatTotal(value)");
    expect(signature.getAttribute("role")).toBe("status");
    unmount();
  }, 20000);

  it("shows and announces hover help from the keyboard before Escape arms focus exit", async () => {
    const { container, unmount } = renderEditor({
      document: codeDocument({ value: "formatTotal" }),
      languageAdapter: javaScriptLanguageAdapter,
      hoverProvider: async () => ({ documentation: { markdown: "Formats a total." } })
    });

    await waitFor(() => !!container.querySelector<HTMLElement>(".cm-content"));
    const content = container.querySelector<HTMLElement>(".cm-content")!;
    content.dispatchEvent(new KeyboardEvent("keydown", {
      key: "i",
      altKey: true,
      bubbles: true,
      cancelable: true
    }));
    await waitFor(() => !!container.querySelector(".studio-code-editor-keyboard-hover"));

    const hover = container.querySelector(".studio-code-editor-keyboard-hover")!;
    expect(hover.textContent).toBe("Formats a total.");
    expect(hover.getAttribute("role")).toBe("status");
    expect(key(content, "Escape").defaultPrevented).toBe(true);
    await waitFor(() => !container.querySelector(".studio-code-editor-keyboard-hover"));
    expect(key(content, "Tab").defaultPrevented).toBe(true);
    unmount();
  }, 20000);

  it("cancels in-flight keyboard hover help on Escape", async () => {
    let resolveHover: ((value: { documentation: { markdown: string } }) => void) | undefined;
    const hoverProvider = vi.fn((_document, _position, signal: AbortSignal) =>
      new Promise<{ documentation: { markdown: string } }>((resolve, reject) => {
        resolveHover = resolve;
        signal.addEventListener("abort", () => reject(signal.reason), { once: true });
      }));
    const { container, unmount } = renderEditor({
      document: codeDocument({ value: "formatTotal" }),
      languageAdapter: javaScriptLanguageAdapter,
      hoverProvider
    });

    await waitFor(() => !!container.querySelector<HTMLElement>(".cm-content"));
    const content = container.querySelector<HTMLElement>(".cm-content")!;
    content.dispatchEvent(new KeyboardEvent("keydown", {
      key: "i",
      altKey: true,
      bubbles: true,
      cancelable: true
    }));
    await waitFor(() => hoverProvider.mock.calls.length === 1);

    expect(key(content, "Escape").defaultPrevented).toBe(true);
    resolveHover?.({ documentation: { markdown: "Must stay hidden." } });
    await Promise.resolve();
    expect(container.querySelector(".studio-code-editor-keyboard-hover")).toBeNull();
    expect(key(content, "Tab").defaultPrevented).toBe(true);
    unmount();
  }, 20000);

  it("cancels in-flight hover help when authorization is revoked", async () => {
    let requestSignal: AbortSignal | undefined;
    const hoverProvider = vi.fn((_document, _position, signal: AbortSignal) => {
      requestSignal = signal;
      return new Promise<{ documentation: { markdown: string } }>(() => {});
    });
    const { container, unmount } = renderEditor({
      document: codeDocument({ value: "sensitiveHoverTarget" }),
      languageAdapter: javaScriptLanguageAdapter,
      hoverProvider
    });

    await waitFor(() => !!container.querySelector<HTMLElement>(".cm-content"));
    container.querySelector<HTMLElement>(".cm-content")!.dispatchEvent(new KeyboardEvent("keydown", {
      key: "i",
      altKey: true,
      bubbles: true,
      cancelable: true
    }));
    await waitFor(() => !!requestSignal);

    window.dispatchEvent(new Event("elsa:auth-session-ended"));

    expect(requestSignal?.aborted).toBe(true);
    expect(container.querySelector(".cm-editor")).toBeNull();
    unmount();
  }, 20000);

  it.each([
    { scenario: "while the query is pending", prefix: "variables.customer", remaining: "", result: "pending", filter: "" },
    { scenario: "after an empty result", prefix: "variables.customer", remaining: "", result: "empty", filter: "" },
    { scenario: "while the author types a member prefix", prefix: "variables.customer", remaining: "", result: "pending", filter: "s" },
    { scenario: "inside a template interpolation", prefix: "`Hi ${variables.customer", remaining: "}`", result: "pending", filter: "" }
  ])("restores implicit member completion $scenario", async ({ prefix, remaining, result, filter }) => {
    const source = prefix + remaining;
    const props = { document: codeDocument({ value: source }), languageAdapter: javaScriptLanguageAdapter };
    const originalProvider = vi.fn(({ signal }: StudioCodeCompletionRequest) => result === "pending"
      ? new Promise<never>((_, reject) => signal.addEventListener("abort", () => reject(signal.reason), { once: true }))
      : []);
    const refreshedProvider = vi.fn(() => [{ label: "street" }]);
    const { container, rerender, unmount } = renderEditor({ ...props, completionProvider: originalProvider });

    try {
      await waitFor(() => !!container.querySelector(".cm-content"));
      const content = container.querySelector<HTMLElement>(".cm-content")!;
      const view = EditorView.findFromDOM(content)!;
      await vi.waitFor(() => expect(syntaxTree(view.state).type.name).toBe("Script"));
      view.dispatch({ changes: { from: prefix.length, insert: "." }, selection: { anchor: prefix.length + 1 }, userEvent: "input.type" });
      await vi.waitFor(() => {
        expect(originalProvider).toHaveBeenCalledWith(expect.objectContaining({ explicit: false }));
        expect(completionStatus(view.state)).toBe(result === "pending" ? "pending" : null);
      });

      if (filter) {
        view.dispatch({ changes: { from: prefix.length + 1, insert: filter }, selection: { anchor: prefix.length + 1 + filter.length }, userEvent: "input.type" });
      }
      const edited = { ...props, document: { ...props.document, value: prefix + "." + filter + remaining } };
      rerender({ ...edited, completionProvider: refreshedProvider });

      await vi.waitFor(() => {
        expect(refreshedProvider).toHaveBeenCalledWith(expect.objectContaining({ explicit: false, position: prefix.length + 1 + filter.length }));
        expect(container.querySelector(".cm-completionLabel")?.textContent).toBe("street");
      });
      if (result === "pending") expect(originalProvider.mock.calls[0]![0].signal.aborted).toBe(true);
      key(content, "Escape");
      rerender({ ...edited, completionProvider: () => [{ label: "dismissed" }] });
      expect(completionStatus(view.state)).toBeNull();
    } finally {
      unmount();
    }
  }, 20000);

  it("restores implicit member completion when the JavaScript grammar finishes loading", async () => {
    const languageLoader = vi.mocked(codeMirrorLanguages.loadCodeMirrorLanguageExtensions);
    const loadExtensions = languageLoader.getMockImplementation()!;
    const extensions = await loadExtensions("javascript");
    let finishLoading!: (value: Extension[]) => void;
    const loading = new Promise<Extension[]>(resolve => { finishLoading = resolve; });
    languageLoader.mockReturnValue(loading);
    const source = "variables.customer";
    const props = {
      document: codeDocument({ uri: "elsa://expressions/delayed-javascript", value: source }),
      languageAdapter: javaScriptLanguageAdapter
    };
    const refreshedProvider = vi.fn(() => [{ label: "street" }]);
    const { container, rerender, unmount } = renderEditor({ ...props, completionProvider: () => null });

    try {
      await waitFor(() => !!container.querySelector(".cm-editor"));
      const view = EditorView.findFromDOM(container.querySelector<HTMLElement>(".cm-editor")!)!;
      expect(syntaxTree(view.state).type.name).not.toBe("Script");
      view.dispatch({ changes: { from: source.length, insert: "." }, selection: { anchor: source.length + 1 }, userEvent: "input.type" });
      rerender({ ...props, document: { ...props.document, value: source + "." }, completionProvider: refreshedProvider });
      expect(completionStatus(view.state)).toBeNull();
      expect(refreshedProvider).not.toHaveBeenCalled();

      finishLoading(extensions);

      await vi.waitFor(() => {
        expect(refreshedProvider).toHaveBeenCalledWith(expect.objectContaining({ explicit: false, position: source.length + 1 }));
        expect(container.querySelector(".cm-completionLabel")?.textContent).toBe("street");
      });
    } finally {
      unmount();
      languageLoader.mockImplementation(loadExtensions);
    }
  }, 20000);

  it.each(["string", "comment", "template"] as const)("clears implicit completion after a period in a JavaScript %s", async kind => {
    const source = { string: '"variables.customer', comment: "// variables.customer", template: "`variables.customer" }[kind];
    const props = { document: codeDocument({ value: source }), languageAdapter: javaScriptLanguageAdapter };
    const { container, rerender, unmount } = renderEditor({ ...props, completionProvider: () => null });

    try {
      await waitFor(() => !!container.querySelector(".cm-editor"));
      const view = EditorView.findFromDOM(container.querySelector<HTMLElement>(".cm-editor")!)!;
      await vi.waitFor(() => expect(syntaxTree(view.state).type.name).toBe("Script"));
      view.dispatch({ changes: { from: source.length, insert: "." }, selection: { anchor: source.length + 1 }, userEvent: "input.type" });
      expect(completionStatus(view.state)).toBe("pending");

      rerender({ ...props, document: { ...props.document, value: source + "." }, completionProvider: () => [{ label: "street" }] });

      expect(completionStatus(view.state)).toBeNull();
    } finally {
      unmount();
    }
  }, 20000);

  it("formats an inline type alias in the completion note", async () => {
    const text = "Types `List<Customer>` and ``arr[`key`](x)``. Unpaired ``tick`.";
    const source = "variables.customer.";
    const { container, unmount } = renderEditor({
      document: codeDocument({ value: source }),
      languageAdapter: javaScriptLanguageAdapter,
      completionProvider: () => [{ label: "street", documentation: { markdown: text + " <img src=x>" } }]
    });

    try {
      await waitFor(() => !!container.querySelector(".cm-editor"));
      const view = EditorView.findFromDOM(container.querySelector<HTMLElement>(".cm-editor")!)!;
      view.dispatch({ selection: { anchor: source.length } });
      startCompletion(view);

      await vi.waitFor(() => {
        const note = container.querySelector(".cm-completionInfo");
        expect(Array.from(note?.querySelectorAll("code") ?? [], code => code.textContent)).toEqual(["List<Customer>", "arr[`key`](x)"]);
        expect(note?.textContent).toBe("Types List<Customer> and arr[`key`](x). Unpaired ``tick`.");
        expect(note?.querySelector("img")).toBeNull();
      });
    } finally {
      unmount();
    }
  }, 20000);

  it("applies a typed variable's nested member mid-path and replaces only the typed prefix", async () => {
    const note = "From the declared type `shop.customer`; the stored value may differ.";
    const getCompletions = vi.fn().mockResolvedValue({
      state: "ready",
      data: { items: [{ label: "street", detail: "String", kind: "property", insertText: "street", documentation: note }] }
    });
    const source = "variables.customer.home.st + 1";
    const projection = createStudioCodeToolingProjection({
      document: { source, sourceVersion: 1 },
      authoringContext: { rootSymbols: [] },
      tooling: { getCompletions }
    });
    const onChange = vi.fn();
    const { container, unmount } = renderEditor({
      document: codeDocument({ value: source }),
      languageAdapter: javaScriptLanguageAdapter,
      completionProvider: projection.completionProvider,
      onChange
    });

    await waitFor(() => !!container.querySelector<HTMLElement>(".cm-editor"));
    const view = EditorView.findFromDOM(container.querySelector<HTMLElement>(".cm-editor")!)!;
    const cursor = "variables.customer.home.st".length;
    view.dispatch({ selection: { anchor: cursor } });
    startCompletion(view);
    await waitFor(() => completionStatus(view.state) === "active");
    await new Promise(resolve => setTimeout(resolve, 100)); // CodeMirror ignores an accept within its 75 ms interaction delay.
    expect(acceptCompletion(view)).toBe(true);

    expect(view.state.doc.toString()).toBe("variables.customer.home.street + 1");
    expect(onChange.mock.lastCall?.[0]).toMatchObject({ value: "variables.customer.home.street + 1" });
    expect(getCompletions).toHaveBeenCalledWith(expect.anything(), expect.anything(), { line: 0, column: cursor }, expect.anything());
    unmount();
  }, 20000);

  it.each(["debouncing", "pending", "active"] as const)("refreshes %s keyboard completion after the provider changes", async status => {
    const source = "variables.customer.";
    const props = { document: codeDocument({ value: source }), languageAdapter: javaScriptLanguageAdapter };
    const originalProvider = vi.fn(({ signal }: StudioCodeCompletionRequest) => status === "pending"
      ? new Promise<never>((_, reject) => signal.addEventListener("abort", () => reject(signal.reason), { once: true }))
      : [{ label: "oldMember" }]);
    const refreshedProvider = vi.fn(() => [{ label: "newMember" }]);
    const { container, rerender, unmount } = renderEditor({ ...props, completionProvider: originalProvider });

    try {
      await waitFor(() => !!container.querySelector(".cm-content"));
      const content = container.querySelector<HTMLElement>(".cm-content")!;
      const view = EditorView.findFromDOM(content)!;
      view.dispatch({ selection: { anchor: source.length } });
      key(content, " ", { code: "Space", ctrlKey: true });
      if (status === "debouncing") {
        expect(completionStatus(view.state)).toBe("pending");
        expect(originalProvider).not.toHaveBeenCalled();
      } else {
        await vi.waitFor(() => {
          expect(originalProvider).toHaveBeenCalledOnce();
          expect(completionStatus(view.state)).toBe(status);
        });
      }

      rerender({ ...props, completionProvider: refreshedProvider });

      await vi.waitFor(() => expect(currentCompletions(view.state).map(item => item.label)).toEqual(["newMember"]));
      expect(refreshedProvider).toHaveBeenCalledOnce();
      expect(refreshedProvider).toHaveBeenCalledWith(expect.objectContaining({ explicit: true, position: source.length }));
      if (status === "pending") expect(originalProvider.mock.calls[0]![0].signal.aborted).toBe(true);
    } finally {
      unmount();
    }
  }, 20000);

  it("keeps typing-triggered completion separate from keyboard requests on provider refresh", async () => {
    const props = { document: codeDocument({ value: "" }), languageAdapter: javaScriptLanguageAdapter };
    const originalProvider = vi.fn(() => null);
    const refreshedProvider = (request: StudioCodeCompletionRequest) => request.explicit ? [{ label: "shortcutOnly" }] : null;
    const { container, rerender, unmount } = renderEditor({ ...props, completionProvider: originalProvider });

    try {
      await waitFor(() => !!container.querySelector(".cm-content"));
      const view = EditorView.findFromDOM(container.querySelector<HTMLElement>(".cm-content")!)!;
      view.dispatch({ changes: { from: 0, insert: "s" }, selection: { anchor: 1 }, userEvent: "input.type" });
      expect(completionStatus(view.state)).toBe("pending");

      rerender({ ...props, document: { ...props.document, value: "s" }, completionProvider: refreshedProvider });

      expect(completionStatus(view.state)).toBeNull();
    } finally {
      unmount();
    }
  }, 20000);

  it("clears pending completion when a provider refresh follows blur", async () => {
    const source = "variables.customer.";
    const props = { document: codeDocument({ value: source }), languageAdapter: javaScriptLanguageAdapter };
    const originalProvider = vi.fn(() => null);
    const refreshedProvider = vi.fn(() => [{ label: "newMember" }]);
    const { container, rerender, unmount } = renderEditor({ ...props, completionProvider: originalProvider });
    const nextControl = document.createElement("button");
    document.body.append(nextControl);

    try {
      await waitFor(() => !!container.querySelector(".cm-content"));
      const content = container.querySelector<HTMLElement>(".cm-content")!;
      const view = EditorView.findFromDOM(content)!;
      view.dispatch({ selection: { anchor: source.length } });
      content.focus();
      key(content, " ", { code: "Space", ctrlKey: true });
      nextControl.focus();
      expect(document.activeElement).toBe(nextControl);
      expect(completionStatus(view.state)).toBe("pending");

      rerender({ ...props, completionProvider: refreshedProvider });

      expect(completionStatus(view.state)).toBeNull();
    } finally {
      nextControl.remove();
      unmount();
    }
  }, 20000);

  it("cancels in-flight completion when authorization is revoked", async () => {
    let requestSignal: AbortSignal | undefined;
    const completionProvider = vi.fn((request: { signal: AbortSignal }) => {
      requestSignal = request.signal;
      return new Promise<never>(() => {});
    });
    const { container, unmount } = renderEditor({
      document: codeDocument({ value: "sensitiveCompletionTarget" }),
      languageAdapter: javaScriptLanguageAdapter,
      completionProvider
    });

    await waitFor(() => !!container.querySelector<HTMLElement>(".cm-content"));
    container.querySelector<HTMLElement>(".cm-content")!.dispatchEvent(new KeyboardEvent("keydown", {
      key: " ",
      code: "Space",
      ctrlKey: true,
      bubbles: true,
      cancelable: true
    }));
    await waitFor(() => !!requestSignal);

    window.dispatchEvent(new Event("elsa:auth-session-ended"));

    expect(requestSignal?.aborted).toBe(true);
    expect(container.querySelector(".cm-editor")).toBeNull();
    unmount();
  }, 20000);

  it("cancels in-flight signature help on Escape", async () => {
    let resolveSignature: ((value: { label: string }) => void) | undefined;
    const signatureProvider = vi.fn((_document, _position, signal: AbortSignal) =>
      new Promise<{ label: string }>((resolve, reject) => {
        resolveSignature = resolve;
        signal.addEventListener("abort", () => reject(signal.reason), { once: true });
      }));
    const { container, unmount } = renderEditor({
      document: codeDocument({ value: "formatTotal(" }),
      languageAdapter: javaScriptLanguageAdapter,
      signatureProvider
    });

    await waitFor(() => !!container.querySelector<HTMLElement>(".cm-content"));
    await waitFor(() => signatureProvider.mock.calls.length > 0);
    const content = container.querySelector<HTMLElement>(".cm-content")!;

    expect(key(content, "Escape").defaultPrevented).toBe(true);
    resolveSignature?.({ label: "Must stay hidden." });
    await Promise.resolve();
    expect(container.querySelector(".studio-code-editor-signature")).toBeNull();
    expect(key(content, "Tab").defaultPrevented).toBe(false);
    unmount();
  }, 20000);

  it("keeps line numbers out of compact CodeMirror fields", async () => {
    const { container, unmount } = renderEditor({
      document: codeDocument(),
      languageAdapter: javaScriptLanguageAdapter,
      profile: "compact"
    });

    click(container.querySelector<HTMLButtonElement>(".studio-code-editor-preview")!);
    await waitFor(() => !!container.querySelector(".studio-code-editor-rich"));

    expect(container.querySelector(".cm-gutters")).toBeNull();
    unmount();
  }, 20000);

  it("opens an existing multiline compact document in the expanded surface", () => {
    const onExpand = vi.fn();
    const { container, unmount } = renderEditor({
      document: codeDocument({ value: "first line\nsecond line" }),
      languageAdapter: javaScriptLanguageAdapter,
      profile: "compact",
      onExpand
    });

    const preview = container.querySelector<HTMLButtonElement>(".studio-code-editor-preview")!;
    preview.focus();
    expect(onExpand).not.toHaveBeenCalled();

    click(preview);

    expect(onExpand).toHaveBeenCalledOnce();
    expect(container.querySelector(".studio-code-editor-rich")).toBeNull();
    unmount();
  });

  it.each(["compact", "expanded"] as const)("indents rich %s CodeMirror fields and lets Escape then Tab leave", async profile => {
    const { container, unmount } = renderEditor({
      document: codeDocument({ value: profile === "compact" ? "return total;" : "if (total) {\nreturn total;\n}" }),
      languageAdapter: javaScriptLanguageAdapter,
      profile
    });

    if (profile === "compact") click(container.querySelector<HTMLButtonElement>(".studio-code-editor-preview")!);
    await waitFor(() => !!container.querySelector<HTMLElement>(".cm-content"));

    const content = container.querySelector<HTMLElement>(".cm-content")!;
    const tab = key(content, "Tab");
    const escape = key(content, "Escape");
    const escapeTab = key(content, "Tab");

    expect(tab.defaultPrevented).toBe(true);
    expect(escape.defaultPrevented).toBe(true);
    expect(escapeTab.defaultPrevented).toBe(false);
    unmount();
  }, 20000);

  it("inserts a newline in an expanded rich editor", async () => {
    const onChange = vi.fn();
    const onExpand = vi.fn();
    const { container, unmount } = renderEditor({
      document: codeDocument({ value: "hello" }),
      languageAdapter: javaScriptLanguageAdapter,
      profile: "expanded",
      onChange,
      onExpand
    });
    try {
      await waitFor(() => !!container.querySelector<HTMLElement>(".cm-content"));
      const content = container.querySelector<HTMLElement>(".cm-content")!;
      const enter = key(content, "Enter");
      expect(enter.defaultPrevented).toBe(true);
      expect(onChange).toHaveBeenCalledWith(expect.objectContaining({ value: "\nhello" }));
      expect(onExpand).not.toHaveBeenCalled();
    } finally {
      unmount();
    }
  });

  it("uses the documented Control M shortcut to let Tab leave an expanded editor", async () => {
    const { container, unmount } = renderEditor({
      document: codeDocument(),
      languageAdapter: javaScriptLanguageAdapter,
      profile: "expanded"
    });

    await waitFor(() => !!container.querySelector<HTMLElement>(".cm-content"));

    const content = container.querySelector<HTMLElement>(".cm-content")!;
    const toggle = key(content, "m", { ctrlKey: true });
    const tab = key(content, "Tab");

    expect(toggle.defaultPrevented).toBe(true);
    expect(tab.defaultPrevented).toBe(false);
    unmount();
  }, 20000);

  it("reconfigures read-only state on an existing rich editor session", async () => {
    const props = {
      document: codeDocument(),
      languageAdapter: javaScriptLanguageAdapter,
      profile: "expanded" as const
    };
    const { container, rerender, unmount } = renderEditor(props);

    await waitFor(() => !!container.querySelector<HTMLElement>(".cm-content"));
    expect(container.querySelector<HTMLElement>(".cm-content")?.getAttribute("contenteditable")).toBe("true");

    rerender({ ...props, readOnly: true });
    expect(container.querySelector<HTMLElement>(".cm-content")?.getAttribute("contenteditable")).toBe("false");

    rerender({ ...props, readOnly: false });
    expect(container.querySelector<HTMLElement>(".cm-content")?.getAttribute("contenteditable")).toBe("true");
    unmount();
  }, 20000);

  it("keeps compact language rendering stable across repeated activations", async () => {
    const { container, unmount } = renderEditor({
      document: codeDocument({ value: "const total = 1;" }),
      languageAdapter: javaScriptLanguageAdapter,
      profile: "compact"
    });

    click(container.querySelector<HTMLButtonElement>(".studio-code-editor-preview")!);
    await waitFor(() => !!container.querySelector<HTMLElement>(".cm-content"));
    await waitFor(() => container.querySelectorAll(".cm-content span").length > 0);
    const initialTokenCount = container.querySelectorAll(".cm-content span").length;
    container.querySelector<HTMLElement>(".cm-content")!.dispatchEvent(new FocusEvent("blur", { bubbles: true }));
    await waitFor(() => !!container.querySelector(".studio-code-editor-preview"));

    click(container.querySelector<HTMLButtonElement>(".studio-code-editor-preview")!);
    await waitFor(() => !!container.querySelector<HTMLElement>(".cm-content"));

    expect(container.querySelectorAll(".cm-content span")).toHaveLength(initialTokenCount);
    unmount();
  }, 20000);

  it("hands a compact view to the next field without retaining source, session, or focus", async () => {
    const host = document.createElement("div");
    document.body.appendChild(host);
    const root = createRoot(host);
    const first = codeDocument({ uri: "elsa://expressions/first.js", value: "firstValue" });
    const second = codeDocument({ uri: "elsa://expressions/second.js", value: "secondValue" });
    flushSync(() => root.render(<>
      <StudioCodeEditor ariaLabel="First expression" document={first} languageAdapter={javaScriptLanguageAdapter} profile="compact" sessionKey="first" onChange={vi.fn()} />
      <StudioCodeEditor ariaLabel="Second expression" document={second} languageAdapter={javaScriptLanguageAdapter} profile="compact" sessionKey="second" onChange={vi.fn()} />
    </>));

    click(host.querySelector<HTMLButtonElement>("[aria-label='First expression. Activate to edit.']")!);
    await waitFor(() => !!host.querySelector(".studio-code-editor-rich-compact .cm-content"));
    click(host.querySelector<HTMLButtonElement>("[aria-label='Second expression. Activate to edit.']")!);
    await waitFor(() => host.querySelectorAll(".studio-code-editor-rich-compact .cm-content").length === 1 &&
      host.querySelector<HTMLElement>(".studio-code-editor-rich-compact .cm-content")?.textContent === "secondValue");

    const content = host.querySelector<HTMLElement>(".studio-code-editor-rich-compact .cm-content")!;
    expect(host.querySelectorAll(".studio-code-editor-rich-compact")).toHaveLength(1);
    expect(document.activeElement).toBe(content);
    expect(host.querySelector("[aria-label='First expression. Activate to edit.']")?.textContent).toContain("firstValue");
    root.unmount();
    host.remove();
  }, 20000);
});

function renderEditor(props: Partial<StudioCodeEditorProps> = {}) {
  const host = document.createElement("div");
  document.body.appendChild(host);
  const root = createRoot(host);
  const defaultProps: StudioCodeEditorProps = {
    document: codeDocument(),
    ariaLabel: "Global JavaScript function",
    onChange: vi.fn()
  };

  const render = (nextProps: Partial<StudioCodeEditorProps>) => {
    flushSync(() => root.render(<StudioCodeEditor {...defaultProps} {...nextProps} />));
  };
  render(props);

  return {
    container: host,
    rerender: render,
    unmount: () => {
      root.unmount();
      host.remove();
    }
  };
}

function editorInput(container: HTMLElement) {
  return container.querySelector<HTMLTextAreaElement>(".studio-code-editor-input")!;
}

function fill(element: HTMLTextAreaElement, value: string) {
  flushSync(() => {
    const valueSetter = Object.getOwnPropertyDescriptor(HTMLTextAreaElement.prototype, "value")?.set;
    valueSetter?.call(element, value);
    element.dispatchEvent(new Event("input", { bubbles: true }));
  });
}

function click(element: HTMLElement) {
  flushSync(() => element.dispatchEvent(new MouseEvent("click", { bubbles: true })));
}

function key(element: HTMLElement, value: string, init: KeyboardEventInit = {}) {
  const keyCode = value === "Escape" ? 27 : value === "Tab" ? 9 : 0;
  const event = new KeyboardEvent("keydown", { key: value, keyCode, bubbles: true, cancelable: true, ...init });
  element.dispatchEvent(event);
  return event;
}

function codeDocument(overrides: Partial<StudioCodeDocument> = {}): StudioCodeDocument {
  return {
    uri: "elsa://functions/global.js",
    language: "javascript",
    value: "return total;",
    ...overrides
  };
}

async function waitFor(predicate: () => boolean) {
  for (let i = 0; i < 200; i++) {
    if (predicate()) return;
    await new Promise(resolve => setTimeout(resolve, 5));
    flushSync(() => {});
  }
  throw new Error("Timed out waiting for predicate.");
}
