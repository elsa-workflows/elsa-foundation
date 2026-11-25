import { createHash } from "node:crypto";
import { existsSync, lstatSync, readFileSync, readdirSync, realpathSync } from "node:fs";
import { dirname, extname, isAbsolute, join, relative, resolve, sep } from "node:path";

const sdk = "Studio/src/essentials/Elsa.Studio.Hosting/Client/src/sdk/";
export const authoritySources = [sdk + "jsonHashInput.ts", sdk + "sha256.ts", sdk + "index.ts",
  "Studio/src/essentials/Elsa.Studio.Workflows/Client/src/sha256.ts"];
const authorityOwners = new Set([sdk + "jsonHashInput.ts::hashJson", sdk + "jsonHashInput.ts::hashJsonText",
  sdk + "jsonHashInput.ts::hashJsonTextAsync", sdk + "sha256.ts::sha256Hex"]);
const classifications = new Set(["random_identity", "random_nonce", "json_serialization_nonce", "json_routed",
  "shared_json_authority", "shared_sha_implementation", "non_json_identifier_hash"]);
export const digest = value => createHash("sha256").update(JSON.stringify(value)).digest("hex");
const slash = path => path.split(sep).join("/");
const ordinal = (a, b) => a < b ? -1 : a > b ? 1 : 0;
const textHash = (file, exact = false) => {
  const bytes = readFileSync(file);
  const semanticText = /\.[cm]?[jt]sx?$|\.html?$|[\\/](?:package|tsconfig[^\\/]*)\.json$|[\\/]pnpm-(?:lock|workspace)\.yaml$|[\\/]\.npmrc$/.test(file);
  const value = !exact && semanticText ? new TextDecoder("utf-8", { fatal: true }).decode(bytes).replace(/\r\n/g, "\n") : bytes;
  return createHash("sha256").update(value).digest("hex");
};
const beneath = (root, file) => { const path = relative(root, file); return path !== ".." && !path.startsWith(".." + sep) && !isAbsolute(path); };
const reviewRegisterPath = "Studio/scripts/baselines/json-hash-review.json";
const goldenTextExtensions = new Set([".cs", ".json", ".jsonc", ".py", ".md", ".txt", ".ts", ".tsx", ".js", ".jsx",
  ".mjs", ".cjs", ".mts", ".cts", ".xml", ".props", ".targets", ".csproj", ".yml", ".yaml"]);

function goldenEvidenceDigest(file) {
  const bytes = readFileSync(file);
  if (!bytes.length) throw new Error("Golden evidence file is empty.");
  if (!goldenTextExtensions.has(extname(file).toLowerCase()))
    return "binary:" + createHash("sha256").update(bytes).digest("hex");
  let text;
  if (bytes.subarray(0, 4).equals(Buffer.from([0xff, 0xfe, 0, 0])) || bytes.subarray(0, 4).equals(Buffer.from([0, 0, 0xfe, 0xff]))) {
    const little = bytes[0] === 0xff;
    if ((bytes.length - 4) % 4) throw new Error("Invalid UTF-32 golden evidence.");
    const characters = [];
    for (let index = 4; index < bytes.length; index += 4) {
      const value = little ? bytes.readUInt32LE(index) : bytes.readUInt32BE(index);
      if (value > 0x10ffff || value >= 0xd800 && value <= 0xdfff) throw new Error("Invalid UTF-32 golden evidence scalar.");
      characters.push(String.fromCodePoint(value));
    }
    text = characters.join("");
  } else {
    let encoding = "utf-8", offset = 0;
    if (bytes.subarray(0, 2).equals(Buffer.from([0xff, 0xfe]))) { encoding = "utf-16le"; offset = 2; }
    else if (bytes.subarray(0, 2).equals(Buffer.from([0xfe, 0xff]))) { encoding = "utf-16be"; offset = 2; }
    else if (bytes.subarray(0, 3).equals(Buffer.from([0xef, 0xbb, 0xbf]))) offset = 3;
    // Strip only the initial encoding marker; a second U+FEFF is actual file content.
    text = new TextDecoder(encoding, { fatal: true, ignoreBOM: true }).decode(bytes.subarray(offset));
  }
  if (!text.length) throw new Error("Decoded golden evidence text is empty.");
  return "text:" + createHash("sha256").update(text.replace(/\r\n/g, "\n"), "utf8").digest("hex");
}

