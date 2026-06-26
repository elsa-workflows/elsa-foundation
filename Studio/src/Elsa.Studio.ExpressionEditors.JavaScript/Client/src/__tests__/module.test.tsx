import React from "react";
import { describe, expect, it, vi } from "vitest";
import { JavaScriptExpandedEditor, JavaScriptInlineEditor, register } from "../module";
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
  });

  it("renders inline and expanded editors through the public contribution contract", async () => {
    const onInlineChange = vi.fn();
    const onExpandedChange = vi.fn();
    const inline = JavaScriptInlineEditor({
      descriptor: descriptor(),
      syntax: "JavaScript",
      value: "return 1;",
      context: context("JavaScript"),
      onChange: onInlineChange
    }) as React.ReactElement<EditorElementProps>;
    const expanded = JavaScriptExpandedEditor({
      descriptor: descriptor(),
      syntax: "JavaScript",
      value: "return 2;",
      context: context("JavaScript"),
      onChange: onExpandedChange
    }) as React.ReactElement<{ children: React.ReactNode }>;
    const expandedTextarea = React.Children
      .toArray(expanded.props.children)
      .find(child => React.isValidElement<CodeMirrorElementProps>(child) && child.props["aria-label"] === "JavaScript expanded expression") as React.ReactElement<CodeMirrorElementProps>;

    expect(inline.props["aria-label"]).toBe("JavaScript expression");
    expect(inline.props.value).toBe("return 1;");
    expect(expandedTextarea.props.value).toBe("return 2;");

    inline.props.onChange(changeEvent("return 3;"));
    expandedTextarea.props.onChange("return 4;");

    expect(onInlineChange).toHaveBeenCalledWith("return 3;");
    expect(onExpandedChange).toHaveBeenCalledWith("return 4;");
  });
});

interface EditorElementProps {
  "aria-label": string;
  value: string;
  onChange(event: React.ChangeEvent<HTMLTextAreaElement>): void;
}

interface CodeMirrorElementProps {
  "aria-label": string;
  value: string;
  onChange(value: string): void;
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

function changeEvent(value: string) {
  return {
    target: { value }
  } as React.ChangeEvent<HTMLTextAreaElement>;
}
