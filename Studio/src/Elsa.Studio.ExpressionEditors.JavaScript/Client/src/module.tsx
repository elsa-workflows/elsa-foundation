import React from "react";
import { javaScriptLanguageAdapter, StudioCodeEditor } from "@elsa-workflows/studio-code-editor";
import type { ElsaStudioModuleApi, StudioExpressionEditorProps } from "@elsa-workflows/studio-sdk";
import "./styles.css";

const javaScriptSyntax = "JavaScript";

export function register(api: ElsaStudioModuleApi) {
  api.expressionEditors.add({
    id: "elsa.javascript-expression-editor",
    order: 100,
    supports: context => context.syntax === javaScriptSyntax,
    surfaces: {
      inline: JavaScriptInlineEditor,
      expanded: JavaScriptExpandedEditor
    }
  });
}

export function JavaScriptInlineEditor({ value, disabled, onChange }: StudioExpressionEditorProps) {
  return (
    <textarea
      aria-label="JavaScript expression"
      className="js-expression-editor inline"
      value={formatValue(value)}
      disabled={disabled}
      rows={2}
      spellCheck={false}
      autoCapitalize="off"
      autoCorrect="off"
      onChange={event => onChange(event.target.value)}
    />
  );
}

export function JavaScriptExpandedEditor({ descriptor, value, disabled, onChange }: StudioExpressionEditorProps) {
  const document = {
    uri: `elsa://expressions/javascript/${encodeURIComponent(descriptor.name || "expression")}`,
    language: "javascript",
    value: formatValue(value)
  };

  return (
    <div className="js-expression-expanded">
      <div className="js-expression-toolbar" aria-hidden="true">
        <span>JavaScript</span>
      </div>
      <StudioCodeEditor
        ariaLabel="JavaScript expanded expression"
        document={document}
        languageAdapter={javaScriptLanguageAdapter}
        minHeight="260px"
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
