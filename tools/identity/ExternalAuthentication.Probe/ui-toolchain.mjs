import { createHash } from "node:crypto";
import { accessSync, constants, readFileSync, realpathSync } from "node:fs";
import { delimiter, isAbsolute, join, resolve } from "node:path";
import { runOwnedCommand } from "./ui-owned-command.mjs";

const repo = resolve(import.meta.dirname, "../../..");
function executable(path) {
  const realPath = realpathSync(path);
  return { path, realPath, sha256: createHash("sha256").update(readFileSync(realPath)).digest("hex") };
}
function fromPath(name) {
  for (const directory of (process.env.PATH ?? "").split(delimiter).filter(Boolean)) {
    const path = resolve(directory, name);
    try { accessSync(path, constants.X_OK); return executable(path); } catch { /* continue through the ordinary PATH */ }
  }
  throw new Error("A required qualification executable is absent from PATH.");
}
export async function captureToolchain(deadline = Date.now() + 30_000) {
  const node = { ...executable(process.execPath), version: process.version };
  const nodeLauncher = fromPath("node");
  if (nodeLauncher.realPath !== node.realPath) throw new Error("PATH Node differs from the actual qualification process.");
  node.pathLookup = { realPath: nodeLauncher.realPath, sha256: nodeLauncher.sha256 };
  const dotnet = fromPath("dotnet");
  const observe = async (path, args) => {
    const result = await runOwnedCommand(path, args, { cwd: repo, timeoutMs: Math.min(10_000, deadline - Date.now()),
      env: { ...process.env, DOTNET_CLI_UI_LANGUAGE: "en-US" } });
    return result.output.trim();
  };
  const sdkVersion = await observe(dotnet.path, ["--version"]); const info = await observe(dotnet.path, ["--info"]);
  const sdkBasePath = info.match(/^\s*Base Path:\s*(.+)$/mu)?.[1]?.trim();
  const hostVersion = info.match(/^Host:\s*\n\s*Version:\s*(\S+)/mu)?.[1];
  if (!/^\d+\.\d+\.\d+$/u.test(sdkVersion) || !sdkBasePath || !hostVersion)
    throw new Error("The actual dotnet host/resolved SDK identity could not be established.");
  const sdk = { version: sdkVersion, basePath: realpathSync(sdkBasePath), entry: executable(join(sdkBasePath, "dotnet.dll")),
    msbuild: executable(join(sdkBasePath, "MSBuild.dll")) };
  if (sdk.basePath.split(/[\\/]/u).at(-1) !== sdkVersion) throw new Error("The resolved SDK path and version disagree.");
  const host = { ...executable(resolve(sdkBasePath, "../..", "dotnet")), version: hostVersion };
  const python = fromPath("python3");
  python.version = (await observe(python.path, ["--version"])).match(/^Python (\S+)$/u)?.[1];
  if (!python.version) throw new Error("The actual Studio producer Python version could not be established.");
  return { node, dotnet: { launcher: dotnet, host, sdk }, python };
}
const nonempty = value => typeof value === "string" && value.length > 0 && value.length <= 4096 && !/[\0\r\n]/u.test(value);
export const absoluteToolPath = value => nonempty(value) && isAbsolute(value);
export function isExecutableIdentity(value) {
  return value !== null && typeof value === "object" && !Array.isArray(value) && absoluteToolPath(value.path) &&
    absoluteToolPath(value.realPath) && typeof value.sha256 === "string" && /^[0-9a-f]{64}$/u.test(value.sha256);
}
function validToolchain(value) {
  return isExecutableIdentity(value?.node) && nonempty(value.node.version) && isExecutableIdentity(value?.python) && nonempty(value.python.version) &&
    isExecutableIdentity(value?.dotnet?.launcher) && isExecutableIdentity(value?.dotnet?.host) && nonempty(value.dotnet.host.version) &&
    nonempty(value?.dotnet?.sdk?.version) && absoluteToolPath(value.dotnet.sdk.basePath) && isExecutableIdentity(value.dotnet.sdk.entry) && isExecutableIdentity(value.dotnet.sdk.msbuild);
}
export function assertToolchain(expected, actual) {
  if (!validToolchain(expected) || !validToolchain(actual) || JSON.stringify(expected) !== JSON.stringify(actual))
    throw new Error("Qualification tools differ from the frozen executable/version/SDK pin.");
}
