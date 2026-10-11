// The UX QA routine's regression net: sign in, create a workflow, add a Write Line, publish, run it from Executables,
// and inspect the run. Adapted from tools/contributor-pair-proof/run.mjs for a source-built pair.
import { journey } from "./lib.mjs";

const stamp = Date.now();
const workflowName = `UX QA golden path ${stamp}`;

await journey("golden-path", async qa => {
  const { page, studioUrl } = qa;
  /** Runs an action, labels any action error with the step name, and captures evidence after success. */
  const run = async (stepName, action) => {
    try {
      await action();
    } catch (error) {
      error.message = `step ${stepName}: ${error.message}`;
      throw error;
    }
    await qa.step(stepName);
  };

  await run("sign-in", async () => {
    await qa.signIn();
    await page.getByRole("heading", { name: "Definitions", exact: true }).waitFor({ state: "visible", timeout: 45_000 });
  });

  await run("create-workflow", async () => {
    await page.locator('button[title="Create workflow"]').click();
    const dialog = page.getByRole("dialog", { name: "Create Workflow" });
    await dialog.getByLabel("Display name", { exact: true }).fill(workflowName);
    // Choose the root explicitly once the activity catalog has enabled it. Create is clickable before the catalog
    // loads, and a workflow created then has no root and no way to add one.
    const flowchart = dialog.getByRole("radio", { name: "Flowchart", exact: true });
    try {
      await flowchart.and(page.locator(":enabled")).waitFor({ state: "attached", timeout: 30_000 });
    } catch {
      throw new Error("the Flowchart root option never became available in the Create Workflow dialog");
    }
    // The radio itself is visually hidden behind its card. A `has` locator resolves inside the card, so it is unscoped.
    await dialog.locator("label.wf-root-card").filter({ has: page.getByRole("radio", { name: "Flowchart", exact: true }) }).click();
    await dialog.getByRole("button", { name: "Create", exact: true }).click();
    await page.waitForFunction(() => new URL(window.location.href).searchParams.has("definition"));
  });

  await run("add-write-line", async () => {
    await page.getByRole("button", { name: "Add activity", exact: true }).click();
    const menu = page.locator(".wf-connect-menu:visible");
    await menu.getByRole("searchbox", { name: "Search activities", exact: true }).fill("WriteLine");
    await menu.getByRole("listbox", { name: "Activity picker", exact: true }).getByRole("option")
      .filter({ has: page.locator("strong").filter({ hasText: /^write\s*line$/i }) })
      .click();
    const text = page.locator(".wf-property-row")
      .filter({ has: page.locator(".wf-property-row-header > label").filter({ hasText: /^Text$/ }) })
      .getByRole("textbox");
    await text.fill(`Hello from ${workflowName}`);
  });

  let artifactId;
  await run("publish", async () => {
    await page.getByRole("button", { name: "Review & publish", exact: true }).click();
    const dialog = page.getByRole("dialog", { name: "Review and publish", exact: true });
    const published = page.waitForResponse(response =>
      response.request().method() === "POST" && /\/publishing\/workflows\/[^/]+\/publish$/.test(new URL(response.url()).pathname));
    await dialog.getByRole("button", { name: "Publish", exact: true }).click();
    const response = await published;
    if (!response.ok()) throw new Error(`publish returned ${response.status()}`);
    artifactId = (await response.json()).artifactId;
    if (!artifactId) throw new Error("publish response carried no artifactId");
  });

  let workflowExecutionId;
  await run("run-from-executables", async () => {
    await page.goto(`${studioUrl}/workflows/executables`, { waitUntil: "domcontentloaded" });
    const row = page.getByRole("row").filter({ hasText: artifactId });
    const started = page.waitForResponse(response =>
      response.request().method() === "POST" && new URL(response.url()).pathname.endsWith("/execute"));
    await row.getByRole("button", { name: "Run", exact: true }).click();
    const response = await started;
    if (!response.ok()) throw new Error(`execute returned ${response.status()}`);
    const body = await response.json();
    workflowExecutionId = body.workflowExecutionId ?? body.runId ?? body.executionId;
    if (!workflowExecutionId) throw new Error("execute response carried no execution id");
  });

  await run("run-completed", async () => {
    const url = new URL(`${studioUrl}/workflows/instances`);
    url.searchParams.set("workflowExecutionId", workflowExecutionId);
    await page.goto(url.href, { waitUntil: "domcontentloaded" });
    const row = page.getByRole("row", { name: new RegExp(`^Inspect workflow run ${workflowExecutionId} ·`) });
    await row.waitFor({ state: "visible", timeout: 60_000 });
    for (let attempt = 0; attempt < 60; attempt++) {
      const text = await row.innerText();
      if (/\bCompleted\b/.test(text)) return;
      if (/\b(Faulted|Cancelled)\b/.test(text)) throw new Error(`run ended ${text.match(/\b(Faulted|Cancelled)\b/)[1]}`);
      await page.getByRole("button", { name: "Refresh", exact: true }).click();
      await page.waitForTimeout(2_000);
    }
    throw new Error("run did not complete within two minutes");
  });

  await run("inspect-timeline", async () => {
    await page.getByRole("row", { name: new RegExp(`^Inspect workflow run ${workflowExecutionId} ·`) }).click();
    const timeline = page.getByRole("list", { name: "Execution timeline" });
    await timeline.waitFor({ state: "visible", timeout: 60_000 });
    const writeLine = timeline.locator(":scope > li").filter({ hasText: /write\s*line/i });
    await writeLine.locator('[data-status="completed"]').waitFor({ state: "visible" });
    await writeLine.getByRole("button").click();
  });

  await run("inspect-activity-tab", async () => {
    await page.getByRole("tab", { name: "Activity", exact: true }).click();
    await page.locator(".wf-activity-overview").getByText("Completed", { exact: true }).waitFor({ state: "visible" });
  });

  await run("inspect-issues-tab", async () => {
    await page.getByRole("tab", { name: /^Issues(?: \(\d+\))?$/i }).click();
    await page.getByText("No incidents recorded.", { exact: true }).waitFor({ state: "visible" });
  });
});
