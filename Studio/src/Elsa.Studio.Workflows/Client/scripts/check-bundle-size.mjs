import { readFile, stat } from "node:fs/promises";
import { gzipSync } from "node:zlib";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const packageRoot = resolve(dirname(fileURLToPath(import.meta.url)), "..");
const outputRoot = resolve(packageRoot, "../wwwroot/studio/modules/workflows");
const manifest = JSON.parse(await readFile(resolve(outputRoot, ".vite/manifest.json"), "utf8"));

// Budgets cover every Workflows asset the browser loads for the authenticated Definitions landing
// path. The former 677.67 kB JavaScript monolith is the independent split baseline. CSS is split by
// route so the staged-upgrade workbench has an independent measured budget and cannot increase the
// authenticated Workflow Definitions landing baseline.
const budgets = {
  entryJavaScript: 125_000,
  stylesheet: 185_000,
  definitionsLandingTotal: 350_000,
  upgradeLandingTotal: 338_000,
  individualChunk: 500_000
};

const deferredHeavySurfaces = [
  "ActivityUpgradeWorkbenchPage",
  "ActivityAvailabilityPage",
  "RuntimeDiagnosticsSettingsPage",
  "WorkflowDefinitions",
  "WorkflowEditor",
  "WorkflowExecutableInspector",
  "WorkflowExecutables",
  "WorkflowInstances"
];

const excludedFromDefinitionsLanding = deferredHeavySurfaces.filter(name => name !== "WorkflowDefinitions");

function collectStaticFiles(entryKey, files = new Set()) {
  const entry = manifest[entryKey];
  if (!entry) throw new Error(`Bundle manifest is missing ${entryKey}.`);

  files.add(entry.file);
  for (const cssFile of entry.css ?? []) files.add(cssFile);
  for (const importedKey of entry.imports ?? []) collectStaticFiles(importedKey, files);
  return files;
}

async function measure(files) {
  const result = { bytes: 0, gzipBytes: 0 };
  for (const file of files) {
    const content = await readFile(resolve(outputRoot, file));
    result.bytes += content.byteLength;
    result.gzipBytes += gzipSync(content).byteLength;
  }
  return result;
}

function formatBytes(bytes) {
  return `${(bytes / 1_000).toFixed(2)} kB`;
}

function assertBudget(label, actual, budget) {
  if (actual > budget) {
    throw new Error(`${label} is ${formatBytes(actual)}; budget is ${formatBytes(budget)}.`);
  }
}

function findChunk(name) {
  const chunk = Object.values(manifest).find(entry => entry.name === name);
  if (!chunk) throw new Error(`Bundle manifest is missing the ${name} chunk.`);
  return chunk;
}

for (const chunkName of deferredHeavySurfaces) {
  if (!findChunk(chunkName).isDynamicEntry) throw new Error(`${chunkName} is no longer emitted as a deferred entry.`);
}

const entryFiles = collectStaticFiles("src/module.tsx");
const definitionsLandingFiles = new Set(entryFiles);
collectStaticFiles("src/workflow-editor/pages.tsx", definitionsLandingFiles);
collectStaticFiles("src/workflow-editor/WorkflowDefinitions.tsx", definitionsLandingFiles);
const upgradeLandingFiles = new Set(entryFiles);
collectStaticFiles("src/ActivityUpgradeWorkbenchPage.tsx", upgradeLandingFiles);
const entryJavaScriptFiles = filesWithExtension(entryFiles, ".js");
const stylesheetFiles = filesWithExtension(entryFiles, ".css");
const definitionsLandingJavaScriptFiles = filesWithExtension(definitionsLandingFiles, ".js");

if (stylesheetFiles.size === 0) throw new Error("Bundle manifest is missing the Workflows entry stylesheet.");

for (const chunkName of excludedFromDefinitionsLanding) {
  const chunk = findChunk(chunkName);
  if (definitionsLandingFiles.has(chunk.file)) {
    throw new Error(`The authenticated Definitions landing path eagerly includes ${chunkName}.`);
  }
}

const [entrySize, stylesheetSize, definitionsLandingSize, definitionsLandingTotalSize, upgradeLandingTotalSize] = await Promise.all([
  measure(entryJavaScriptFiles),
  measure(stylesheetFiles),
  measure(definitionsLandingJavaScriptFiles),
  measure(definitionsLandingFiles),
  measure(upgradeLandingFiles)
]);

const jsFiles = [...new Set(Object.values(manifest).map(entry => entry.file).filter(file => file.endsWith(".js")))];
const jsSizes = await Promise.all(jsFiles.map(async file => ({ file, bytes: (await stat(resolve(outputRoot, file))).size })));
const largestChunk = jsSizes.reduce((largest, candidate) => candidate.bytes > largest.bytes ? candidate : largest);

assertBudget("Primary Workflows JavaScript entry", entrySize.bytes, budgets.entryJavaScript);
assertBudget("Workflows stylesheet", stylesheetSize.bytes, budgets.stylesheet);
assertBudget("Authenticated Definitions landing path", definitionsLandingTotalSize.bytes, budgets.definitionsLandingTotal);
assertBudget("Activity Definition upgrades landing path", upgradeLandingTotalSize.bytes, budgets.upgradeLandingTotal);
assertBudget(`Largest JavaScript chunk (${largestChunk.file})`, largestChunk.bytes, budgets.individualChunk);

console.log("Workflows bundle budget");
console.log(`  Primary JavaScript entry:          ${formatBytes(entrySize.bytes)} raw / ${formatBytes(entrySize.gzipBytes)} gzip (budget ${formatBytes(budgets.entryJavaScript)})`);
console.log(`  Workflows stylesheet:              ${formatBytes(stylesheetSize.bytes)} raw / ${formatBytes(stylesheetSize.gzipBytes)} gzip (budget ${formatBytes(budgets.stylesheet)})`);
console.log(`  Definitions JavaScript closure:    ${formatBytes(definitionsLandingSize.bytes)} raw / ${formatBytes(definitionsLandingSize.gzipBytes)} gzip`);
console.log(`  Authenticated Definitions total:   ${formatBytes(definitionsLandingTotalSize.bytes)} raw / ${formatBytes(definitionsLandingTotalSize.gzipBytes)} gzip (budget ${formatBytes(budgets.definitionsLandingTotal)})`);
console.log(`  Activity upgrades total:           ${formatBytes(upgradeLandingTotalSize.bytes)} raw / ${formatBytes(upgradeLandingTotalSize.gzipBytes)} gzip (budget ${formatBytes(budgets.upgradeLandingTotal)})`);
console.log(`  Largest JavaScript chunk:          ${formatBytes(largestChunk.bytes)} (${largestChunk.file}; limit ${formatBytes(budgets.individualChunk)})`);
console.log(`  Deferred heavy surfaces:           ${deferredHeavySurfaces.length}`);

function filesWithExtension(files, extension) {
  return new Set([...files].filter(file => file.endsWith(extension)));
}