/** Bind complete reviewed proof files and their exact owner mappings, independently of shipping source selection. */
export function readJsonHashGoldenEvidence(repoRoot, owners) {
  const findings = [], files = new Map(), bindings = [], identities = new Set();
  const root = resolve(repoRoot), register = resolve(root, reviewRegisterPath);
  const comparable = file => process.platform === "win32" ? file.toLowerCase() : file;
  if (!Array.isArray(owners)) return { signature: "", findings: ["TypeScript golden evidence needs a reviewed owner list."] };
  for (const owner of owners) {
    if (typeof owner.identity !== "string" || !owner.identity.trim() || identities.has(owner.identity)) {
      findings.push("Invalid/duplicate TypeScript golden evidence owner: " + owner.identity);
      continue;
    }
    identities.add(owner.identity);
    if (owner.goldenEvidence !== undefined && !Array.isArray(owner.goldenEvidence)) {
      findings.push("TypeScript golden evidence must be a file list: " + owner.identity);
      continue;
    }
    const evidenceFiles = owner.goldenEvidence ?? [], selected = new Set(), physical = new Set();
    if (["json_routed", "shared_json_authority", "shared_sha_implementation", "non_json_identifier_hash"].includes(owner.classification) && !evidenceFiles.length)
      findings.push("Missing TypeScript hash golden evidence: " + owner.identity);
    for (const evidence of evidenceFiles) {
      try {
        if (typeof evidence !== "string" || !evidence.trim() || evidence !== evidence.trim() || evidence.includes(":") || evidence.includes("\0") || isAbsolute(evidence))
          throw new Error("Golden evidence must be a relative repository file.");
        const portable = evidence.replace(/\\/g, "/");
        if (portable.split("/").some(part => !part || part === "." || part === ".."))
          throw new Error("Golden evidence cannot contain traversal or path aliases.");
        const path = resolve(root, portable);
        if (!beneath(root, path)) throw new Error("Golden evidence escapes the repository.");
        if (comparable(path) === comparable(register)) throw new Error("The hash review register cannot be its own golden evidence.");
        const item = lstatSync(path);
        if (!item.isFile()) throw new Error("Golden evidence must be an ordinary file.");
        // Refuse links even when they currently resolve inside the checkout, including linked directories/junctions.
        for (let current = path; ; current = dirname(current)) {
          if (lstatSync(current).isSymbolicLink()) throw new Error("Golden evidence has a linked ancestor.");
          if (current === dirname(current)) break;
        }
        const real = realpathSync(path);
        if (!beneath(realpathSync(root), real)) throw new Error("Golden evidence escaped the physical repository.");
        if (selected.has(portable) || physical.has(comparable(real))) throw new Error("Duplicate golden evidence reference.");
        selected.add(portable); physical.add(comparable(real));
        if (!files.has(portable)) files.set(portable, goldenEvidenceDigest(path));
      } catch (error) {
        findings.push(`Invalid TypeScript golden evidence for ${owner.identity}: ${String(evidence)}: ${error.message}`);
      }
    }
    bindings.push({ owner: owner.identity, files: [...selected].sort(ordinal) });
  }
  return { signature: findings.length ? "" : digest({ bindings: bindings.sort((a, b) => ordinal(a.owner, b.owner)),
    files: [...files].sort(([a], [b]) => ordinal(a, b)).map(([path, contentDigest]) => ({ path, contentDigest })) }), findings };
}

