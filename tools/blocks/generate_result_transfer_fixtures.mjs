#!/usr/bin/env node
// Golden planner documents consumed by both the TypeScript regressions and the actual-host tests.
import { mkdir, readFile, writeFile } from "node:fs/promises";
import { createRequire } from "node:module";
import { dirname, resolve } from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";

const repository = resolve(dirname(fileURLToPath(import.meta.url)), "../..");
const require = createRequire(resolve(repository, "Studio/package.json"));
const { createServer } = await import(pathToFileURL(require.resolve("vite")).href);
const client = resolve(repository, "Studio/src/essentials/Elsa.Studio.Workflows/Client");
const server = await createServer({ root: client, configFile: false, optimizeDeps: { noDiscovery: true }, server: { middlewareMode: true }, appType: "custom", logLevel: "error" });
try {
  const planner = await server.ssrLoadModule(resolve(client, "src/graph-authoring/nativeBlocks.ts"));
  const adapter = await server.ssrLoadModule(resolve(client, "src/workflowAdapter.ts"));
  const { preservesJavaScriptWithAddedVariable } = await server.ssrLoadModule(resolve(repository, "Extensions/ExpressionEditors.JavaScript/studio/Elsa.Studio.ExpressionEditors.JavaScript/Client/src/variableAdditionPreservation.ts"));
  const expressionScopePreservation = { revision: "javascript-variable-addition-v1", allowsVariableAddition: preservesJavaScriptWithAddedVariable };
  const fixtures = await server.ssrLoadModule(resolve(client, "src/__tests__/fixtures.ts"));
  const whileActivity = { ...fixtures.whileActivity, activityTypeKey: "Elsa.Activities.While.Activities.While" };
  const catalog = [fixtures.sequenceActivity, fixtures.flowchartActivity].map(item => ({ ...item,
    ports: [{ name: "Done" }, { name: "Break" }], designFacets: [...item.designFacets ?? [], { kind: "elsa.outcomes", schemaVersion: "1", payload: { ports: [{ name: "Done" }, { name: "Break" }] } }] }));
  catalog.push(whileActivity, { ...fixtures.writeLine, activityVersionId: "inline-version", activityTypeKey: "Elsa.Activities.Primitives.Activities.Inline", ports: [{ name: "Done" }],
    inputs: [{ name: "Expression", referenceKey: "Expression", typeName: "Object", collectionKind: "Single", isNullable: true, isRequired: false }], outputs: [] }, fixtures.setVariableActivity);
  const cases = [];
  for (const kind of ["sequence", "flowchart"]) for (const profile of ["direct", "parameter", "loop"]) {
    const parameter = profile === "parameter";
    const loop = profile === "loop";
    const body = adapter.createActivityNode(kind === "sequence" ? fixtures.sequenceActivity : fixtures.flowchartActivity, loop ? "body" : "root");
    const argument = (expressionType, value) => ({ expressionType, value });
    const node = (nodeId, expressionType, value) => ({ nodeId, activityVersionId: "inline-version", inputs: [{ referenceKey: "Expression", value: argument(expressionType, value) }], outputs: [] });
    const read = { producerNodeId: "producer", projectionKey: "$result", producerScopeId: "scope:root" };
    // Object syntax preserves numeric JSON for Inline's Object input; Literal scalars are text at that boundary.
    const producer = node("producer", loop ? "Variable" : "Object", loop ? { referenceKey: "counter" } : 42);
    const consumer = node("consumer", parameter ? "JavaScript" : "ActivityResult", parameter ? {
      language: "JavaScript", source: "args.prior + 1", resultType: { alias: "Object", collectionKind: "single" }, options: {}, capabilityProfile: "binding-pure-v1",
      parameters: { prior: { kind: "ActivityResult", producerNodeId: "producer", projectionKey: "$result" } }
    } : read);
    const answer = { nodeId: "answer", activityVersionId: "elsa.intrinsic.set-output@1", intrinsic: { kind: "setOutput", valueType: { alias: "Elsa.Any", collectionKind: "single" } }, outputs: [],
      inputs: [{ referenceKey: "name", value: argument("Literal", "answer") }, { referenceKey: "value", value: argument("ActivityResult", { producerNodeId: "consumer", projectionKey: "$result", producerScopeId: "scope:root" }) }] };
    body.structure.payload.activities = [producer, consumer, answer];
    if (kind === "flowchart") Object.assign(body.structure.payload, { startNodeId: "producer", connections: [
      { id: "read", source: { nodeId: "producer", port: "Done" }, target: { nodeId: "consumer" } },
      { id: "answer", source: { nodeId: "consumer", port: "Done" }, target: { nodeId: "answer" } }
    ] });
    let rootActivity = body;
    const variables = [];
    const scope = [];
    if (loop) {
      const increment = { nodeId: "increment", activityVersionId: "elsa.intrinsic.set@1", inputs: [{ referenceKey: "value", value: argument("JavaScript", {
        language: "JavaScript", source: "args.counter + 1", resultType: { alias: "Int32", collectionKind: "single" }, options: {}, capabilityProfile: "binding-pure-v1",
        parameters: { counter: { kind: "Variable", declaringScopeNodeId: "workflow", variableKey: "counter" } }
      }) }], outputs: [],
        intrinsic: { kind: "set", valueType: { alias: "Elsa.Any", collectionKind: "single" }, variable: { referenceKey: "counter" } } };
      body.structure.payload.activities = [increment, producer, consumer];
      if (kind === "flowchart") Object.assign(body.structure.payload, { startNodeId: "increment", connections: [
        { id: "increment", source: { nodeId: "increment", port: "Done" }, target: { nodeId: "producer" } },
        { id: "consume", source: { nodeId: "producer", port: "Done" }, target: { nodeId: "consumer" } }
      ] });
      const repeat = adapter.createActivityNode(whileActivity, "loop");
      repeat.inputs = [{ referenceKey: "Condition", value: argument("JavaScript", "getVariable('Counter') < 3") }];
      repeat.structure.payload.body = body;
      answer.inputs[1].value = argument("Variable", { referenceKey: "counter" });
      rootActivity = adapter.createActivityNode(fixtures.sequenceActivity, "root");
      rootActivity.structure.payload.activities = [repeat, answer];
      variables.push({ referenceKey: "counter", name: "Counter", type: { alias: "Int32", collectionKind: "single" }, storageDriverType: null, default: argument("Literal", 0) });
      const frames = adapter.findNodeScopePath(rootActivity, "producer", item => item.nodeId, catalog);
      if (!frames) throw new Error("The fixture's producer has no authored scope.");
      scope.push(...frames);
    }
    const document = { rootActivity, layout: [], activityPresentation: [] };
    const context = { document, catalog, scope, expressionScopePreservation, referenceContext: [variables], sourceKey: `fixture:${kind}:${profile}`, canEdit: true };
    const request = { nodeId: "block", displayName: "Producer block", activityVersionId: body.activityVersionId, selectedNodeIds: loop ? ["increment", "producer"] : ["producer"] };
    const plan = planner.planExtractNativeBlock(context, request);
    const applied = planner.applyNativeBlockPlan(context, plan);
    if (applied.status !== "ready") throw new Error(JSON.stringify(applied));
    const inverse = planner.planUngroupNativeBlock({ ...context, document: applied.document }, "block");
    if (inverse.status !== "ready") throw new Error(JSON.stringify(inverse));
    cases.push({ name: `${kind}-${profile}`, expected: loop ? "3" : parameter ? "43" : "42", loop, ownerId: body.nodeId, variables,
      context: { document, scope, referenceContext: [variables], sourceKey: context.sourceKey, canEdit: true }, request, grouped: applied.document, ungrouped: inverse.document });
  }
  const output = resolve(repository, "specs/1204-named-embedded-blocks/contracts/native-result-transfer-cases.json");
  const bytes = JSON.stringify({ formatVersion: 1, kind: "pure-planner-golden-documents-not-runtime-evidence", catalog, versions: {
    [fixtures.sequenceActivity.activityVersionId]: "Elsa.Activities.Sequence.Activities.Sequence",
    [fixtures.flowchartActivity.activityVersionId]: "Elsa.Activities.Flowchart.Activities.Flowchart",
    [whileActivity.activityVersionId]: whileActivity.activityTypeKey,
    "inline-version": "Elsa.Activities.Primitives.Activities.Inline"
  }, cases }, null, 2) + "\n";
  if (process.argv.includes("--check")) {
    if (await readFile(output, "utf8") !== bytes) throw new Error("Native result-transfer fixtures are stale. Regenerate and review them.");
  } else {
    await mkdir(dirname(output), { recursive: true });
    await writeFile(output, bytes);
  }
} finally { await server.close(); }
