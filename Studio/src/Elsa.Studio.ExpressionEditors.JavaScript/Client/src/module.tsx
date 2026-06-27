import React from "react";
import CodeMirror from "@uiw/react-codemirror";
import { javascript } from "@codemirror/lang-javascript";
import type { ElsaStudioModuleApi, StudioExpressionEditorProps } from "@elsa-workflows/studio-sdk";
import "./styles.css";

const javaScriptSyntax = "JavaScript";
const javaScriptExtensions = [javascript({ jsx: true, typescript: true })];

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

export function JavaScriptExpandedEditor({ value, disabled, onChange }: StudioExpressionEditorProps) {
  return (
    <div className="js-expression-expanded">
      <div className="js-expression-toolbar" aria-hidden="true">
        <span>JavaScript</span>
      </div>
      <CodeMirror
        aria-label="JavaScript expanded expression"
        className="js-expression-code"
        value={formatValue(value)}
        minHeight="260px"
        basicSetup={{
          lineNumbers: true,
          foldGutter: true,
          highlightActiveLine: true,
          bracketMatching: true,
          autocompletion: true
        }}
        editable={!disabled}
        readOnly={disabled}
        theme="dark"
        extensions={javaScriptExtensions}
        onChange={nextValue => onChange(nextValue)}
      />
    </div>
  );
}

function formatValue(value: unknown) {
  return value == null ? "" : String(value);
}
