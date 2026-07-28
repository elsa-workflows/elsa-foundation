import React, { memo } from "react";
import {
  createStudioCodeToolingProjection,
  javaScriptLanguageAdapter,
  projectStudioCodeDiagnostics,
  StudioCodeEditor,
  type StudioCodeEditorProfile
} from "@elsa-workflows/studio-code-editor";
import type { ElsaStudioModuleApi, StudioExpressionEditorProps, StudioExpressionSourceRendererProps } from "@elsa-workflows/studio-sdk";
import { javaScriptToolingProjection } from "./javaScriptToolingProjection";
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
    },
    metadata: {
      toolingCapabilities: {
        highlighting: true,
        signatures: true,
        formatting: false,
        localDiagnostics: true
      }
    },
    sourceRenderer: {
      compact: JavaScriptSourceRenderer,
      expanded: JavaScriptSourceRenderer
    }
  });
}

export function JavaScriptSourceRenderer({ context }: StudioExpressionSourceRendererProps) {
  return context.isSensitive
    ? <span>Protected JavaScript source</span>
    : <code>{formatSourceValue(context.value)}</code>;
}

export function JavaScriptInlineEditor(props: StudioExpressionEditorProps) {
  return <MemoizedJavaScriptCodeEditor {...props} profile="compact" />;
}

export function JavaScriptExpandedEditor(props: StudioExpressionEditorProps) {
  return (
    <div className="js-expression-expanded">
      <div className="js-expression-toolbar" aria-hidden="true">
        <span>JavaScript</span>
      </div>
      <MemoizedJavaScriptCodeEditor {...props} profile="expanded" />
    </div>
  );
}

function JavaScriptCodeEditor({
  descriptor,
  value,
  disabled,
  initialFocus,
  context,
  onExpand,
  onChange,
  profile
}: StudioExpressionEditorProps & { profile: StudioCodeEditorProfile }) {
  const source = context.document?.source ?? formatValue(value);
  const uri = context.document?.uri
    ?? `elsa://expressions/javascript/${encodeURIComponent(descriptor.name || "expression")}`;
  const document = {
    uri,
    language: "javascript",
    value: source,
    version: context.document?.sourceVersion
  };
  const tooling = createStudioCodeToolingProjection({
    document: context.document,
    authoringContext: withJavaScriptCapabilities(context.authoringContext?.data),
    tooling: context.tooling,
    languageProjection: javaScriptToolingProjection
  });
  const diagnostics = projectStudioCodeDiagnostics(uri, context.validation?.data?.diagnostics ?? []);

  return (
    <div className={`js-expression-editor ${profile}`}>
      <StudioCodeEditor
        ariaLabel={`JavaScript ${profile === "compact" ? "expression" : "expanded expression"}`}
        focusOnMount={initialFocus}
        completionProvider={tooling.completionProvider}
        diagnostics={diagnostics}
        document={document}
        hoverProvider={tooling.hoverProvider}
        languageAdapter={javaScriptLanguageAdapter}
        minHeight={profile === "compact" ? "2.25rem" : "260px"}
        profile={profile}
        readOnly={disabled}
        sessionKey={context.document?.id ?? uri}
        signatureProvider={tooling.signatureProvider}
        status={toolingStatus(context)}
        theme="studio"
        onChange={nextDocument => onChange(nextDocument.value)}
        onFocus={context.onFocus}
        onExpand={onExpand}
      />
    </div>
  );
}

function withJavaScriptCapabilities(
  authoringContext: NonNullable<StudioExpressionEditorProps["context"]["authoringContext"]>["data"]
) {
  if (!authoringContext) return undefined;
  return {
    ...authoringContext,
    capabilities: {
      ...authoringContext.capabilities,
      highlighting: true,
      signatures: true,
      formatting: false,
      localDiagnostics: true
    }
  };
}

const MemoizedJavaScriptCodeEditor = memo(JavaScriptCodeEditor, sameJavaScriptEditorProps);

function sameJavaScriptEditorProps(
  previous: StudioExpressionEditorProps & { profile: StudioCodeEditorProfile },
  next: StudioExpressionEditorProps & { profile: StudioCodeEditorProfile }
) {
  return previous.descriptor === next.descriptor &&
    previous.syntax === next.syntax &&
    previous.value === next.value &&
    previous.disabled === next.disabled &&
    previous.initialFocus === next.initialFocus &&
    previous.profile === next.profile &&
    previous.context.document?.id === next.context.document?.id &&
    previous.context.document?.uri === next.context.document?.uri &&
    previous.context.document?.source === next.context.document?.source &&
    previous.context.document?.sourceVersion === next.context.document?.sourceVersion &&
    previous.context.authoringContext === next.context.authoringContext &&
    previous.context.validation === next.context.validation &&
    previous.context.tooling === next.context.tooling;
}

function toolingStatus(context: StudioExpressionEditorProps["context"]) {
  const authoringState = context.authoringContext?.state;
  if (authoringState === "supported-empty") return "No JavaScript symbols are available in this context.";
  const state = authoringState === "ready" ? context.validation?.state : authoringState;
  if (!state || state === "ready" || state === "supported-empty") return undefined;
  if (state === "unauthorized") return "JavaScript code intelligence is unavailable for your permissions.";
  if (state === "incompatible") return "JavaScript code intelligence is incompatible with this Studio version.";
  return "JavaScript code intelligence is unavailable. Syntax highlighting remains active.";
}

function formatValue(value: unknown) {
  return value == null ? "" : String(value);
}

function formatSourceValue(value: unknown) {
  const source = formatValue(value);
  return source.length > 4_000 ? `${source.slice(0, 3_997)}...` : source;
}
