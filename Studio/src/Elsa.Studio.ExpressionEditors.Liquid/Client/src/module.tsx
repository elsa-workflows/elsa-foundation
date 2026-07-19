import React from "react";
import { StudioCodeEditor, type StudioCodeLanguageAdapter } from "@elsa-workflows/studio-code-editor";
import type { ElsaStudioModuleApi, StudioExpressionEditorProps, StudioExpressionSourceRendererProps } from "@elsa-workflows/studio-sdk";
import "./styles.css";

const liquidSyntax = "Liquid";

const liquidLanguageAdapter: StudioCodeLanguageAdapter = {
  language: "liquid",
  displayName: "Liquid"
};

export function register(api: ElsaStudioModuleApi) {
  api.expressionEditors.add({
    id: "elsa.liquid-expression-editor",
    order: 110,
    supports: context => context.syntax === liquidSyntax,
    surfaces: {
      expanded: LiquidExpandedEditor
    },
    sourceRenderer: {
      compact: LiquidSourceRenderer,
      expanded: LiquidSourceRenderer
    }
  });
}

export function LiquidSourceRenderer({ context }: StudioExpressionSourceRendererProps) {
  return context.isSensitive
    ? <span>Protected Liquid source</span>
    : <code>{formatSourceValue(context.value)}</code>;
}

export function LiquidExpandedEditor({ descriptor, value, disabled, onChange }: StudioExpressionEditorProps) {
  const document = {
    uri: `elsa://expressions/liquid/${encodeURIComponent(descriptor.name || "expression")}`,
    language: "liquid",
    value: formatValue(value)
  };

  return (
    <div className="liquid-expression-expanded">
      <div className="liquid-expression-toolbar" aria-hidden="true">
        <span>Liquid</span>
      </div>
      <StudioCodeEditor
        ariaLabel="Liquid expanded expression"
        document={document}
        languageAdapter={liquidLanguageAdapter}
        minHeight="240px"
        readOnly={disabled}
        theme="dark"
        onChange={nextDocument => onChange(nextDocument.value)}
      />
    </div>
  );
}

function formatValue(value: unknown) {
  return value == null ? "" : String(value);
}

function formatSourceValue(value: unknown) {
  const source = formatValue(value);
  return source.length > 4_000 ? `${source.slice(0, 3_997)}...` : source;
}
