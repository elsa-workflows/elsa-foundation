import { createHash } from "node:crypto";
import { accessSync, constants, readFileSync, realpathSync } from "node:fs";
import { delimiter, join, resolve } from "node:path";
import { runOwnedCommand } from "./ui-owned-command.mjs";

const repo = resolve(import.meta.dirname, "../../..");
function executable(path) {
  const realPath = realpathSync(path);
  return { path, realPath, sha256: createHash("sha256").update(readFileSync(realPath)).digest("hex") };
}
export function fromPath(name) {
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
  const observe = async args => {
    const result = await runOwnedCommand(dotnet.path, args, { cwd: repo, timeoutMs: Math.min(10_000, deadline - Date.now()),
      env: { ...process.env, DOTNET_CLI_UI_LANGUAGE: "en-US" } });
    return result.output.trim();
  };
  const sdkVersion = await observe(["--version"]); const info = await observe(["--info"]);
  const sdkBasePath = info.match(/^\s*Base Path:\s*(.+)$/mu)?.[1]?.trim();
  const hostVersion = info.match(/^Host:\s*\n\s*Version:\s*(\S+)/mu)?.[1];
  if (!/^\d+\.\d+\.\d+$/u.test(sdkVersion) || !sdkBasePath || !hostVersion)
    throw new Error("The actual dotnet host/resolved SDK identity could not be established.");
  const sdk = { version: sdkVersion, basePath: realpathSync(sdkBasePath), entry: executable(join(sdkBasePath, "dotnet.dll")),
    msbuild: executable(join(sdkBasePath, "MSBuild.dll")) };
  if (sdk.basePath.split(/[\\/]/u).at(-1) !== sdkVersion) throw new Error("The resolved SDK path and version disagree.");
  const host = { ...executable(resolve(sdkBasePath, "../..", "dotnet")), version: hostVersion };
  return { node, dotnet: { launcher: dotnet, host, sdk } };
}
export function assertToolchain(expected, actual) {
  if (!expected?.node?.sha256 || !expected?.dotnet?.sdk?.entry?.sha256 || JSON.stringify(expected) !== JSON.stringify(actual))
    throw new Error("Qualification tools differ from the frozen executable/version/SDK pin.");
}
