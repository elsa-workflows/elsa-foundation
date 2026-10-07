import assert from "node:assert/strict";
import { execFileSync, spawn } from "node:child_process";
import { createHash } from "node:crypto";
import { mkdtemp, readFile, rm, writeFile } from "node:fs/promises";
import net from "node:net";
import os from "node:os";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { chromium } from "playwright";

const images = {
  workbench: {
    ref: "elsaworkflows/elsa-workbench@sha256:73ef26e41760eaa4618a37cfbd7285002408f634e4089201b3864cb1f6147904",
    amd64: "sha256:e56e093b73ea239c7833107719e23bf1d03a4faaf07d1b704a8523ac88461e46",
    source: "592d6c0eb2a1ae45563ce5b234fb4e97af2867ed",
    repository: "elsa-foundation"
  },
  studio: {
    ref: "elsaworkflows/elsa-studio@sha256:1a4401d2f5bee207c3edbc51d4157ecb23ba796fb1dacc9e5ff6775f6c4f6ad0",
    amd64: "sha256:d151aabcc161474ba5a7d18d9979c65f381571bfb2e96d1840bfc0b99e8bb647",
    source: "7cb2f2c2381411694c470cdb9179402571bcf832",
    repository: "elsa-foundation-studio"
  }
};

const ports = { workbench: 13000, studio: 14000 };
const workbenchUrl = `http://localhost:${ports.workbench}`;
const studioUrl = `http://localhost:${ports.studio}`;
const repoRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "../..");
const runId = process.env.GITHUB_RUN_ID;
const runAttempt = process.env.GITHUB_RUN_ATTEMPT;
const projectName = `elsa-pair-${runId}-${runAttempt}`;
const expectedOutput = `Published image pair proof ${runId}-${runAttempt}`;
const workflowName = `Published image pair ${runId}-${runAttempt}`;

let stage = "runner-preflight";
let tempDir;
let composeFile;
let browser;
let cleanupError;
let ownershipApproved = false;
let ownedWorkbenchContainerId;

function log(message) {
  console.log(`[published-pair] ${message}`);
}

function safeFailureClass(error) {
  const name = error instanceof Error ? error.name : "";
  if (name === "TimeoutError") return "timeout";
  if (name === "AssertionError") return "assertion";
  if (name === "Error" && error.message.includes("strict mode violation")) return "ambiguous-locator";
  if (name === "Error" && /interrupted by another navigation|ERR_ABORTED/.test(error.message)) return "navigation-interrupted";
  if (name === "Error") return "error";
  return "other";
}

function commandOutput(command, args, options = {}) {
  try {
    return execFileSync(command, args, {
      cwd: repoRoot,
      encoding: "utf8",
      stdio: ["ignore", "pipe", "pipe"],
      maxBuffer: 8 * 1024 * 1024,
      ...options
    }).trim();
  } catch (error) {
    throw new Error(`${command} ${args[0] ?? ""} failed (${error.status ?? "no exit status"})`);
  }
}

function commandQuiet(command, args) {
  try {
    execFileSync(command, args, { cwd: repoRoot, stdio: "ignore" });
  } catch (error) {
    throw new Error(`${command} ${args[0] ?? ""} failed (${error.status ?? "no exit status"})`);
  }
}

function commandCaptured(command, args) {
  return new Promise((resolve, reject) => {
    const child = spawn(command, args, { cwd: repoRoot, stdio: ["ignore", "pipe", "pipe"] });
    let stdout = "";
    let stderr = "";
    child.stdout.setEncoding("utf8").on("data", chunk => { stdout += chunk; });
    child.stderr.setEncoding("utf8").on("data", chunk => { stderr += chunk; });
    child.once("error", () => reject(new Error(`${command} ${args[0] ?? ""} could not be started`)));
    child.once("close", code => {
      code === 0
        ? resolve(`${stdout}\n${stderr}`)
        : reject(new Error(`${command} ${args[0] ?? ""} failed (${code ?? "no exit status"})`));
    });
  });
}

function imageWasPresent(ref) {
  try {
    execFileSync("docker", ["image", "inspect", ref], { cwd: repoRoot, stdio: "ignore" });
    return true;
  } catch {
    return false;
  }
}

