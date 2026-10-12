import React from "react";
import { describe, expect, it, vi } from "vitest";
import { StudioCodeEditor } from "@elsa-workflows/studio-code-editor";
import { LiquidExpandedEditor, LiquidInlineEditor, LiquidSourceRenderer, register } from "../module";
import type { ElsaStudioModuleApi, StudioContributionRegistry, StudioExpressionEditorContribution } from "@elsa-workflows/studio-sdk";

describe("Liquid expression editor module", () => {
  it("registers compact and expanded Liquid expression editor surfaces", () => {
    const api = testApi();

    register(api);

    const contribution = api.expressionEditors.list()[0];
    expect(contribution.id).toBe("elsa.liquid-expression-editor");
    expect(contribution.supports(context("Liquid"))).toBe(true);
    expect(contribution.supports(context("JavaScript"))).toBe(false);
    expect(contribution.surfaces.inline).toBe(LiquidInlineEditor);
    expect(contribution.surfaces.expanded).toBe(LiquidExpandedEditor);
    expect(contribution.metadata?.toolingCapabilities).toMatchObject({
      highlighting: true,
      signatures: true,
      localDiagnostics: true
    });
    expect(contribution.sourceRenderer?.compact).toBe(LiquidSourceRenderer);
    expect(contribution.sourceRenderer?.expanded).toBe(LiquidSourceRenderer);
  });

  it("renders authorized authored Liquid without evaluating it", () => {
    const rendered = LiquidSourceRenderer({
      context: {
        expressionType: "Liquid",
        value: "{{ customer.email }}",
        metadata: {},
        isSensitive: false,
        surface: "expanded"
      }
    }) as React.ReactElement;

    expect(rendered.type).toBe("code");
    expect(rendered.props.children).toBe("{{ customer.email }}");
  });

  it("provides the rich compact editor for inline Liquid fields", () => {
    const api = testApi();
    register(api);

    const inlineEditor = api.expressionEditors.list()
      .find(editor => editor.surfaces.inline && editor.supports(context("Liquid", "inline")));

    expect(inlineEditor?.surfaces.inline).toBe(LiquidInlineEditor);
  });

  it("uses the same stable document and session for compact and expanded editors", () => {
    const editorContext = {
      ...context("Liquid"),
      document: {
        id: "liquid-document",
        uri: "elsa://workflow-expressions/draft/activity/text/Liquid",
        draftId: "draft",
        activityId: "activity",
        propertyKey: "text",
        expressionType: "Liquid",
        source: "{% assign total = order.total %}",
        sourceVersion: 4
      }
    };
    const inline = findStudioCodeEditor(
      <LiquidInlineEditor
        descriptor={descriptor()}
        syntax="Liquid"
        value="ignored because the document is authoritative"
        context={editorContext}
        onChange={vi.fn()}
      />
    );
    const onExpandedChange = vi.fn();
    const expandedEditor = findStudioCodeEditor(
      <LiquidExpandedEditor
        descriptor={descriptor()}
        syntax="Liquid"
        value="{% assign total = order.total %}"
        context={{ ...editorContext, surface: "expanded" }}
        onChange={onExpandedChange}
      />
    );

    expect(inline.props.profile).toBe("compact");
    expect(expandedEditor.props.ariaLabel).toBe("Liquid expanded expression");
    expect(expandedEditor.props.document).toEqual({
      uri: "elsa://workflow-expressions/draft/activity/text/Liquid",
      language: "liquid",
      value: "{% assign total = order.total %}",
      version: 4
    });
    expect(expandedEditor.props.profile).toBe("expanded");
    expect(expandedEditor.props.sessionKey).toBe(inline.props.sessionKey);
    expect(expandedEditor.props.languageAdapter.displayName).toBe("Liquid");
    expect(expandedEditor.props.readOnly).toBeUndefined();
    expect(expandedEditor.props.theme).toBe("studio");

    expandedEditor.props.onChange({ ...expandedEditor.props.document, value: "{{ order.discount }}" });

    expect(onExpandedChange).toHaveBeenCalledWith("{{ order.discount }}");
  });
});

interface StudioCodeEditorElementProps {
  ariaLabel: string;
  document: {
    uri: string;
    language: string;
    value: string;
  };
  languageAdapter: {
    language: string;
    displayName: string;
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

function context(syntax: string, surface: "inline" | "expanded" = "inline") {
  return {
    syntax,
    surface,
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
