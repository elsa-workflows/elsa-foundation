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
import type { StudioAgentPrivilegeClass, StudioAgentPreparedInvocation, StudioAgentEffectReceipt,
  StudioAgentToolContractContribution, StudioAgentContextProviderContribution, StudioAgentContributionDiagnostic,
  StudioAgentClientExecutionResult } from "@elsa-workflows/studio-sdk";
const classes: readonly StudioAgentPrivilegeClass[] = ["administrative", "irrecoverable", "activation"];
const legacyFields: Pick<StudioAgentPreparedInvocation, "requiredPrivilegeClasses"> = {};
const currentFields: Pick<StudioAgentPreparedInvocation, "requiredPrivilegeClasses"> = { requiredPrivilegeClasses: classes };
const nullFields: Pick<StudioAgentPreparedInvocation, "requiredPrivilegeClasses"> = { requiredPrivilegeClasses: null };
const emptyFields: Pick<StudioAgentPreparedInvocation, "requiredPrivilegeClasses"> = { requiredPrivilegeClasses: [] };
declare const prepared: StudioAgentPreparedInvocation;
// @ts-expect-error The server's frozen class list is not a mutable authoring collection.
prepared.requiredPrivilegeClasses?.push("activation");
const target = { moduleId: "Owner", resourceType: "sample", resourceId: "one", revision: "r1" };
const receipt: StudioAgentEffectReceipt = { invocationId: "invocation", before: target, after: target,
  scope: "noPersistedEffect", moduleCode: "sample.test.negative", evidence: { typeId: "sample.verdict", schemaVersion: "1", data: { passed: false } } };
const legacyReceipt: StudioAgentEffectReceipt = { invocationId: "invocation", before: target, after: target,
  scope: "serverPersisted", evidence: receipt.evidence };
const legacyTool: StudioAgentToolContractContribution = { id: "sample_test", slotId: "sample", displayName: "Test", description: "Test resource",
  surfaces: ["*"], inputSchema: {}, resourceTargetSchema: {}, resultSchema: {}, risk: "admin", invocationModes: ["proposal"] };
const currentTool: StudioAgentToolContractContribution = { ...legacyTool, executionLocation: "server",
  canPerformWithoutPersistence: true, moduleCodes: ["sample.test.negative"] };
const undeclaredTool: StudioAgentToolContractContribution = { ...legacyTool, moduleCodes: null };
const closedEmptyTool: StudioAgentToolContractContribution = { ...legacyTool, moduleCodes: [] };
// @ts-expect-error Declared code collections are readonly metadata.
currentTool.moduleCodes?.push("sample.test.changed");
const legacyContext: StudioAgentContextProviderContribution = { id: "owner", displayName: "Owner", surfaces: ["*"], sensitivity: "internal", collect: async () => [] };
const currentContext: StudioAgentContextProviderContribution = { ...legacyContext, moduleCodes: ["sample.read.unavailable"] };
const diagnostic: StudioAgentContributionDiagnostic = { contributorId: "owner", code: "sample.read.unavailable", message: "Owner unavailable.", required: true, ownerUnavailable: true };
// @ts-expect-error A transport flag cannot make a client ACK a server performed result.
const clientPerformed: StudioAgentClientExecutionResult = { outcome: "performed", summary: "Not a client ACK outcome." };
`;

describe("Agent module-author SDK compatibility", () => {
  it.each(entrypoints)("$label supports optional effects without changing legacy callers", ({ path }) => {
    const entry = fileURLToPath(new URL(path, import.meta.url));
    const consumerPath = fileURLToPath(new URL("./__agent-sdk-consumer__.ts", import.meta.url));
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
