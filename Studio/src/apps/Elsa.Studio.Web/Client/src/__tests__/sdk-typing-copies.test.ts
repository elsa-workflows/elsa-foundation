// @vitest-environment node

import { fileURLToPath } from "node:url";
import ts from "typescript";
import { describe, expect, it } from "vitest";

// Compiler-backed architecture guard for the type-only module boundary. Each extension's ambient
// declaration is checked in isolation, so another copy cannot silently supply a missing member.
// Secrets consumes the canonical SDK directly and runs its own package typecheck.
const typingSources = [
  { label: "canonical SDK", path: "../sdk/index.ts", hasPropertyEditors: true },
  { label: "JavaScript copy", path: "../../../../../../../Extensions/ExpressionEditors.JavaScript/studio/Elsa.Studio.ExpressionEditors.JavaScript/Client/src/studio-sdk.d.ts", hasPropertyEditors: false },
  { label: "Liquid copy", path: "../../../../../../../Extensions/ExpressionEditors.Liquid/studio/Elsa.Studio.ExpressionEditors.Liquid/Client/src/studio-sdk.d.ts", hasPropertyEditors: false },
  { label: "Connections copy", path: "../../../../../../../Extensions/Connections/studio/Elsa.Studio.Connections/Client/src/studio-sdk.d.ts", hasPropertyEditors: true }
];

const inputContract = `
type Equal<A, B> = (<T>() => T extends A ? 1 : 2) extends (<T>() => T extends B ? 1 : 2) ? true : false;
type Assert<T extends true> = T;
type Metadata = Pick<StudioActivityInputDescriptor, "tier" | "isRequired" | "isSensitive" | "isCredential">;
const legacyMetadata: Metadata = {};
type InputContract = [
  Assert<Equal<Metadata["tier"], string | null | undefined>>,
  Assert<Equal<Metadata["isRequired"], boolean | null | undefined>>,
  Assert<Equal<Metadata["isSensitive"], boolean | null | undefined>>,
  Assert<Equal<Metadata["isCredential"], boolean | null | undefined>>
];`;

const editorContract = `
type ReportingProps = Pick<StudioActivityPropertyEditorProps, "onValidationChange">;
type ReportingContribution = Pick<StudioActivityPropertyEditorContribution, "supportsValidationReporting">;
type ActivityContext = Pick<StudioActivityPropertyEditorContext, "activityId">;
const legacyProps: ReportingProps = {};
const legacyContribution: ReportingContribution = {};
const legacyContext: ActivityContext = {};
type EditorContract = [
  Assert<Equal<ReportingProps["onValidationChange"], ((hasError: boolean) => void) | undefined>>,
  Assert<Equal<ReportingContribution["supportsValidationReporting"], true | undefined>>,
  Assert<Equal<ActivityContext["activityId"], string | null | undefined>>
];`;

function compileConsumer({ path, hasPropertyEditors }: typeof typingSources[number]) {
  const declarationPath = fileURLToPath(new URL(path, import.meta.url));
  const consumerPath = fileURLToPath(new URL("./__sdk-contract-consumer__.ts", import.meta.url));
  const moduleName = path === "../sdk/index.ts" ? "../sdk/index" : "@elsa-workflows/studio-sdk";
  const imports = ["StudioActivityInputDescriptor", ...(hasPropertyEditors
    ? ["StudioActivityPropertyEditorProps", "StudioActivityPropertyEditorContribution", "StudioActivityPropertyEditorContext"] : [])];
  const consumer = `import type { ${imports.join(", ")} } from "${moduleName}";\n${inputContract}\n${hasPropertyEditors ? editorContract : ""}`;
  const options: ts.CompilerOptions = {
    strict: true,
    noEmit: true,
    skipLibCheck: true,
    target: ts.ScriptTarget.ES2022,
    module: ts.ModuleKind.ESNext,
    moduleResolution: ts.ModuleResolutionKind.Bundler,
    jsx: ts.JsxEmit.ReactJSX,
    types: []
  };
  const host = ts.createCompilerHost(options);
  const getSourceFile = host.getSourceFile.bind(host);
  host.getSourceFile = (name, languageVersion, onError, shouldCreateNewSourceFile) => name === consumerPath
    ? ts.createSourceFile(name, consumer, languageVersion, true)
    : getSourceFile(name, languageVersion, onError, shouldCreateNewSourceFile);
  const program = ts.createProgram([declarationPath, consumerPath], options, host);
  const source = program.getSourceFile(consumerPath)!;
  // This guard checks consumer compatibility, not unrelated implementation diagnostics in SDK exports.
  return [...program.getSyntacticDiagnostics(source), ...program.getSemanticDiagnostics(source)]
    .map(diagnostic => ts.flattenDiagnosticMessageText(diagnostic.messageText, "\n"));
}

describe("SDK declaration consumer compatibility", () => {
  it.each(typingSources)("$label supports metadata and backwards-compatible optional editor contracts", source => {
    expect(compileConsumer(source)).toEqual([]);
  });
});
