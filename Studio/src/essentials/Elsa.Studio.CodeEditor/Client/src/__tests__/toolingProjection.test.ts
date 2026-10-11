import { describe, expect, it, vi } from "vitest";
import { createStudioCodeToolingProjection, projectStudioCodeDiagnostics } from "../toolingProjection";

describe("createStudioCodeToolingProjection", () => {
  it("merges authoring-context symbols with an asynchronous catalog without SDK imports", async () => {
    const toolingDocument = { source: "for" };
    const authoringContext = {
      visibleVariables: [{ id: "total", name: "total", kind: "variable", documentation: "The order total." }]
    };
    const getCatalog = vi.fn().mockResolvedValue({
      state: "ready",
      data: {
        symbols: [
          { id: "total", name: "total", kind: "variable" },
          { id: "format", name: "formatTotal", kind: "function", documentation: "<b>Formats</b> a total.", signatures: [{ label: "formatTotal(value)" }] }
        ]
      }
    });
    const projection = createStudioCodeToolingProjection({
      document: toolingDocument,
      authoringContext,
      tooling: { getCatalog }
    });
    const controller = new AbortController();

    const completions = await projection.completionProvider({
      document: { uri: "elsa://drafts/a/total", language: "javascript", value: "for", version: 1 },
      position: 3,
      explicit: true,
      signal: controller.signal
    });

    expect(getCatalog).toHaveBeenCalledWith(toolingDocument, authoringContext, "for", undefined, controller.signal);
    expect(completions).toEqual(expect.arrayContaining([
      expect.objectContaining({ label: "total", kind: "variable" }),
      expect.objectContaining({ label: "formatTotal", detail: "formatTotal(value)" })
    ]));
  });

  it("projects hover, signature, and semantic ranges for the active editor URI", async () => {
    const projection = createStudioCodeToolingProjection({
      authoringContext: {
        workflowInputs: [{
          name: "customer",
          documentation: "<script>bad()</script>Customer data.",
          signatures: [{ label: "customer()", documentation: "Returns the customer." }]
        }]
      }
    });
    const controller = new AbortController();
    const document = { uri: "elsa://drafts/a/customer", language: "liquid", value: "customer(", version: 1 };

    await expect(projection.hoverProvider(document, 3, controller.signal)).resolves.toEqual({
      range: { from: 0, to: 8 },
      documentation: { markdown: "<script>bad()</script>Customer data." }
    });
    await expect(projection.signatureProvider(document, document.value.length, controller.signal)).resolves.toEqual({
      label: "customer()",
      documentation: { markdown: "Returns the customer." }
    });
    expect(projectStudioCodeDiagnostics(document.uri, [{
      severity: "error",
      code: "ELSA001",
      message: "Unknown input.",
      range: { start: { line: 2, column: 3 }, end: { line: 2, column: 11 } }
    }])).toEqual([{
      uri: document.uri,
      severity: "error",
      code: "ELSA001",
      message: "Unknown input.",
      startLineNumber: 2,
      startColumn: 3,
      endLineNumber: 2,
      endColumn: 11
    }]);
  });

  it("retains local context when the catalog is unavailable", async () => {
    const authoringContext = { workflowInputs: [{ name: "invoice", kind: "value" }] };
    const projection = createStudioCodeToolingProjection({
      document: { source: "inv" },
      authoringContext,
      tooling: { getCatalog: vi.fn().mockResolvedValue({ state: "unavailable" }) }
    });

    const completions = await projection.completionProvider({
      document: { uri: "elsa://drafts/a/invoice", language: "liquid", value: "inv", version: 1 },
      position: 3,
      explicit: false,
      signal: new AbortController().signal
    });

    expect(completions).toEqual([expect.objectContaining({ label: "invoice" })]);
  });

  it("traverses language-projected children and nested value shapes without knowing a language", async () => {
    const authoringContext = {
      workflowInputs: [{ id: "input:customer", name: "customer", kind: "value", shapeId: "shape:customer" }],
      visibleVariables: [{ id: "variable:total", name: "total", kind: "variable" }],
      visibleActivityOutputs: []
    };
    const getValueShape = vi.fn().mockResolvedValue({
      state: "ready",
      data: {
        id: "shape:customer",
        members: [{ name: "email", documentation: "The customer email.", shapeId: "shape:string" }]
      }
    });
    const projection = createStudioCodeToolingProjection({
      document: { source: "" },
      authoringContext,
      tooling: { getValueShape },
      languageProjection: {
        projectContext: context => [{
          id: "language:container",
          name: "container",
          kind: "variable",
          children: context?.workflowInputs ?? []
        }]
      }
    });
    const signal = new AbortController().signal;
    const complete = (value: string) => projection.completionProvider({
      document: { uri: "elsa://drafts/a/customer", language: "javascript", value, version: 1 },
      position: value.length,
      explicit: true,
      signal
    });

    const roots = await complete("");
    expect(roots).toEqual([expect.objectContaining({ label: "container" })]);
    await expect(complete("container.")).resolves.toEqual([
      expect.objectContaining({ label: "customer" })
    ]);
    await expect(complete("container.customer.")).resolves.toEqual([
      expect.objectContaining({ label: "email", kind: "property" })
    ]);
    expect(getValueShape).toHaveBeenCalledWith(
      { source: "" },
      authoringContext,
      "shape:customer",
      signal
    );
  });

  it("projects Liquid members directly from the permission-filtered context", async () => {
    const authoringContext = {
      workflowInputs: [{ id: "input:customer", name: "customer", kind: "value", shapeId: "shape:customer" }]
    };
    const projection = createStudioCodeToolingProjection({
      document: { source: "" },
      authoringContext,
      tooling: {
        getValueShape: vi.fn().mockResolvedValue({
          state: "ready",
          data: {
            id: "shape:customer",
            members: [{ name: "name", documentation: "Display name.", shapeId: "shape:string" }]
          }
        })
      }
    });
    const value = "customer.";

    await expect(projection.completionProvider({
      document: { uri: "elsa://drafts/a/customer", language: "liquid", value, version: 1 },
      position: value.length,
      explicit: true,
      signal: new AbortController().signal
    })).resolves.toEqual([
      expect.objectContaining({ label: "name", kind: "property" })
    ]);
  });

  it("does not invoke or synthesize tooling that the provider declares unsupported", async () => {
    const getCompletions = vi.fn();
    const getHover = vi.fn();
    const projection = createStudioCodeToolingProjection({
      document: { source: "customer", sourceVersion: 1 },
      authoringContext: {
        capabilities: { completion: false, hover: false, signatures: false },
        rootSymbols: [{ name: "customer", documentation: "Customer", signatures: [{ label: "customer()" }] }]
      },
      tooling: { getCompletions, getHover }
    });
    const document = { uri: "elsa://drafts/a/customer", language: "javascript", value: "customer", version: 1 };
    const signal = new AbortController().signal;

    await expect(projection.completionProvider({
      document,
      position: document.value.length,
      explicit: true,
      signal
    })).resolves.toEqual([]);
    await expect(projection.hoverProvider(document, 3, signal)).resolves.toBeNull();
    await expect(projection.signatureProvider(document, 3, signal)).resolves.toBeNull();
    expect(getCompletions).not.toHaveBeenCalled();
    expect(getHover).not.toHaveBeenCalled();
  });

  it("prefers source-aware language-provider completion and hover operations", async () => {
    const getCompletions = vi.fn().mockResolvedValue({
      state: "ready",
      data: { items: [{ label: "getCustomer", insertText: "getCustomer()", kind: "function" }] }
    });
    const getHover = vi.fn().mockResolvedValue({
      state: "ready",
      data: { contents: "Returns the current customer." }
    });
    const expressionDocument = { source: "line one\ngetCu", sourceVersion: 4 };
    const authoringContext = { rootSymbols: [] };
    const projection = createStudioCodeToolingProjection({
      document: expressionDocument,
      authoringContext,
      tooling: { getCompletions, getHover }
    });
    const editorDocument = {
      uri: "elsa://drafts/a/customer",
      language: "javascript",
      value: "line one\ngetCus",
      version: 2
    };
    const signal = new AbortController().signal;

    await expect(projection.completionProvider({
      document: editorDocument,
      position: editorDocument.value.length,
      explicit: true,
      signal
    })).resolves.toEqual([
      expect.objectContaining({ label: "getCustomer", apply: "getCustomer()", kind: "function" })
    ]);
    await expect(projection.hoverProvider(editorDocument, editorDocument.value.length, signal)).resolves.toEqual({
      documentation: { markdown: "Returns the current customer." }
    });
    await projection.completionProvider({
      document: { ...editorDocument, value: "line one\ngetCust" },
      position: "line one\ngetCust".length,
      explicit: true,
      signal
    });
    expect(getCompletions).toHaveBeenCalledWith(
      { ...expressionDocument, source: editorDocument.value, sourceVersion: 5 },
      authoringContext,
      { line: 1, column: 6 },
      signal
    );
    expect(getHover).toHaveBeenCalledWith(
      { ...expressionDocument, source: editorDocument.value, sourceVersion: 5 },
      authoringContext,
      { line: 1, column: 6 },
      signal
    );
    expect(getCompletions).toHaveBeenLastCalledWith(
      { ...expressionDocument, source: "line one\ngetCust", sourceVersion: 6 },
      authoringContext,
      { line: 1, column: 7 },
      signal
    );
  });
});
