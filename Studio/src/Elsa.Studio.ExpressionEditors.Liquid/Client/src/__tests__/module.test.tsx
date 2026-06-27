import React from "react";
import { describe, expect, it, vi } from "vitest";
import { StudioCodeEditor } from "@elsa-workflows/studio-code-editor";
import { LiquidExpandedEditor, register } from "../module";
import type { ElsaStudioModuleApi, StudioContributionRegistry, StudioExpressionEditorContribution } from "@elsa-workflows/studio-sdk";

describe("Liquid expression editor module", () => {
  it("registers the expanded Liquid expression editor surface", () => {
    const api = testApi();

    register(api);

    const contribution = api.expressionEditors.list()[0];
    expect(contribution.id).toBe("elsa.liquid-expression-editor");
    expect(contribution.supports(context("Liquid"))).toBe(true);
    expect(contribution.supports(context("JavaScript"))).toBe(false);
    expect(contribution.surfaces.inline).toBeUndefined();
    expect(contribution.surfaces.expanded).toBe(LiquidExpandedEditor);
  });

  it("leaves unavailable inline surfaces to host fallback resolution", () => {
    const api = testApi();
    register(api);

    const inlineEditor = api.expressionEditors.list()
      .find(editor => editor.surfaces.inline && editor.supports(context("Liquid", "inline")));

    expect(inlineEditor).toBeUndefined();
  });

  it("renders expanded editor through the public contribution contract", () => {
    const onExpandedChange = vi.fn();
    const expanded = LiquidExpandedEditor({
      descriptor: descriptor(),
      syntax: "Liquid",
      value: "{% assign total = order.total %}",
      context: context("Liquid", "expanded"),
      onChange: onExpandedChange
    }) as React.ReactElement<{ children: React.ReactNode }>;
    const expandedEditor = React.Children
      .toArray(expanded.props.children)
      .find(child => React.isValidElement<StudioCodeEditorElementProps>(child) && child.type === StudioCodeEditor) as React.ReactElement<StudioCodeEditorElementProps>;

    expect(expandedEditor.props.ariaLabel).toBe("Liquid expanded expression");
    expect(expandedEditor.props.document).toEqual({
      uri: "elsa://expressions/liquid/Text",
      language: "liquid",
      value: "{% assign total = order.total %}"
    });
    expect(expandedEditor.props.languageAdapter.displayName).toBe("Liquid");
    expect(expandedEditor.props.readOnly).toBeUndefined();
    expect(expandedEditor.props.theme).toBe("dark");

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
  onChange(document: { value: string }): void;
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
