import fs from "node:fs";
import path from "node:path";
import { createRequire } from "node:module";
import { fileURLToPath } from "node:url";

// Refresh after a reviewed lockfile upgrade. Vite copies public/ into both module and consumer builds.
const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const seen = new Set();
let notice = "Third-party notices for the optional bpmn-js editor\n\n"
  + "The bpmn.io watermark must remain visible and unobstructed. Its source has not been modified.\n\n";

function manifestFor(name, require) {
  try { return require.resolve(`${name}/package.json`); }
  catch (error) {
    if (error.code !== "ERR_PACKAGE_PATH_NOT_EXPORTED") throw error;
    let directory = path.dirname(require.resolve(name));
    for (;;) {
      const candidate = path.join(directory, "package.json");
      if (fs.existsSync(candidate) && JSON.parse(fs.readFileSync(candidate, "utf8")).name === name) return candidate;
      const parent = path.dirname(directory);
      if (parent === directory) throw new Error(`Cannot locate package manifest for ${name}.`);
      directory = parent;
    }
  }
}

function collect(name, require) {
  const manifest = manifestFor(name, require);
  const pkg = JSON.parse(fs.readFileSync(manifest, "utf8"));
  const id = `${pkg.name}@${pkg.version}`;
  if (seen.has(id)) return;
  seen.add(id);
  const directory = path.dirname(manifest);
  const files = fs.readdirSync(directory).filter(name => /^(licen[sc]e|copying|notice)(\.|$)/i.test(name)
    && fs.statSync(path.join(directory, name)).isFile()).sort();
  if (!files.length) throw new Error(`Missing license text for ${id}.`);
  notice += `${"=".repeat(72)}\n${id} (${pkg.license})\n`
    + `${typeof pkg.repository === "string" ? pkg.repository : pkg.repository?.url ?? ""}\n\n`;
  for (const file of files) notice += `${file}\n${fs.readFileSync(path.join(directory, file), "utf8")}\n\n`;
  const nested = createRequire(manifest);
  for (const dependency of Object.keys(pkg.dependencies ?? {}).sort()) collect(dependency, nested);
}

collect("bpmn-js", createRequire(path.join(root, "package.json")));
fs.mkdirSync(path.join(root, "public"), { recursive: true });
const output = `${notice.trimEnd()}\n`;
fs.writeFileSync(path.join(root, "public/THIRD_PARTY_NOTICES.txt"), output);
console.log(`Retained notices for ${seen.size} pinned runtime packages (${Buffer.byteLength(output)} bytes).`);