export function shippingTypeScriptSources(repoRoot) {
  const clients = [], files = [];
  function findClients(folder) {
    for (const entry of readdirSync(folder, { withFileTypes: true })) {
      if (!entry.isDirectory() || ["node_modules", "bin", "obj", ".git"].includes(entry.name)) continue;
      const path = join(folder, entry.name);
      if (entry.name === "Client" && existsSync(join(path, "src"))) clients.push(path);
      else findClients(path);
    }
  }
  findClients(join(repoRoot, "Studio/src"));
  for (const entry of readdirSync(join(repoRoot, "extensions"), { withFileTypes: true })) {
    const studio = join(repoRoot, "extensions", entry.name, "studio");
    if (entry.isDirectory() && existsSync(studio)) findClients(studio);
  }
  function source(folder) {
    for (const entry of readdirSync(folder, { withFileTypes: true })) {
      const path = join(folder, entry.name);
      if (entry.isDirectory()) { if (!["node_modules", "__tests__"].includes(entry.name)) source(path); }
      else if (/\.[cm]?[jt]sx?$/.test(entry.name) && !/\.(test|spec)\.[cm]?[jt]sx?$/.test(entry.name)) files.push(path);
    }
  }
  clients.forEach(client => source(join(client, "src")));
  return { clients: clients.sort(), rootNames: files.sort() };
}

export function sourceDocument(repoRoot, file, exact = false) {
  file = resolve(file);
  if (file.split(sep).includes("node_modules")) {
    const parts = file.split(sep), index = parts.lastIndexOf("node_modules");
    const boundary = parts.slice(0, index + (parts[index + 1].startsWith("@") ? 3 : 2)).join(sep);
    const scopes = [];
    let folder = dirname(file);
    while (folder !== dirname(folder)) {
      const manifest = join(folder, "package.json");
      if (existsSync(manifest)) {
        const pkg = JSON.parse(readFileSync(manifest, "utf8"));
        if (pkg.name && pkg.version)
          return { path: `package:${pkg.name}@${pkg.version}/${slash(relative(folder, file))}`, sha256: textHash(file, exact), packageSha256: textHash(manifest),
            scopes: scopes.map(scope => ({ path: slash(relative(folder, scope)), sha256: textHash(scope) })) };
        scopes.push(manifest);
      }
      if (folder === boundary) break;
      folder = dirname(folder);
    }
    throw new Error("External hash contract has no owning package: " + file);
  }
  if (!beneath(repoRoot, file) || !beneath(realpathSync(repoRoot), realpathSync(file))) throw new Error("Hash source escaped the checkout: " + file);
  return { path: slash(relative(repoRoot, file)), sha256: textHash(file, exact), ...(exact ? { exactBytes: true } : {}) };
}

export function ownerSites(references) {
  const counts = new Map();
  return references.map(({ role, kind, target }) => {
    // A dependency can be restored at a different workspace depth. Its exact bytes,
    // package identity and lockfile remain authenticated by the source receipt.
    target = target.replace(/^.*node_modules\/(?:\.pnpm\/[^/]+\/node_modules\/)?(typescript\/lib\/[^:]+)::/, "$1::");
    const key = JSON.stringify({ role, kind, target });
    const occurrence = (counts.get(key) ?? 0) + 1;
    counts.set(key, occurrence);
    return { role, kind, target, occurrence };
  }).sort((a, b) => ordinal(JSON.stringify(a), JSON.stringify(b)));
}

