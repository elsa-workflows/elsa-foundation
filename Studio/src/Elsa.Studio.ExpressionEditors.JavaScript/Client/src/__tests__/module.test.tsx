import React from "react";
import { describe, expect, it, vi } from "vitest";
import { createStudioCodeToolingProjection, StudioCodeEditor } from "@elsa-workflows/studio-code-editor";
import { JavaScriptExpandedEditor, JavaScriptInlineEditor, JavaScriptSourceRenderer, register } from "../module";
import { javaScriptToolingProjection } from "../javaScriptToolingProjection";
import type { ElsaStudioModuleApi, StudioContributionRegistry, StudioExpressionEditorContribution } from "@elsa-workflows/studio-sdk";

describe("JavaScript expression editor module", () => {
  it("registers inline and expanded JavaScript expression editor surfaces", () => {
    const api = testApi();

    register(api);

    const contribution = api.expressionEditors.list()[0];
    expect(contribution.id).toBe("elsa.javascript-expression-editor");
    expect(contribution.supports(context("JavaScript"))).toBe(true);
    expect(contribution.supports(context("Literal"))).toBe(false);
    expect(contribution.surfaces.inline).toBe(JavaScriptInlineEditor);
    expect(contribution.surfaces.expanded).toBe(JavaScriptExpandedEditor);
    expect(contribution.metadata?.toolingCapabilities).toMatchObject({
      highlighting: true,
      signatures: true,
      localDiagnostics: true
    });
    expect(contribution.sourceRenderer?.compact).toBe(JavaScriptSourceRenderer);
    expect(contribution.sourceRenderer?.expanded).toBe(JavaScriptSourceRenderer);
  });

  it("renders authorized authored JavaScript without evaluating it", () => {
    const rendered = JavaScriptSourceRenderer({
      context: {
        expressionType: "JavaScript",
        value: "variables.customer.email",
        metadata: {},
        isSensitive: false,
        surface: "compact"
      }
    }) as React.ReactElement;

    expect(rendered.type).toBe("code");
    expect(rendered.props.children).toBe("variables.customer.email");
  });

  it("renders inline and expanded editors through the public contribution contract", async () => {
    const onInlineChange = vi.fn();
    const onExpandedChange = vi.fn();
    const editorContext = {
      ...context("JavaScript"),
      editorSessionScope: "workflow-editor-1",
      document: {
        id: "document-1",
        uri: "elsa://workflow-expressions/draft/activity/text/JavaScript",
        draftId: "draft",
        activityId: "activity",
        propertyKey: "text",
        expressionType: "JavaScript",
        source: "return 1;",
        sourceVersion: 7
      }
    };
    const inline = findStudioCodeEditor(
      <JavaScriptInlineEditor
        descriptor={descriptor()}
        syntax="JavaScript"
        value="return 1;"
        initialFocus
        context={editorContext}
        onChange={onInlineChange}
      />
    );
    const expandedEditor = findStudioCodeEditor(
      <JavaScriptExpandedEditor
        descriptor={descriptor()}
        syntax="JavaScript"
        value="return 2;"
        context={{ ...editorContext, surface: "expanded" }}
        onChange={onExpandedChange}
      />
    );

    expect(inline.props.profile).toBe("compact");
    expect(inline.props.document.value).toBe("return 1;");
    expect(inline.props.document.version).toBe(7);
    expect(inline.props.sessionKey).toBe("workflow-editor-1\u001fdocument-1");
    expect(expandedEditor.props.ariaLabel).toBe("JavaScript expanded expression");
    expect(expandedEditor.props.document).toMatchObject({
      uri: "elsa://workflow-expressions/draft/activity/text/JavaScript",
      language: "javascript",
      value: "return 1;",
      version: 7
    });
    expect(expandedEditor.props.profile).toBe("expanded");
    expect(expandedEditor.props.sessionKey).toBe(inline.props.sessionKey);
    expect(expandedEditor.props.readOnly).toBeUndefined();
    expect(expandedEditor.props.theme).toBe("studio");

    inline.props.onChange({ ...inline.props.document, value: "return 3;" });
    expandedEditor.props.onChange({ ...expandedEditor.props.document, value: "return 4;" });

    expect(onInlineChange).toHaveBeenCalledWith("return 3;");
    expect(onExpandedChange).toHaveBeenCalledWith("return 4;");
  });

  it("owns JavaScript globals and resolves getter-call members through the neutral shared traversal", async () => {
    const authoringContext = {
      workflowInputs: [{ id: "input:customer", name: "customer", kind: "value" as const, shapeId: "shape:customer" }],
      visibleVariables: [
        { id: "variable:attempts", name: "attempts", kind: "value" as const, shapeId: "shape:number" },
        {
          id: "variable:eclair",
          name: "éclair",
          kind: "value" as const,
          shapeId: "shape:string",
          documentation: "Unicode variable."
        },
        { id: "variable:eszett", name: "ßeta", kind: "value" as const, shapeId: "shape:string" }
      ],
      visibleActivityOutputs: []
    };
    const getValueShape = vi.fn().mockResolvedValue({
      state: "ready",
      data: {
        id: "shape:customer",
        members: [{ name: "email", documentation: "Customer email.", shapeId: "shape:string" }]
      }
    });
    const projection = createStudioCodeToolingProjection({
      document: { source: "" },
      authoringContext,
      tooling: { getValueShape },
      languageProjection: javaScriptToolingProjection
    });
    const signal = new AbortController().signal;
    const complete = (value: string) => projection.completionProvider({
      document: { uri: "elsa://drafts/a/customer", language: "javascript", value, version: 1 },
      position: value.length,
      explicit: true,
      signal
    });

    await expect(complete("")).resolves.toEqual(expect.arrayContaining([
      expect.objectContaining({ label: "args" }),
      expect.objectContaining({ label: "getAttempts", kind: "function" }),
      expect.objectContaining({ label: "getÉclair", kind: "function" }),
      expect.objectContaining({ label: "getßeta", kind: "function" }),
      expect.objectContaining({ label: "variables" })
    ]));
    await expect(complete("")).resolves.not.toEqual(expect.arrayContaining([
      expect.objectContaining({ label: "getSSeta" })
    ]));
    await expect(complete("")).resolves.not.toEqual(expect.arrayContaining([
      expect.objectContaining({ label: "getCustomer" })
    ]));
    await expect(complete("args.customer.")).resolves.toEqual([
      expect.objectContaining({ label: "email", kind: "property" })
    ]);
    await expect(complete("variables.éclair.")).resolves.toEqual([
      expect.objectContaining({ label: "email", kind: "property" })
    ]);
    const unicodeVariable = "variables.éclair";
    await expect(projection.hoverProvider(
      { uri: "elsa://drafts/a/unicode", language: "javascript", value: unicodeVariable, version: 1 },
      unicodeVariable.length,
      signal
    )).resolves.toEqual(expect.objectContaining({
      documentation: { markdown: "Unicode variable." }
    }));
    expect(getValueShape).toHaveBeenCalledWith({ source: "" }, authoringContext, "shape:customer", signal);
  });
});

