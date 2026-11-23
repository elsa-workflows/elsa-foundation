#!/usr/bin/env node
// Produce executable qualification inputs using the production planner, without a test-only transform.
import { createHash } from "node:crypto";
import { readFile, writeFile } from "node:fs/promises";
import { createRequire } from "node:module";
import { dirname, resolve } from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";
import { execFileSync } from "node:child_process";
import process from "node:process";
import { log } from "node:console";

const repository = resolve(dirname(fileURLToPath(import.meta.url)), "../..");
const [inputPath, outputPath] = process.argv.slice(2);
if (!inputPath || !outputPath || process.argv.length !== 4) throw new Error("Usage: node tools/blocks/plan_native_cases.mjs INPUT.json OUTPUT.json");
const git = (...args) => execFileSync("git", args, { cwd: repository, encoding: "utf8" }).trim();
const source = git("rev-parse", "HEAD");
if (git("status", "--porcelain")) throw new Error("Commit the complete source before qualification");
const inputBytes = await readFile(resolve(inputPath));
const input = JSON.parse(inputBytes);
if (input.source !== source || !Array.isArray(input.catalog) || !Array.isArray(input.cases) || !input.cases.length) throw new Error("Expected cases and the actual catalog from the current committed source");
const require = createRequire(resolve(repository, "Studio/package.json"));
const { createServer } = await import(pathToFileURL(require.resolve("vite")).href);
const client = resolve(repository, "Studio/src/essentials/Elsa.Studio.Workflows/Client");
const plannerPath = resolve(client, "src/graph-authoring/nativeBlocks.ts");
const server = await createServer({ root: client, configFile: false, optimizeDeps: { noDiscovery: true }, server: { middlewareMode: true }, appType: "custom", logLevel: "error" });
const sha256 = bytes => createHash("sha256").update(bytes).digest("hex");
try {
  const planner = await server.ssrLoadModule(plannerPath);
  const names = new Set();
  const cases = input.cases.map(test => {
    if (typeof test.name !== "string" || !test.name || names.has(test.name)) throw new Error("Case names must be unique and nonempty");
    names.add(test.name);
    if (test.operation !== undefined && !["create", "extract"].includes(test.operation)) throw new Error(`${test.name}: unknown operation`);
    const context = { document: test.document, catalog: input.catalog, scope: test.scope ?? [], referenceContext: test.referenceContext ?? [test.variables ?? []], sourceKey: `${source}/${test.name}`, canEdit: true };
    const plan = test.operation === "create" ? planner.planCreateNativeBlock(context, test.request) : planner.planExtractNativeBlock(context, test.request);
    const applied = planner.applyNativeBlockPlan(context, plan);
    if (test.refusal) {
      if (applied.status !== "refused" || !applied.diagnostics.some(x => x.code === test.refusal)) throw new Error(`${test.name}: expected ${test.refusal}; got ${JSON.stringify(applied)}`);
      return { name: test.name, baseline: test.document, refusal: applied.diagnostics };
    }
    if (applied.status !== "ready") throw new Error(`${test.name}: ${JSON.stringify(applied.diagnostics)}`);
    const ungroup = planner.planUngroupNativeBlock({ ...context, document: applied.document }, test.request.nodeId);
    const inverse = planner.applyNativeBlockPlan({ ...context, document: applied.document }, ungroup);
    if (inverse.status !== "ready") throw new Error(`${test.name}: inverse ${JSON.stringify(inverse.diagnostics)}`);
    return { name: test.name, baseline: test.document, candidate: applied.document, ungrouped: inverse.document, selection: applied.selection };
  });
  if (git("rev-parse", "HEAD") !== source || git("status", "--porcelain")) throw new Error("Source changed during planning");
  const result = { source, inputSha256: sha256(inputBytes), plannerSha256: sha256(await readFile(plannerPath)), cases };
  await writeFile(resolve(outputPath), JSON.stringify(result, null, 2) + "\n", { flag: "wx" });
  log(JSON.stringify({ source, output: resolve(outputPath), planned: cases.filter(x => x.candidate).length, refused: cases.filter(x => x.refusal).length }));
} finally {
  await server.close();
}
