// Playwright helpers for the UX QA routine (docs/skills/catalog.md#ux-qa-routine). A journey script is both the
// exploration and the reproduction filed in an issue:
//
//   import { journey } from "./lib.mjs";
//   await journey("rename-workflow", async qa => {
//     await qa.signIn();
//     await qa.page.goto(`${qa.studioUrl}/workflows/definitions`);
//     await qa.step("definitions-list");
//   });
//
// Each step writes a screenshot and an ARIA snapshot; console errors, page errors and failed requests are captured
// throughout. The journey writes trace.json to its output directory and exits non-zero when it throws.
import { existsSync, readFileSync } from "node:fs";
import { mkdir, writeFile } from "node:fs/promises";
import { createRequire } from "node:module";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { chromium } from "playwright";

const here = path.dirname(fileURLToPath(import.meta.url));
const require = createRequire(import.meta.url);

/** Reads setup's KEY=value state file as data, returning an empty object when it is absent. */
function readState() {
  const file = path.join(here, ".state", "env");
  if (!existsSync(file)) return {};
  return Object.fromEntries(readFileSync(file, "utf8").split("\n")
    .map(line => line.match(/^([A-Z_]+)=(.*)$/))
    .filter(Boolean)
    .map(([, key, value]) => [key, value]));
}

export const state = readState();
export const studioUrl = process.env.STUDIO_URL ?? state.STUDIO_URL ?? "http://localhost:5089";
export const workbenchUrl = process.env.WORKBENCH_URL ?? state.WORKBENCH_URL ?? "http://localhost:5095";
const foundationDir = state.FOUNDATION_DIR ?? path.resolve(here, "../..");

/** Reads the Development admin credentials from the Workbench's shells.json seed settings. */
function seededAdmin() {
  const shells = readFileSync(path.join(foundationDir, "src/apps/Elsa.Workbench/shells.json"), "utf8");
  /** Returns the first quoted value for a seed setting, or undefined when it is absent. */
  const value = key => shells.match(new RegExp(`"${key}"\\s*:\\s*"([^"]*)"`))?.[1];
  return { username: value("SeedAdminUserName"), password: value("SeedAdminPassword") };
}

/** Adds page console, runtime and network errors to the trace, queuing HTTP error body reads in trace.pending. */
function observe(page, trace) {
  page.on("console", message => {
    if (message.type() === "error" || message.type() === "warning")
      trace.events.push({ kind: `console-${message.type()}`, url: page.url(), text: message.text().slice(0, 2000) });
  });
  page.on("pageerror", error => trace.events.push({ kind: "page-error", url: page.url(), text: String(error).slice(0, 2000) }));
  page.on("requestfailed", request => trace.events.push({
    kind: "request-failed", method: request.method(), url: request.url(), text: request.failure()?.errorText
  }));
  page.on("response", response => {
    if (response.status() < 400) return;
    const event = { kind: "http-error", method: response.request().method(), url: response.url(), status: response.status() };
    trace.events.push(event);
    trace.pending.push(response.text()
      .then(body => {
        event.traceId = body.match(/"traceId"\s*:\s*"([^"]+)"/)?.[1];
        event.body = body.slice(0, 1000);
      })
      .catch(() => {}));
  });
}

/** Runs one journey in a fresh browser context. Options: viewport ({ width, height }), out (output directory). */
export async function journey(name, fn, { viewport = { width: 1440, height: 1000 }, out } = {}) {
  const outDir = out ?? path.join(process.env.UX_QA_OUT ?? "/tmp/ux-qa", `${name}-${Date.now()}`);
  await mkdir(outDir, { recursive: true });
  const trace = {
    journey: name, studioUrl, workbenchUrl, foundationSha: state.FOUNDATION_SHA, studioSha: state.STUDIO_SHA,
    startedAt: new Date().toISOString(), steps: [], events: [], audits: [], notes: [], pending: []
  };
  const browser = await chromium.launch({ headless: process.env.HEADED !== "1" });
  const context = await browser.newContext({ viewport });
  context.on("page", page => observe(page, trace)); // fires for newPage() too, so every page is observed once
  const page = await context.newPage();
  page.setDefaultTimeout(20_000);
  let stepNumber = 0;

  const qa = {
    page, context, studioUrl, workbenchUrl, outDir,

    /** Saves a numbered screenshot and ARIA snapshot, then records the page URL and current event count. */
    async step(stepName) {
      const prefix = `${String(++stepNumber).padStart(2, "0")}-${stepName}`;
      await page.screenshot({ path: path.join(outDir, `${prefix}.png`) });
      const aria = await page.locator("body").ariaSnapshot().catch(error => `aria snapshot failed: ${error}`);
      await writeFile(path.join(outDir, `${prefix}.aria.yml`), aria);
      trace.steps.push({ step: prefix, url: page.url(), at: new Date().toISOString(), eventsSoFar: trace.events.length });
    },

    /** Appends an observation to the journey trace's notes. */
    note(text) { trace.notes.push(text); },

    /** Signs in with the seeded admin and waits for the redirect to Studio and removal of the login form. */
    async signIn() {
      const { username, password } = seededAdmin();
      await page.goto(`${studioUrl}/workflows/definitions`, { waitUntil: "domcontentloaded" });
      const form = page.locator('form[action="/_elsa/identity/login"]');
      await form.waitFor({ state: "visible", timeout: 45_000 });
      // The form is served by the Workbench, so only a redirect back to Studio can satisfy the wait below.
      if (new URL(page.url()).origin === studioUrl) throw new Error("the sign-in form rendered on the Studio origin");
      await form.locator('input[name="username"]').fill(username);
      await form.locator('input[name="password"]').fill(password);
      await form.locator('button[type="submit"]').click({ noWaitAfter: true });
      await page.waitForURL(url => url.origin === studioUrl && !url.pathname.startsWith("/_elsa/identity"), { timeout: 45_000 });
      await form.waitFor({ state: "detached" });
    },

    /** Runs axe-core (WCAG 2 A/AA) on the current page and records the violations. */
    async audit(auditName) {
      await page.addScriptTag({ content: readFileSync(require.resolve("axe-core/axe.min.js"), "utf8") });
      const result = await page.evaluate(() => window.axe.run(document, { runOnly: ["wcag2a", "wcag2aa"] }));
      const violations = result.violations.map(v => ({
        id: v.id, impact: v.impact, help: v.help, nodes: v.nodes.length, targets: v.nodes.slice(0, 3).map(n => n.target.join(" "))
      }));
      trace.audits.push({ audit: auditName, url: page.url(), violations });
      return violations;
    }
  };

  try {
    await fn(qa);
    trace.outcome = "passed";
  } catch (error) {
    trace.outcome = "failed";
    trace.error = { name: error?.name, message: String(error?.message ?? error).slice(0, 4000), url: page.url() };
    await page.screenshot({ path: path.join(outDir, "failure.png") }).catch(() => {});
    process.exitCode = 1;
  } finally {
    await Promise.all(trace.pending);
    delete trace.pending;
    trace.finishedAt = new Date().toISOString();
    await writeFile(path.join(outDir, "trace.json"), JSON.stringify(trace, null, 2));
    await browser.close();
  }

  const errors = trace.events.filter(e => e.kind !== "console-warning").length;
  console.log(`${name}: ${trace.outcome}; ${trace.steps.length} steps, ${errors} error events; output in ${outDir}`);
  if (trace.error) console.log(`  ${trace.error.message.split("\n")[0]}`);
  return trace;
}