function summarizePullOutput(output) {
  const cleaned = output.replace(/\u001b\[[0-9;]*m/g, "");
  const outcome = /Downloaded newer image/.test(cleaned)
    ? "downloaded newer image"
    : /Image is up to date/.test(cleaned)
      ? "already up to date"
      : /\bPulled\b/.test(cleaned)
        ? "pulled"
        : "successful; outcome not reported";
  const allowedStatuses = ["Pulling fs layer", "Waiting", "Downloading", "Verifying Checksum", "Download complete", "Extracting", "Pull complete", "Already exists"];
  const counts = Object.fromEntries(allowedStatuses.map(status => [status, 0]));
  for (const line of cleaned.split(/[\r\n]+/)) {
    const match = line.match(/^\s*[a-f0-9]{12}: (Pulling fs layer|Waiting|Downloading|Verifying Checksum|Download complete|Extracting|Pull complete|Already exists)\s*$/i);
    if (match) {
      const status = allowedStatuses.find(candidate => candidate.toLowerCase() === match[1].toLowerCase());
      counts[status]++;
    }
  }
  const layerSummary = allowedStatuses.filter(status => counts[status]).map(status => `${status}=${counts[status]}`).join(", ") || "not reported";
  return { outcome, layerSummary };
}

function replaceExactlyOnce(source, before, after, label) {
  const count = source.split(before).length - 1;
  assert.equal(count, 1, `canonical Compose ${label} changed; review it before using this proof`);
  return source.replace(before, after);
}

async function assertPortFree(port) {
  const server = net.createServer();
  await new Promise((resolve, reject) => {
    server.once("error", reject);
    server.listen(port, "127.0.0.1", resolve);
  });
  await new Promise((resolve, reject) => server.close(error => error ? reject(error) : resolve()));
}

async function waitForHttp(url, label) {
  const deadline = Date.now() + 180_000;
  while (Date.now() < deadline) {
    try {
      const response = await fetch(url, { signal: AbortSignal.timeout(3_000), redirect: "manual" });
      if (response.status >= 200 && response.status < 400) return;
    } catch {
      // The containers can need time to initialize on a fresh runner.
    }
    await new Promise(resolve => setTimeout(resolve, 1_000));
  }
  throw new Error(`${label} did not become ready within 180 seconds`);
}

function compose(...args) {
  return ["compose", "--project-name", projectName, "--file", composeFile, ...args];
}

function readComposeConfig() {
  return JSON.parse(commandOutput("docker", compose("config", "--format", "json")));
}

function validateComposeConfig(config) {
  assert.equal(config.name, projectName, "temporary Compose project name is not unique to this run");
  assert.deepEqual(Object.keys(config.services).sort(), ["elsa-studio", "elsa-workbench"]);
  assert.equal(config.services["elsa-workbench"].image, images.workbench.ref);
  assert.equal(config.services["elsa-studio"].image, images.studio.ref);
  assert.equal(config.services["elsa-workbench"].environment.ASPNETCORE_ENVIRONMENT, "Production");
  assert.equal(config.services["elsa-studio"].environment.ASPNETCORE_ENVIRONMENT, "Production");
  assert.equal(config.services["elsa-workbench"].environment["Cors__AllowedOrigins__0"], studioUrl);
  const returnOrigins = config.services["elsa-workbench"].environment;
  const returnOriginPrefix = "CShells__Shells__default__Features__FoundationIdentityAspNetCoreIdentity__AllowedReturnUrlOrigins__";
  assert.equal(returnOrigins[`${returnOriginPrefix}2`], studioUrl,
    "the Production login return-origin allowlist must include the published Studio origin at index 2");
  assert.equal(returnOrigins[`${returnOriginPrefix}0`], undefined,
    "the pair Compose override must preserve the shell's existing return-origin entry at index 0");
  assert.equal(returnOrigins[`${returnOriginPrefix}1`], undefined,
    "the pair Compose override must preserve the shell's existing return-origin entry at index 1");
  assert.equal(config.services["elsa-studio"].environment.Studio__BackendBaseUrl, workbenchUrl);
  assert.equal(config.services["elsa-workbench"].environment["Elsa__ModuleManagement__ApiKey"],
    config.services["elsa-studio"].environment.Studio__BackendModuleManagementApiKey);
  assert.deepEqual(Object.keys(config.networks ?? {}), ["default"]);
  assert.equal(config.networks.default.name, `${projectName}_default`);
  assert.notEqual(config.networks.default.external, true, "external networks are not allowed in this proof");
  assert.ok(!config.networks.default.driver || config.networks.default.driver === "bridge",
    "only the default local bridge network is allowed");

  for (const [name, service] of Object.entries(config.services)) {
    assert.deepEqual(Object.keys(service.networks ?? {}), ["default"], `${name} must use only the project-local default network`);
    assert.equal(service.build, undefined, `${name} must use its published image`);
    const expectedHostPort = name === "elsa-workbench" ? ports.workbench : ports.studio;
    const mappings = (service.ports ?? []).map(({ host_ip, target, published, protocol }) => ({
      host_ip, target, published: String(published), protocol
    }));
    assert.deepEqual(mappings, [{ host_ip: "127.0.0.1", target: 8080, published: String(expectedHostPort), protocol: "tcp" }],
      `${name} port mapping changed from its loopback-only published pair port`);
    for (const volume of service.volumes ?? []) {
      assert.equal(volume.type, "volume", `${name} has an unexpected bind mount`);
    }
  }
  for (const volume of Object.values(config.volumes ?? {})) {
    assert.notEqual(volume.external, true, "external volumes are not allowed in this proof");
    assert.ok(!volume.driver || volume.driver === "local", "only local named volumes are allowed");
    assert.deepEqual(volume.driver_opts ?? {}, {}, "volume driver options are not allowed");
  }

  assert.deepEqual(Object.keys(config.volumes ?? {}).sort(), ["elsa-data", "server-packages"]);
  for (const [key, expectedName] of Object.entries({
    "elsa-data": `${projectName}_elsa-data`,
    "server-packages": `${projectName}_server-packages`
  })) {
    assert.equal(config.volumes[key].name, expectedName, `${key} did not resolve to the unique Compose project volume`);
  }
  const workbenchMounts = (config.services["elsa-workbench"].volumes ?? []).map(mount => ({
    source: mount.source,
    target: mount.target,
    type: mount.type
  })).sort((left, right) => left.target.localeCompare(right.target));
  assert.deepEqual(workbenchMounts, [
    { source: "elsa-data", target: "/app/data", type: "volume" },
    { source: "server-packages", target: "/app/packages", type: "volume" }
  ]);
  assert.deepEqual(config.services["elsa-studio"].volumes ?? [], []);
}

function ownedResources() {
  return {
    containers: commandOutput("docker", ["ps", "--all", "--quiet", "--filter", `label=com.docker.compose.project=${projectName}`]),
    networks: commandOutput("docker", ["network", "ls", "--quiet", "--filter", `label=com.docker.compose.project=${projectName}`]),
    volumes: commandOutput("docker", ["volume", "ls", "--quiet", "--filter", `label=com.docker.compose.project=${projectName}`])
  };
}

function assertNoPreexistingProjectResources() {
  assert.deepEqual(ownedResources(), { containers: "", networks: "", volumes: "" },
    "unique Compose project name already has resources; refusing ownership and cleanup");
  const volumeNames = commandOutput("docker", ["volume", "ls", "--format", "{{.Name}}"]).split("\n");
  for (const name of [`${projectName}_server-packages`, `${projectName}_elsa-data`]) {
    assert.ok(!volumeNames.includes(name), "unique Compose volume name already exists; refusing ownership and cleanup");
  }
}

function selectedAmd64Manifest(image) {
  const raw = commandOutput("docker", ["buildx", "imagetools", "inspect", "--raw", image.ref]);
  const index = JSON.parse(raw);
  const manifest = index.manifests?.find(entry => entry.platform?.os === "linux" && entry.platform?.architecture === "amd64");
  assert.ok(manifest, `${image.ref} has no linux/amd64 manifest`);
  assert.equal(manifest.digest, image.amd64, `${image.ref} linux/amd64 digest differs from the reviewed candidate`);
  return manifest.digest;
}

function inspectImage(image) {
  const output = commandOutput("docker", [
    "image", "inspect", "--format", "{{.Id}}|{{.Os}}/{{.Architecture}}|{{json .Config.Labels}}", image.ref
  ]);
  const separator = output.indexOf("|");
  const imageId = output.slice(0, separator);
  const remainder = output.slice(separator + 1);
  const platformSeparator = remainder.indexOf("|");
  const platform = remainder.slice(0, platformSeparator);
  const labels = JSON.parse(remainder.slice(platformSeparator + 1));
  assert.equal(platform, "linux/amd64", `${image.ref} was not pulled for linux/amd64`);

  const source = labels?.["org.opencontainers.image.source"] ?? "not supplied";
  const revision = labels?.["org.opencontainers.image.revision"] ?? "not supplied";
  const version = labels?.["org.opencontainers.image.version"] ?? "not supplied";
  if (revision !== "not supplied") {
    assert.ok(image.source.startsWith(revision) || revision.startsWith(image.source),
      `${image.ref} OCI revision label does not match the reviewed source revision`);
  }
  if (source !== "not supplied") {
    assert.ok(source.toLowerCase().includes(image.repository), `${image.ref} OCI source label points elsewhere`);
  }
  return { imageId, platform, source, revision, version };
}

function inspectApplicationVersion(containerId, application) {
  const manifest = commandOutput("docker", ["exec", containerId, "cat", `/app/${application}.deps.json`]);
  const libraries = Object.keys(JSON.parse(manifest).libraries ?? {});
  const entries = libraries.filter(name => name.startsWith(`${application}/`));
  assert.equal(entries.length, 1, "the published application dependency manifest must identify its host version");
  const version = entries[0].slice(application.length + 1);
  assert.match(version, /^\d[0-9A-Za-z.+-]{0,99}$/);
  return { version, manifestHash: createHash("sha256").update(manifest).digest("hex") };
}

function findRecord(value, predicate) {
  if (Array.isArray(value)) {
    for (const item of value) {
      const found = findRecord(item, predicate);
      if (found) return found;
    }
  } else if (value && typeof value === "object") {
    if (predicate(value)) return value;
    for (const item of Object.values(value)) {
      const found = findRecord(item, predicate);
      if (found) return found;
    }
  }
  return null;
}

function captureUiPostIdentity(page, { endpoint, expectedOrigin, predicate, project, expectedDefinitionId, expectedArtifactId }) {
  return new Promise((resolve, reject) => {
    const timer = setTimeout(() => finish(new Error(`${project} response was not observed`)), 45_000);
    const finish = value => {
      clearTimeout(timer);
      page.off("response", onResponse);
      value instanceof Error ? reject(value) : resolve(value);
    };
    const onResponse = async response => {
      if (response.request().method() !== "POST" || !response.ok()) return;
      const url = new URL(response.url());
      if (url.origin !== expectedOrigin || !endpoint(url.pathname)) return;
      let payload;
      try {
        payload = await response.json();
      } catch {
        return;
      }
      const record = findRecord(payload, predicate);
      if (!record) return;
      if (expectedDefinitionId && record.definitionId !== expectedDefinitionId) return;
      if (expectedArtifactId && url.pathname.split("/").at(-2) !== expectedArtifactId) return;
      // Keep only the identifiers needed to correlate the visible browser journey.
      finish(project === "publication"
        ? { artifactId: record.artifactId, definitionId: record.definitionId, publicationId: record.publicationId }
        : { workflowExecutionId: record.workflowExecutionId ?? record.runId ?? record.executionId });
    };
    page.on("response", onResponse);
  });
}

async function signIn(page) {
  const authResponses = observeSafeAuthResponses(page);
  await loginPhase(page, "login-studio-navigation", authResponses,
    () => page.goto(`${studioUrl}/workflows/definitions`, { waitUntil: "domcontentloaded" }));
  const loginForm = page.locator('form[action="/_elsa/identity/login"]');
  await loginPhase(page, "login-backend-form", authResponses,
    () => loginForm.waitFor({ state: "visible", timeout: 45_000 }));
  authResponses.returnTargets.hidden = classifyReturnTarget(
    await loginForm.locator('input[name="returnUrl"]').getAttribute("value")
  );
  await loginPhase(page, "login-form-submit", authResponses, async () => {
    await loginForm.locator('input[name="username"]').fill("admin");
    await loginForm.locator('input[name="password"]').fill("Password123!");
    await loginForm.locator('button[type="submit"]').click({ noWaitAfter: true });
  });
  await loginPhase(page, "login-return-to-studio", authResponses,
    () => page.waitForURL(url => url.origin === studioUrl, { timeout: 45_000 }));
  await loginPhase(page, "login-return-contract", authResponses, async () => {
    assert.deepEqual(authResponses.returnTargets,
      { query: "studio-origin", hidden: "studio-origin", redirect: "studio-origin" },
      "the login challenge, rendered form and redirect must preserve the Studio return origin");
    log("Login return targets: query=studio-origin; hidden=studio-origin; redirect=studio-origin");
  });
  await loginPhase(page, "login-session-navigation", authResponses,
    () => page.goto(`${studioUrl}/workflows/definitions`, { waitUntil: "domcontentloaded" }));
  await loginPhase(page, "login-session-ready", authResponses,
    () => page.getByRole("heading", { name: "Definitions", exact: true }).waitFor({ state: "visible", timeout: 45_000 }));
}

function observeSafeAuthResponses(page) {
  const allowedPaths = new Set([
    "/_elsa/identity/bootstrap",
    "/_elsa/identity/session",
    "/_elsa/identity/login"
  ]);
  const responses = [];
  const returnTargets = { query: "other", hidden: "other", redirect: "other" };
  page.on("response", response => {
    const url = new URL(response.url());
    if (url.origin !== workbenchUrl || !allowedPaths.has(url.pathname)) return;
    const method = response.request().method();
    if (url.pathname === "/_elsa/identity/login" && method === "GET") {
      returnTargets.query = classifyReturnTarget(url.searchParams.get("returnUrl"));
    }
    if (url.pathname === "/_elsa/identity/login" && method === "POST") {
      returnTargets.redirect = classifyReturnTarget(response.headers().location);
    }
    responses.push(`${method} ${url.pathname} status=${response.status()}`);
    if (responses.length > 8) responses.shift();
  });
  return { responses, returnTargets };
}

function classifyReturnTarget(value) {
  if (typeof value !== "string" || value.length === 0) return "other";
  try {
    const target = new URL(value, `${workbenchUrl}/`);
    if (target.origin === studioUrl) return "studio-origin";
    if (target.origin === workbenchUrl && target.pathname === "/") return "workbench-root";
    if (!/^[a-z][a-z0-9+.-]*:/i.test(value) && !value.startsWith("//")) return "relative";
  } catch {
    // Invalid or unsupported targets are reported only as the fixed "other" category.
  }
  return "other";
}

async function loginPhase(page, phase, authResponses, action) {
  stage = phase;
  try {
    return await action();
  } catch (error) {
    await logSafeLoginDiagnostic(page, phase, authResponses, error);
    throw error;
  }
}

async function logSafeLoginDiagnostic(page, phase, authResponses, error) {
  const currentUrl = new URL(page.url());
  const knownOrigin = [studioUrl, workbenchUrl].includes(currentUrl.origin);
  const knownPath = ["/", "/workflows/definitions", "/_elsa/identity/login"].includes(currentUrl.pathname);
  const location = knownOrigin
    ? `${currentUrl.origin}${knownPath ? currentUrl.pathname : "/[other-route]"}`
    : "[other-origin]";
  const formVisible = await page.locator('form[action="/_elsa/identity/login"]').isVisible().catch(() => false);
  const signingInVisible = await page.locator('[role="status"]').getByText("Signing in…", { exact: true }).isVisible().catch(() => false);
  const unableToSignInVisible = await page.getByRole("alert").getByText("Unable to sign in", { exact: true }).isVisible().catch(() => false);
  const state = formVisible
    ? "backend-login-form"
    : unableToSignInVisible
      ? "studio-auth-failed"
      : signingInVisible
        ? "studio-signing-in"
        : currentUrl.origin === workbenchUrl && currentUrl.pathname === "/_elsa/identity/login"
          ? "backend-login-route-without-form"
          : currentUrl.origin === studioUrl && currentUrl.pathname === "/workflows/definitions"
            ? "studio-definitions-without-session"
            : currentUrl.origin === studioUrl
              ? "studio-other-route"
              : currentUrl.origin === workbenchUrl
                ? "workbench-other-route"
                : "other-origin";
  log(`login diagnostic phase=${phase}; failure=${safeFailureClass(error)}; state=${state}; location=${location}; form=${formVisible}; signing-in=${signingInVisible}; return-targets=query:${authResponses.returnTargets.query},hidden:${authResponses.returnTargets.hidden},redirect:${authResponses.returnTargets.redirect}; auth-responses=${authResponses.responses.join(" | ") || "none"}`);
}

async function runBrowserJourney() {
  browser = await chromium.launch({ headless: true });
  const context = await browser.newContext({ viewport: { width: 1440, height: 1000 } });
  const page = await context.newPage();
  page.setDefaultTimeout(30_000);

  stage = "browser-login";
  await signIn(page);
  log("Authenticated through the published Studio and Workbench sign-in flow");

  stage = "workflow-create";
  const createButton = page.locator('button[title="Create workflow"]');
  await createButton.waitFor({ state: "visible" });
  await createButton.click();
  const createDialog = page.getByRole("dialog", { name: "Create Workflow" });
  await createDialog.getByLabel("Display name", { exact: true }).fill(workflowName);
  await createDialog.getByRole("button", { name: "Create", exact: true }).click();
  await page.waitForFunction(() => new URL(window.location.href).searchParams.has("definition"));
  const definitionId = await page.evaluate(() => new URL(window.location.href).searchParams.get("definition"));
  assert.ok(definitionId, "created workflow definition ID is missing from the Studio URL");
  log(`Created workflow definition ${definitionId} through the Studio UI`);

  stage = "activity-picker-trigger";
  const addActivity = page.getByRole("button", { name: "Add activity", exact: true });
  await addActivity.waitFor({ state: "visible" });
  await addActivity.click();
  stage = "activity-picker-open";
  const menu = page.locator(".wf-connect-menu:visible");
  await menu.waitFor({ state: "visible" });
  assert.equal(await menu.count(), 1, "the activity picker menu must be unique and visible");
  const picker = menu.getByRole("listbox", { name: "Activity picker", exact: true });
  await picker.waitFor({ state: "visible" });
  assert.equal(await picker.count(), 1, "the visible menu must contain one Activity picker listbox");
  stage = "activity-picker-search";
  await menu.getByRole("searchbox", { name: "Search activities", exact: true }).fill("WriteLine");
  stage = "activity-picker-option";
  const writeLineOption = picker.getByRole("option").filter({
    has: page.locator("strong").filter({ hasText: /^write\s*line$/i })
  });
  await writeLineOption.waitFor({ state: "visible" });
  assert.equal(await writeLineOption.count(), 1, "the activity picker must return one Write Line option");
  await writeLineOption.click();
  stage = "activity-configure";
  const textProperty = page.locator(".wf-property-row").filter({
    has: page.locator(".wf-property-row-header > label").filter({ hasText: /^Text$/ })
  });
  await textProperty.waitFor({ state: "visible" });
  assert.equal(await textProperty.count(), 1, "the selected Write Line must expose one Text property row");
  const textEditor = textProperty.getByRole("textbox");
  await textEditor.waitFor({ state: "visible" });
  assert.equal(await textEditor.count(), 1, "the Text property must expose one literal text editor");
  await textEditor.fill(expectedOutput);
  const saveButton = page.getByRole("button", { name: /^Save$/i });
  if (await saveButton.count()) await saveButton.first().click();
  log("Added and configured a Write Line activity through the Studio UI");

  stage = "workflow-publish";
  await page.getByRole("button", { name: "Review & publish", exact: true }).click();
  const reviewDialog = page.getByRole("dialog", { name: "Review and publish", exact: true });
  await reviewDialog.waitFor({ state: "visible" });
  const publicationResponse = captureUiPostIdentity(page, {
    endpoint: pathname => /\/publishing\/workflows\/[^/]+\/publish$/.test(pathname),
    expectedOrigin: workbenchUrl,
    predicate: record => record.definitionId === definitionId && typeof record.artifactId === "string" && typeof record.publicationId === "string",
    project: "publication",
    expectedDefinitionId: definitionId
  });
  const publishButton = reviewDialog.getByRole("button", { name: "Publish", exact: true });
  await publishButton.waitFor({ state: "visible" });
  assert.equal(await publishButton.count(), 1, "the review dialog must expose one Publish action");
  await publishButton.click();
  const publication = await publicationResponse;
  assert.ok(publication.artifactId, "published artifact ID was not returned by the Studio publication action");
  if (publication.definitionId) assert.equal(publication.definitionId, definitionId);
  log(`Published artifact ${publication.artifactId} from definition ${definitionId}`);

  stage = "published-executables";
  await page.goto(`${studioUrl}/workflows/executables`, { waitUntil: "domcontentloaded" });
  const executableRow = page.getByRole("row").filter({ hasText: publication.artifactId });
  await executableRow.waitFor({ state: "visible" });
  assert.equal(await executableRow.count(), 1, "published artifact was not uniquely listed in Studio Executables");
  const runResponse = captureUiPostIdentity(page, {
    endpoint: pathname => pathname.endsWith(`/runtime/workflows/executables/${encodeURIComponent(publication.artifactId)}/execute`),
    expectedOrigin: workbenchUrl,
    predicate: record => typeof (record.workflowExecutionId ?? record.runId ?? record.executionId) === "string",
    project: "execution",
    expectedArtifactId: publication.artifactId
  });
  const runStartedAt = new Date();
  stage = "published-run-start";
  await executableRow.getByRole("button", { name: "Run", exact: true }).click();
  const run = await runResponse;
  const workflowExecutionId = run.workflowExecutionId;
  assert.ok(workflowExecutionId, "Studio run response did not include a workflow execution ID");
  log(`Started published artifact ${publication.artifactId} as run ${workflowExecutionId}`);

  const runUrl = new URL(`${studioUrl}/workflows/instances`);
  runUrl.searchParams.set("workflowExecutionId", workflowExecutionId);
  stage = "run-history-refresh";
  await page.goto(runUrl.href, { waitUntil: "domcontentloaded" });
  const runRow = page.getByRole("row", { name: new RegExp(`^Inspect workflow run ${workflowExecutionId} ·`) });
  await runRow.waitFor({ state: "visible", timeout: 60_000 });
  let terminalStatus = "";
  for (let attempt = 0; attempt < 90; attempt++) {
    const rowText = await runRow.innerText();
    if (/\bCompleted\b/.test(rowText)) { terminalStatus = "Completed"; break; }
    if (/\b(Faulted|Cancelled)\b/.test(rowText)) throw new Error("run reached a terminal unsuccessful status");
    await page.getByRole("button", { name: "Refresh", exact: true }).click();
    await page.waitForTimeout(2_000);
  }
  assert.equal(terminalStatus, "Completed", "published run did not complete within three minutes");
  assert.equal(await runRow.count(), 1, "the execution identifier must select exactly one current run row");
  await runRow.getByText("Published Run", { exact: true }).waitFor({ state: "visible" });
  await runRow.getByText(publication.artifactId, { exact: true }).waitFor({ state: "visible" });
  await runRow.getByText("0 incidents", { exact: true }).waitFor({ state: "visible" });
  await runRow.getByText("No active incidents", { exact: true }).waitFor({ state: "visible" });
  assert.ok((await runRow.innerText()).includes(definitionId), "run does not point to the created definition");
  log(`Inspected run ${workflowExecutionId}: Published Run, Completed, artifact matched, zero incidents`);

  stage = "run-activity-inspection";
  await runRow.click();
  await page.getByRole("heading", { name: "Run" }).waitFor({ state: "visible" });
  const timeline = page.getByRole("list", { name: "Execution timeline" });
  await timeline.waitFor({ state: "visible", timeout: 60_000 });
  const writeLineRows = timeline.locator(":scope > li").filter({ hasText: /write\s*line/i });
  assert.equal(await writeLineRows.count(), 1, "the current run must contain exactly one Write Line activity execution");
  await writeLineRows.locator('[data-status="completed"]').waitFor({ state: "visible" });
  await writeLineRows.first().getByRole("button").click();
  await page.getByRole("button", { name: /^Activity$/i }).click();
  const activityOverview = page.locator(".wf-activity-overview");
  await activityOverview.getByRole("heading", { name: "Write Line", exact: true }).waitFor({ state: "visible" });
  await activityOverview.getByText("Completed", { exact: true }).waitFor({ state: "visible" });
  assert.equal(await activityOverview.locator(".wf-activity-summary-grid").getByText("0", { exact: true }).count(), 1,
    "the current Write Line execution must report zero incidents");

  stage = "run-incident-inspection";
  await page.getByRole("button", { name: /^Issues(?: \(\d+\))?$/i }).click();
  await page.getByText("No incidents recorded.", { exact: true }).waitFor({ state: "visible" });
  log(`Inspected the completed Write Line activity for run ${workflowExecutionId}`);

  await context.close();
  await browser.close();
  browser = undefined;
  return { definitionId, artifactId: publication.artifactId, workflowExecutionId, runStartedAt };
}

async function countExactStdoutLine(containerId, expectedLine, since) {
  const logPath = commandOutput("docker", ["inspect", "--format", "{{.LogPath}}", containerId]);
  const pathMatch = logPath.match(/^\/var\/lib\/docker\/containers\/([a-f0-9]{64})\/([a-f0-9]{64})-json\.log$/);
  assert.ok(pathMatch && pathMatch[1] === containerId && pathMatch[2] === containerId,
    "owned Workbench does not use its expected local json-file log");

  let raw;
  try {
    raw = await readFile(logPath, "utf8");
  } catch {
    raw = commandOutput("sudo", ["cat", logPath]);
  }
  const sinceTime = since.getTime();
  let count = 0;
  let pending = "";
  for (const line of raw.split("\n")) {
    if (!line) continue;
    let entry;
    try { entry = JSON.parse(line); } catch { continue; }
    const timestamp = Date.parse(entry.time);
    if (entry.stream !== "stdout" || !Number.isFinite(timestamp) || timestamp < sinceTime) continue;
    pending += String(entry.log ?? "");
    const outputLines = pending.split("\n");
    pending = outputLines.pop() ?? "";
    for (const outputLine of outputLines) {
      if (outputLine.replace(/\r$/, "") === expectedLine) count++;
    }
  }
  if (pending.replace(/\r$/, "") === expectedLine) count++;
  return count;
}

async function cleanupOwnedResources() {
  if (!composeFile || !ownershipApproved) return;
  commandQuiet("docker", compose("down", "--volumes", "--remove-orphans"));
  assert.deepEqual(ownedResources(), { containers: "", networks: "", volumes: "" }, "owned Compose resources remain after cleanup");
  await assertPortFree(ports.workbench);
  await assertPortFree(ports.studio);
}

async function main() {
  assert.ok(runId && runAttempt, "this proof must run in an isolated GitHub Actions job");
  assert.match(projectName, /^elsa-pair-[a-zA-Z0-9-]+$/);

  const harnessSha = commandOutput("git", ["rev-parse", "HEAD"]);
  log(`Harness git=${harnessSha}; event=${process.env.GITHUB_EVENT_NAME ?? "not supplied"}; event SHA=${process.env.GITHUB_SHA ?? "not supplied"}; head ref=${process.env.GITHUB_HEAD_REF || "not supplied"}`);
  log(`Runner ${process.env.RUNNER_OS}/${process.arch}; image=${process.env.ImageOS ?? "not supplied"}/${process.env.ImageVersion ?? "not supplied"}; Docker and Compose versions recorded below`);
  log(commandOutput("docker", ["version", "--format", "{{.Server.Version}}"]));
  log(commandOutput("docker", ["compose", "version", "--short"]));
  assert.equal(os.platform(), "linux", "the published pair proof is scoped to Linux");
  assert.equal(process.arch, "x64", "the published pair proof is scoped to amd64");
  await assertPortFree(ports.workbench);
  await assertPortFree(ports.studio);

  stage = "compose-configuration";
  const composeSource = await readFile(path.join(repoRoot, "docker/compose/docker-compose.images.yml"), "utf8");
  let isolated = composeSource;
  isolated = replaceExactlyOnce(isolated, "name: elsa-stack", `name: ${projectName}`, "project name");
  isolated = replaceExactlyOnce(isolated, "image: elsaworkflows/elsa-workbench:latest", `image: ${images.workbench.ref}`, "Workbench image");
  isolated = replaceExactlyOnce(isolated, "image: elsaworkflows/elsa-studio:latest", `image: ${images.studio.ref}`, "Studio image");
  isolated = replaceExactlyOnce(isolated, '- "13000:8080"', '- "127.0.0.1:13000:8080"', "Workbench loopback port");
  isolated = replaceExactlyOnce(isolated, '- "14000:8080"', '- "127.0.0.1:14000:8080"', "Studio loopback port");
  tempDir = await mkdtemp(path.join(process.env.RUNNER_TEMP ?? os.tmpdir(), "elsa-published-pair-"));
  composeFile = path.join(tempDir, "docker-compose.images.yml");
  await writeFile(composeFile, isolated, { mode: 0o600 });
  const resolved = readComposeConfig();
  validateComposeConfig(resolved);
  assertNoPreexistingProjectResources();
  ownershipApproved = true;
  log("Canonical Production Compose copy validated: immutable images, loopback ports, project-owned named volumes");

  stage = "image-manifest-provenance";
  const imageWasCached = {};
  for (const [name, image] of Object.entries(images)) {
    const wasPresent = imageWasPresent(image.ref);
    imageWasCached[name] = wasPresent;
    const child = selectedAmd64Manifest(image);
    log(`${name}: ${image.ref}; local image before pull=${wasPresent ? "present" : "absent"}; linux/amd64 manifest=${child}`);
  }

  stage = "pull-pinned-images";
  for (const [name, service] of [["workbench", "elsa-workbench"], ["studio", "elsa-studio"]]) {
    const pullOutput = await commandCaptured("docker", compose("pull", service));
    const summary = summarizePullOutput(pullOutput);
    log(`${name}: local image before pull=${imageWasCached[name] ? "present" : "absent"}; Compose pull=${summary.outcome}; layer statuses=${summary.layerSummary}`);
  }
  stage = "start-owned-image-pair";
  commandQuiet("docker", compose("up", "--detach", "--pull", "never", "--wait", "--wait-timeout", "180"));
  await waitForHttp(workbenchUrl, "Workbench");
  await waitForHttp(studioUrl, "Studio");
  for (const [name, image] of Object.entries(images)) {
    stage = "container-image-provenance";
    const metadata = inspectImage(image);
    const serviceName = name === "workbench" ? "elsa-workbench" : "elsa-studio";
    const containerId = commandOutput("docker", compose("ps", "--quiet", serviceName));
    assert.match(containerId, /^[a-f0-9]{64}$/);
    const runningIdentity = commandOutput("docker", ["inspect", "--format", "{{.Image}}|{{.Config.Image}}|{{.HostConfig.LogConfig.Type}}", containerId]);
    const [runningImageId, configuredRef, logDriver] = runningIdentity.split("|");
    assert.equal(configuredRef, image.ref, `${name} container did not use the pinned immutable reference`);
    assert.equal(runningImageId, metadata.imageId, `${name} container image ID differs from the pinned image`);
    assert.equal(logDriver, "json-file", `${name} container is not using the expected local json-file log driver`);
    if (name === "workbench") ownedWorkbenchContainerId = containerId;
    log(`${name}: image ID=${metadata.imageId}; running image ID=${runningImageId}; OCI source=${metadata.source}; revision=${metadata.revision}; version=${metadata.version}; platform=${metadata.platform}`);
    stage = "application-version-provenance";
    const application = name === "workbench" ? "Elsa.Workbench" : "Elsa.Studio.Web";
    const artifact = inspectApplicationVersion(containerId, application);
    log(`${application}: dependency-manifest host version=${artifact.version}; trimmed manifest SHA256=${artifact.manifestHash}; this is distinct from the image tag and OCI base-image version`);
  }

  stage = "browser-workflow-journey";
  const identity = await runBrowserJourney();

  stage = "workbench-stdout-verification";
  assert.ok(ownedWorkbenchContainerId, "owned Workbench container identity is missing");
  const stdoutCount = await countExactStdoutLine(ownedWorkbenchContainerId, expectedOutput, identity.runStartedAt);
  assert.equal(stdoutCount, 1, "the owned Workbench did not emit the expected Write Line output exactly once");
  log(`Workbench emitted the expected Write Line output once; definition=${identity.definitionId}; artifact=${identity.artifactId}; run=${identity.workflowExecutionId}`);
}

try {
  await main();
} catch (error) {
  console.error(`[published-pair] failed during ${stage} (${safeFailureClass(error)})`);
  process.exitCode = 1;
} finally {
  if (browser) await browser.close().catch(() => {});
  try {
    await cleanupOwnedResources();
    if (ownershipApproved) log("Owned containers, network, volumes, and ports were cleaned up");
    else log("Owned-resource cleanup was skipped because project ownership was not approved");
  } catch (error) {
    cleanupError = error;
  }
  if (tempDir) await rm(tempDir, { recursive: true, force: true }).catch(() => {});
  if (cleanupError) {
    console.error(`[published-pair] owned cleanup failed (${safeFailureClass(cleanupError)})`);
    process.exitCode = 1;
  }
}
