import ts from "typescript";
import { existsSync, readFileSync, writeFileSync } from "node:fs";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { scanJsonHashReferences } from "./lib/jsonHashReferences.mjs";
import { authoritySources, createJsonHashReceipt, shippingTypeScriptSources, validateJsonHashReview } from "./lib/jsonHashReceipt.mjs";
import { physicalModuleInputs, studioHashBuildInputs } from "./lib/jsonHashBuildInputs.mjs";

const repoRoot = resolve(dirname(fileURLToPath(import.meta.url)), "../..");
const baseline = join(repoRoot, "Studio/scripts/baselines/json-hash-review.json");
const args = process.argv.slice(2);
if (args.length && (args.length !== 2 || args[0] !== "--observe")) throw new Error("Usage: node Studio/scripts/check-json-hashes.mjs [--observe <new-report.json>]");
const config = ts.readConfigFile(join(repoRoot, "Studio/tsconfig.base.json"), ts.sys.readFile);
if (config.error) throw new Error(ts.flattenDiagnosticMessageText(config.error.messageText, " "));
const converted = ts.convertCompilerOptionsFromJson(config.config.compilerOptions, join(repoRoot, "Studio"));
if (converted.errors.length) throw new Error("TypeScript hash compiler options could not be resolved.");
const { clients, rootNames } = shippingTypeScriptSources(repoRoot);
const result = scanJsonHashReferences({ repoRoot, rootNames, options: converted.options,
  gateways: [{ file: join(repoRoot, authoritySources[0]), exports: ["hashJson", "hashJsonText", "hashJsonTextAsync"] }],
  primitives: [{ file: join(repoRoot, authoritySources[1]), exports: ["sha256", "sha256Hex"] }], maxResolutionSteps: 5_000_000 });
const settings = studioHashBuildInputs(repoRoot, clients);
const physicalFiles = physicalModuleInputs(repoRoot, [...result.sourceFiles, ...settings]);
const receipt = createJsonHashReceipt({ repoRoot, rootNames, settings, physicalFiles, ...result });
const register = JSON.parse(readFileSync(baseline, "utf8"));
const findings = validateJsonHashReview(receipt, register, repoRoot);
if (args.length) {
  const output = resolve(args[1]);
  if (existsSync(output)) throw new Error("Preserve the previous hash report; use a new output path.");
  const reviewed = new Map(register.owners.map(owner => [owner.identity, owner]));
  const observed = { ...receipt, owners: receipt.owners.map(owner => ({
    ...owner, classification: reviewed.get(owner.identity)?.classification ?? "unreviewed",
    reason: reviewed.get(owner.identity)?.reason ?? "", goldenEvidence: reviewed.get(owner.identity)?.goldenEvidence ?? []
  })), findings, analysis: { clients: clients.length, references: result.references.length, moduleEdges: result.moduleEdges.length } };
  writeFileSync(output, JSON.stringify(observed, null, 2) + "\n");
}
if (findings.length) {
  for (const finding of findings) console.error(finding);
  process.exitCode = 1;
} else console.log(`TypeScript JSON hashes: ${receipt.owners.length} reviewed owners, ${result.references.length} resolved references, ${receipt.sourceCount} authenticated sources; first-stable hash freeze applies.`);
