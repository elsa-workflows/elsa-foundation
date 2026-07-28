import { flushSync } from "react-dom";
import { createRoot } from "react-dom/client";
import { describe, expect, it, vi } from "vitest";
import { StudioCodeEditor } from "../StudioCodeEditor";
import { javaScriptLanguageAdapter } from "../languages/javascript";
import type { StudioCodeDocument, StudioCodeEditorProps } from "../types";

describe("StudioCodeEditor", () => {
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

  it("uses Enter to expand, Tab to indent, and Escape then Tab to leave a compact fallback editor", () => {
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

    expect(onExpand).toHaveBeenCalledOnce();
    expect(enter.defaultPrevented).toBe(true);
    expect(tab.defaultPrevented).toBe(true);
    expect(escapeTab.defaultPrevented).toBe(false);
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

  it.each(["compact", "expanded"] as const)("indents rich %s CodeMirror fields and lets Escape then Tab leave", async profile => {
    const { container, unmount } = renderEditor({
      document: codeDocument({ value: "if (total) {\nreturn total;\n}" }),
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
