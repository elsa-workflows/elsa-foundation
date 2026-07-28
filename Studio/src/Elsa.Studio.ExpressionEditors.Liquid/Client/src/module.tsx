import React from "react";
import {
  createStudioCodeToolingProjection,
  liquidLanguageAdapter,
  projectStudioCodeDiagnostics,
  StudioCodeEditor,
  type StudioCodeEditorProfile
} from "@elsa-workflows/studio-code-editor";
import type { ElsaStudioModuleApi, StudioExpressionEditorProps, StudioExpressionSourceRendererProps } from "@elsa-workflows/studio-sdk";
import "./styles.css";

const liquidSyntax = "Liquid";

export function register(api: ElsaStudioModuleApi) {
  api.expressionEditors.add({
    id: "elsa.liquid-expression-editor",
    order: 110,
    supports: context => context.syntax === liquidSyntax,
    surfaces: {
      inline: LiquidInlineEditor,
      expanded: LiquidExpandedEditor
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

export function LiquidInlineEditor(props: StudioExpressionEditorProps) {
  return <LiquidCodeEditor {...props} profile="compact" />;
}

export function LiquidExpandedEditor(props: StudioExpressionEditorProps) {
  return (
    <div className="liquid-expression-expanded">
      <div className="liquid-expression-toolbar" aria-hidden="true">
        <span>Liquid</span>
      </div>
      <LiquidCodeEditor {...props} profile="expanded" />
    </div>
  );
}

function LiquidCodeEditor({
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
    ?? `elsa://expressions/liquid/${encodeURIComponent(descriptor.name || "expression")}`;
  const document = {
    uri,
    language: "liquid",
    value: source,
    version: context.document?.sourceVersion
  };
  const tooling = createStudioCodeToolingProjection({
    document: context.document,
    authoringContext: withLiquidCapabilities(context.authoringContext?.data),
    tooling: context.tooling
  });
  const diagnostics = projectStudioCodeDiagnostics(uri, context.validation?.data?.diagnostics ?? []);

  return (
    <div className={`liquid-expression-editor ${profile}`}>
      <StudioCodeEditor
        ariaLabel={`Liquid ${profile === "compact" ? "expression" : "expanded expression"}`}
        focusOnMount={initialFocus}
        completionProvider={tooling.completionProvider}
        diagnostics={diagnostics}
        document={document}
        hoverProvider={tooling.hoverProvider}
        languageAdapter={liquidLanguageAdapter}
        minHeight={profile === "compact" ? "2.25rem" : "240px"}
        profile={profile}
        readOnly={disabled}
        sessionKey={context.editorSessionScope
          ? `${context.editorSessionScope}\u001f${context.document?.id ?? uri}`
          : context.document?.id ?? uri}
        signatureProvider={tooling.signatureProvider}
        status={toolingStatus(context)}
        theme="studio"
        onChange={nextDocument => onChange(nextDocument.value)}
        onBlur={context.onBlur}
        onFocus={context.onFocus}
        onExpand={onExpand}
      />
    </div>
  );
}

function withLiquidCapabilities(
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

function toolingStatus(context: StudioExpressionEditorProps["context"]) {
  const authoringState = context.authoringContext?.state;
  if (authoringState === "supported-empty") return "No Liquid symbols are available in this context.";
  const state = authoringState === "ready" ? context.validation?.state : authoringState;
  if (!state || state === "ready" || state === "supported-empty") return undefined;
  if (state === "unauthorized") return "Liquid code intelligence is unavailable for your permissions.";
  if (state === "incompatible") return "Liquid code intelligence is incompatible with this Studio version.";
  return "Liquid code intelligence is unavailable. Syntax highlighting remains active.";
}

function formatValue(value: unknown) {
  return value == null ? "" : String(value);
}

function formatSourceValue(value: unknown) {
  const source = formatValue(value);
  return source.length > 4_000 ? `${source.slice(0, 3_997)}...` : source;
}