export function createJsonHashReceipt({ repoRoot, rootNames, sourceFiles, references, moduleEdges, errors, settings = [], physicalFiles = [], authorities = authoritySources }) {
  if (errors.length) throw new Error("Incomplete TypeScript hash analysis: " + JSON.stringify(errors));
  const documents = new Map();
  const physical = new Set(physicalFiles.map(file => resolve(file)));
  for (const file of new Set([...rootNames, ...sourceFiles, ...settings, ...physical])) {
    const document = sourceDocument(repoRoot, file, physical.has(resolve(file)));
    const previous = documents.get(document.path);
    if (previous && JSON.stringify(previous) !== JSON.stringify(document)) throw new Error("Ambiguous hash contract source: " + document.path);
    documents.set(document.path, document);
  }
  const sources = [...documents.values()].sort((a, b) => a.path < b.path ? -1 : a.path > b.path ? 1 : 0);
  for (const authority of authorities) {
    if (!documents.has(authority)) throw new Error("Missing exact hash implementation/barrel source: " + authority);
  }
  const groups = new Map();
  for (const reference of references) {
    const owner = reference.file + "::" + reference.owner;
    if (!groups.has(owner)) groups.set(owner, []);
    groups.get(owner).push(reference);
  }
  const owners = [...groups].map(([identity, rows]) => ({ identity, sites: ownerSites(rows) })).sort((a, b) => a.identity < b.identity ? -1 : a.identity > b.identity ? 1 : 0);
  return { formatVersion: 1, sourceSignature: digest(sources), rootSignature: digest(rootNames.map(file => slash(relative(repoRoot, file))).sort()),
    authoritySignature: digest(authorities.map(path => documents.get(path))),
    moduleSignature: digest(moduleEdges.map(edge => ({ ...edge, target: ownerSites([{ target: edge.target }])[0].target })).sort((a, b) => ordinal(JSON.stringify(a), JSON.stringify(b)))),
    sourceCount: sources.length, rootCount: rootNames.length, owners, sources };
}

export function validateJsonHashReview(receipt, register, repoRoot) {
  const findings = [];
  for (const name of ["formatVersion", "sourceSignature", "rootSignature", "authoritySignature", "moduleSignature"])
    if (receipt[name] !== register[name]) findings.push("Changed TypeScript hash receipt: " + name);
  const reviewed = new Map();
  for (const owner of register.owners ?? []) {
    if (reviewed.has(owner.identity)) findings.push("Duplicate reviewed TypeScript hash owner: " + owner.identity);
    reviewed.set(owner.identity, owner);
  }
  for (const owner of receipt.owners) {
    const entry = reviewed.get(owner.identity);
    if (!entry) { findings.push("Unreviewed TypeScript hash/crypto owner: " + owner.identity); continue; }
    reviewed.delete(owner.identity);
    if (!classifications.has(entry.classification) || !entry.reason?.trim()) findings.push("Missing exact TypeScript hash classification/reason: " + owner.identity);
    if (JSON.stringify(owner.sites) !== JSON.stringify(entry.sites)) findings.push("Changed TypeScript hash references: " + owner.identity);
    const primitive = owner.sites.some(site => site.role === "digest-primitive");
    if (primitive && !["shared_json_authority", "shared_sha_implementation", "non_json_identifier_hash"].includes(entry.classification))
      findings.push("A TypeScript primitive cannot borrow a JSON/random-identity exemption: " + owner.identity);
    if (["shared_json_authority", "shared_sha_implementation"].includes(entry.classification) && !authorityOwners.has(owner.identity))
      findings.push("A TypeScript owner cannot claim hash authority: " + owner.identity);
    const json = owner.sites.some(site => site.role === "json-hash-input");
    if (json && entry.classification !== "json_routed") findings.push("JSON hash routing was misclassified: " + owner.identity);
    if ((json || primitive) && !entry.goldenEvidence?.length) findings.push("Missing TypeScript hash golden evidence: " + owner.identity);
  }
  for (const owner of reviewed.keys()) findings.push("Removed TypeScript hash owner needs review: " + owner);
  const goldens = readJsonHashGoldenEvidence(repoRoot, register.owners);
  findings.push(...goldens.findings);
  if (typeof register.goldenSignature !== "string" || !register.goldenSignature || register.goldenSignature !== goldens.signature)
    findings.push("Changed or missing TypeScript goldenSignature: review the complete actual-owner proof files and mappings before updating it.");
  return findings;
}
