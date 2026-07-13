import React from "react";
import { flushSync } from "react-dom";
import { createRoot } from "react-dom/client";
import { describe, expect, it, vi } from "vitest";
import { StudioCodeEditor } from "@elsa-workflows/studio-code-editor";
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
    const container = document.createElement("div");
    document.body.appendChild(container);
    const root = createRoot(container);
    flushSync(() => root.render(
      <JavaScriptInlineEditor
        descriptor={descriptor()}
        syntax="JavaScript"
        value="return 1;"
        initialFocus
        context={context("JavaScript")}
        onChange={onInlineChange}
      />
    ));
    const inline = container.querySelector<HTMLInputElement>("input[aria-label='JavaScript expression']")!;
    const expanded = JavaScriptExpandedEditor({
      descriptor: descriptor(),
      syntax: "JavaScript",
      value: "return 2;",
      context: context("JavaScript"),
      onChange: onExpandedChange
    }) as React.ReactElement<{ children: React.ReactNode }>;
    const expandedEditor = React.Children
      .toArray(expanded.props.children)
      .find(child => React.isValidElement<StudioCodeEditorElementProps>(child) && child.type === StudioCodeEditor) as React.ReactElement<StudioCodeEditorElementProps>;

    expect(inline.value).toBe("return 1;");
    expect(inline.type).toBe("text");
    expect(document.activeElement).toBe(inline);
    expect(expandedEditor.props.ariaLabel).toBe("JavaScript expanded expression");
    expect(expandedEditor.props.document).toEqual({
      uri: "elsa://expressions/javascript/Text",
      language: "javascript",
      value: "return 2;"
    });
    expect(expandedEditor.props.readOnly).toBeUndefined();
    expect(expandedEditor.props.theme).toBe("dark");

    flushSync(() => {
      Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, "value")?.set?.call(inline, "return 3;");
      inline.dispatchEvent(new Event("input", { bubbles: true }));
    });
    expandedEditor.props.onChange({ ...expandedEditor.props.document, value: "return 4;" });

    expect(onInlineChange).toHaveBeenCalledWith("return 3;");
    expect(onExpandedChange).toHaveBeenCalledWith("return 4;");
    flushSync(() => root.unmount());
    container.remove();
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