interface StudioCodeEditorElementProps {
  ariaLabel: string;
  document: {
    uri: string;
    language: string;
    value: string;
  };
  readOnly?: boolean;
  theme?: string;
  profile?: string;
  sessionKey?: string;
  onChange(document: { value: string }): void;
}

function findStudioCodeEditor(node: React.ReactNode): React.ReactElement<StudioCodeEditorElementProps> {
  if (!React.isValidElement(node)) throw new Error("StudioCodeEditor was not rendered.");
  if (node.type === StudioCodeEditor) return node as React.ReactElement<StudioCodeEditorElementProps>;
  if (typeof node.type === "function") {
    return findStudioCodeEditor((node.type as (props: unknown) => React.ReactNode)(node.props));
  }
  if (typeof node.type === "object" && node.type && "type" in node.type &&
      typeof node.type.type === "function") {
    return findStudioCodeEditor((node.type.type as (props: unknown) => React.ReactNode)(node.props));
  }
  for (const child of React.Children.toArray((node.props as { children?: React.ReactNode }).children)) {
    try {
      return findStudioCodeEditor(child);
    } catch {
      // Keep searching siblings.
    }
  }
  throw new Error("StudioCodeEditor was not rendered.");
}

function testApi(): ElsaStudioModuleApi {
  return {
    expressionEditors: registry<StudioExpressionEditorContribution>()
  } as ElsaStudioModuleApi;
}

function registry<T>(): StudioContributionRegistry<T> {
  const contributions: T[] = [];
  return {
    add: contribution => contributions.push(contribution),
    list: () => [...contributions]
  };
}

function context(syntax: string) {
  return {
    syntax,
    surface: "inline" as const,
    descriptor: descriptor(),
    activity: {},
    expressionDescriptors: []
  };
}

function descriptor() {
  return {
    name: "Text",
    displayName: "Text",
    typeName: "System.String"
  };
}
