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

  it("lazy-loads the rich editor for JavaScript documents", async () => {
    const { container, unmount } = renderEditor({
      document: codeDocument({ language: "javascript", value: "return total;" }),
      languageAdapter: javaScriptLanguageAdapter,
      theme: "dark"
    });

    expect(editorInput(container).value).toBe("return total;");
    await waitFor(() => !!container.querySelector(".studio-code-editor-rich"));

    expect(container.querySelector(".studio-code-editor-rich")).toBeTruthy();
    expect(container.querySelector("[aria-label='Global JavaScript function']")).toBeTruthy();
    expect(container.querySelector(".studio-code-editor-header")?.textContent).toContain("JavaScript");
    expect(container.querySelector(".studio-code-editor")?.getAttribute("data-theme")).toBe("dark");
    unmount();
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

  flushSync(() => root.render(<StudioCodeEditor {...defaultProps} {...props} />));

  return {
    container: host,
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
