// @vitest-environment node
import { fileURLToPath } from "node:url";
import ts from "typescript";
import { describe, expect, it } from "vitest";

// Exercise the actual declarations through both entry points; this is not an immutable-package witness.
const entrypoints = [
  { label: "canonical host SDK", path: "../sdk/index.ts" },
  { label: "module-author workspace SDK", path: "../../../StudioSdk/src/index.ts" }
];
const consumer = `
import type { StudioWeaverPrivilegeClass, StudioWeaverPreparedInvocation, StudioWeaverEffectReceipt,
  StudioWeaverToolContractContribution, StudioWeaverContextProviderContribution, StudioWeaverContributionDiagnostic,
  StudioWeaverClientExecutionResult, StudioWeaverToolResult } from "@elsa-workflows/studio-sdk";
const classes: readonly StudioWeaverPrivilegeClass[] = ["administrative", "irrecoverable", "activation"];
const legacyFields: Pick<StudioWeaverPreparedInvocation, "requiredPrivilegeClasses"> = {};
const currentFields: Pick<StudioWeaverPreparedInvocation, "requiredPrivilegeClasses"> = { requiredPrivilegeClasses: classes };
const nullFields: Pick<StudioWeaverPreparedInvocation, "requiredPrivilegeClasses"> = { requiredPrivilegeClasses: null };
const emptyFields: Pick<StudioWeaverPreparedInvocation, "requiredPrivilegeClasses"> = { requiredPrivilegeClasses: [] };
declare const prepared: StudioWeaverPreparedInvocation;
// @ts-expect-error The server's frozen class list is not a mutable authoring collection.
prepared.requiredPrivilegeClasses?.push("activation");
const target = { moduleId: "Owner", resourceType: "sample", resourceId: "one", revision: "r1" };
const receipt: StudioWeaverEffectReceipt = { invocationId: "invocation", before: target, after: target,
  scope: "noPersistedEffect", moduleCode: "sample.test.negative", evidence: { typeId: "sample.verdict", schemaVersion: "1", data: { passed: false } } };
const legacyReceipt: StudioWeaverEffectReceipt = { invocationId: "invocation", before: target, after: target,
  scope: "serverPersisted", evidence: receipt.evidence };
const legacyTool: StudioWeaverToolContractContribution = { id: "sample_test", slotId: "sample", displayName: "Test", description: "Test resource",
  surfaces: ["*"], inputSchema: {}, resourceTargetSchema: {}, resultSchema: {}, risk: "admin", invocationModes: ["proposal"] };
const currentTool: StudioWeaverToolContractContribution = { ...legacyTool, executionLocation: "server",
  canPerformWithoutPersistence: true, moduleCodes: ["sample.test.negative"] };
const undeclaredTool: StudioWeaverToolContractContribution = { ...legacyTool, moduleCodes: null };
const closedEmptyTool: StudioWeaverToolContractContribution = { ...legacyTool, moduleCodes: [] };
// @ts-expect-error Declared code collections are readonly metadata.
currentTool.moduleCodes?.push("sample.test.changed");
const legacyContext: StudioWeaverContextProviderContribution = { id: "owner", displayName: "Owner", surfaces: ["*"], sensitivity: "internal", collect: async () => [] };
const currentContext: StudioWeaverContextProviderContribution = { ...legacyContext, moduleCodes: ["sample.read.unavailable"] };
const diagnostic: StudioWeaverContributionDiagnostic = { contributorId: "owner", code: "sample.read.unavailable", message: "Owner unavailable.", required: true, ownerUnavailable: true };
const legacyResult: StudioWeaverToolResult = { toolCallId: "call", toolName: "sample_test", succeeded: false, content: "Refused." };
const refusalResult: StudioWeaverToolResult = { ...legacyResult, error: { code: "module.owner.unavailable", message: "Owner unavailable.",
  statusCode: 503, ownerUnavailable: true } };
const nullResultError: StudioWeaverToolResult = { ...legacyResult, error: null };
// @ts-expect-error Typed diagnostic classification cannot be supplied as a string.
const invalidResultError: StudioWeaverToolResult = { ...legacyResult, error: { ...refusalResult.error!, ownerUnavailable: "true" } };
// @ts-expect-error A transport flag cannot make a client ACK a server performed result.
const clientPerformed: StudioWeaverClientExecutionResult = { outcome: "performed", summary: "Not a client ACK outcome." };
`;

describe("Weaver module-author SDK compatibility", () => {
  it.each(entrypoints)("$label supports optional effects without changing legacy callers", ({ path }) => {
    const entry = fileURLToPath(new URL(path, import.meta.url));
    const consumerPath = fileURLToPath(new URL("./__weaver-sdk-consumer__.ts", import.meta.url));
    const options: ts.CompilerOptions = { strict: true, noEmit: true, skipLibCheck: true, types: [],
      target: ts.ScriptTarget.ES2022, module: ts.ModuleKind.ESNext, moduleResolution: ts.ModuleResolutionKind.Bundler,
      jsx: ts.JsxEmit.ReactJSX, paths: { "@elsa-workflows/studio-sdk": [entry] } };
    const host = ts.createCompilerHost(options); const original = host.getSourceFile.bind(host);
    host.getSourceFile = (name, version, onError, create) => name === consumerPath
      ? ts.createSourceFile(name, consumer, version, true) : original(name, version, onError, create);
    const program = ts.createProgram([entry, consumerPath], options, host);
    const source = program.getSourceFile(consumerPath)!;
    // Like the existing sdk-typing-copies guard, isolate actual consuming-module diagnostics.
    expect([...program.getSyntacticDiagnostics(source), ...program.getSemanticDiagnostics(source)]
      .map(item => ts.flattenDiagnosticMessageText(item.messageText, "\n"))).toEqual([]);
  });
});
