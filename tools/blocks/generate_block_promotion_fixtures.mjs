#!/usr/bin/env node
// The backend consumes these exact production-planner documents; generation is not runtime proof.
import { readFile, writeFile, mkdir } from "node:fs/promises";
import { createRequire } from "node:module";
import { dirname, resolve } from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";
const repository = resolve(dirname(fileURLToPath(import.meta.url)), "../..");
const require = createRequire(resolve(repository, "Studio/package.json"));
const { createServer } = await import(pathToFileURL(require.resolve("vite")).href);
const client = resolve(repository, "Studio/src/essentials/Elsa.Studio.Workflows/Client");
const server = await createServer({ root: client, configFile: false, optimizeDeps: { noDiscovery: true }, server: { middlewareMode: true }, appType: "custom", logLevel: "error" });
try {
  const { planBlockPromotion, bindBlockPromotionVersion } = await server.ssrLoadModule(resolve(client, "src/graph-authoring/blockPromotion.ts"));
  const { context, descriptor, literal } = await server.ssrLoadModule(resolve(client, "src/__tests__/blockPromotionFixtures.ts"));
  const { preservesJavaScriptWithAddedVariable } = await server.ssrLoadModule(resolve(repository, "extensions/ExpressionEditors.JavaScript/studio/Elsa.Studio.ExpressionEditors.JavaScript/Client/src/variableAdditionPreservation.ts"));
  const cases = [];
  const versions = { sequence: "Elsa.Activities.Sequence.Activities.Sequence", flowchart: "Elsa.Activities.Flowchart.Activities.Flowchart", bpmn: "Elsa.Activities.Bpmn.Activities.BpmnProcess", write: "Elsa.Activities.Primitives.Activities.WriteLine" };
  for (const kind of ["sequence", "flowchart", "bpmn"]) for (const parameter of [false, true]) {
    const ctx = context();
    ctx.catalog = ctx.catalog.map(item => ({ ...item, ports: [{ name: "Done", type: "outcome", referenceKey: "Done" }] }));
    ctx.catalog.push(...["flowchart", "bpmn"].map(key => ({ ...descriptor(key, versions[key]), ports: [{ name: "Done", type: "outcome", referenceKey: "Done" }] })));
    ctx.capabilities.storageDriverKeys = ["elsa.json"];
    ctx.capabilities.types.forEach(type => { type.compatibleStorageDriverKeys = ["elsa.json"]; });
    ctx.expressionBoundary = { revision: "explicit-args-closed-boundary-v1", allows: preservesJavaScriptWithAddedVariable };
    ctx.sourceKey = `qualification:${kind}:${parameter ? "parameter" : "direct"}`;
    const block = ctx.document.rootActivity.structure.payload.activities[0];
    block.activityVersionId = kind;
    const read = parameter ? { expressionType: "JavaScript", value: { language: "JavaScript", source: "args.message", resultType: { alias: "String", collectionKind: "single" }, options: {}, capabilityProfile: "binding-pure-v1", parameters: { message: { kind: "WorkflowRequest", memberKey: "Greeting" } } } } : { expressionType: "Input", value: { referenceKey: "greeting" } };
    const assign = { nodeId: "assign", activityVersionId: "set", inputs: [{ referenceKey: "value", value: read }], outputs: [], intrinsic: { kind: "set", valueType: { alias: "String", collectionKind: "Single" }, variable: { referenceKey: "local", declaringScopeId: "block" } } };
    const show = { nodeId: "show", activityVersionId: "write", inputs: [{ referenceKey: "text", value: { expressionType: "Variable", value: { referenceKey: "local", declaringScopeId: "block" } } }], outputs: [] };
    block.structure = { kind: `elsa.${kind === "bpmn" ? "bpmn" : kind}.structure`, schemaVersion: "1.0.0", payload: {
      activities: [assign, show], variables: [{ referenceKey: "local", name: "Shipment code", type: { alias: "String", collectionKind: "Single" }, storageDriverType: null, default: literal("") }]
    } };
    if (kind === "flowchart") Object.assign(block.structure.payload, { startNodeId: "assign", connections: [{ id: "assign-show", source: { nodeId: "assign", port: "Done" }, target: { nodeId: "show" }, label: "Keep this route" }], nodeMetadata: {}, connectionMetadata: {} });
    if (kind === "bpmn") Object.assign(block.structure.payload, { elements: [{ elementId: "start", elementType: "startEvent" }, { elementId: "element-assign", elementType: "task", childNodeId: "assign" }, { elementId: "element-show", elementType: "task", childNodeId: "show" }, { elementId: "end", elementType: "endEvent" }], sequenceFlows: [{ flowId: "start-assign", sourceRef: "start", targetRef: "element-assign" }, { flowId: "assign-show", sourceRef: "element-assign", targetRef: "element-show" }, { flowId: "show-end", sourceRef: "element-show", targetRef: "end" }], diagram: { shapes: { start: { x: 0, y: 0, width: 36, height: 36 }, "element-assign": { x: 100, y: 0, width: 100, height: 80 }, "element-show": { x: 300, y: 0, width: 100, height: 80 }, end: { x: 500, y: 0, width: 36, height: 36 } }, edges: {} } });
    ctx.document.layout = [{ nodeId: "block", x: 100, y: 100 }];
    ctx.document.activityPresentation = [{ nodeId: "block", displayName: "Prepare shipment" }];
    const original = structuredClone(ctx.document);
    const plan = planBlockPromotion(ctx, "block");
    if (plan.status !== "ready") throw new Error(JSON.stringify({ kind, parameter, plan }));
    if (JSON.stringify(original) !== JSON.stringify(ctx.document)) throw new Error("Analysis changed the source");
    const replaced = bindBlockPromotionVersion(ctx, plan, "published-version");
    if (replaced.status !== "ready") throw new Error(JSON.stringify(replaced));
    cases.push({ name: `${kind}-${parameter ? "parameter" : "direct"}`, kind, parameter, original, candidate: plan.candidate,
      inputs: ctx.publicInputs, callSite: { ...plan.callSite, activityVersionId: "published-version", structure: null }, replacement: replaced.document,
      outputReferenceKey: plan.candidate.contract.outputs[0].referenceKey, expected: ["one", "two", "one"] });
  }
  const output = resolve(repository, "specs/1204-named-embedded-blocks/contracts/block-promotion-cases.json");
  const bytes = JSON.stringify({ formatVersion: 1, kind: "pure-planner-golden-documents-not-runtime-evidence", versions, cases }, null, 2) + "\n";
  if (process.argv.includes("--check")) { if (await readFile(output, "utf8") !== bytes) throw new Error("Promotion fixtures are stale. Regenerate and review them."); }
  else { await mkdir(dirname(output), { recursive: true }); await writeFile(output, bytes); }
  process.stdout.write(`Reviewed ${cases.length} production-planner promotion documents\n`);
} finally { await server.close(); }
