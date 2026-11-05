import { expect, test, type Page } from "@playwright/test";
import axe, { type AxeResults, type RunOptions } from "axe-core";
import { createDefaultEtlConnectorPipeline } from "../../../Extensions/Etl/studio/Elsa.Studio.Etl/Client/src/connectorAuthoringHelpers";

// Browser-level product components against a deterministic API fixture. Actual Workbench HTTP acceptance is T019.
async function arrangeApi(page: Page, options: { jsonlSource?: boolean; csvErrors?: boolean; continuous?: boolean; sqlMain?: boolean; sqlSource?: boolean;
  rabbitMqMain?: boolean; rabbitMqSource?: boolean } = {}) {
  let pipeline = createDefaultEtlConnectorPipeline();
  let revision = 1;
  let created = false;
  const hash = "b".repeat(64);
  let published = false;
  const tuple = { definitionId: "orders", definitionVersion: 1, contentHash: hash,
    selection: `etl1:orders:1:${hash}`, publishedAt: "2026-10-21T12:00:00Z" };
  const relations = {
    bindings: "bindings", definitions: "definitions", definition: "definitions/{id}", draft: "definitions/{id}/draft",
    validation: "definitions/{id}/validate", publication: "definitions/{id}/publish",
    versions: "definitions/{id}/versions", version: "definitions/{id}/versions/{version}"
  };
  await page.route("**/fixture-etl/**", async route => {
    const request = route.request();
    const path = new URL(request.url()).pathname.replace("/fixture-etl/", "");
    let json: unknown;
    if (path === "capabilities") json = { capabilities: [{ id: "elsa.api.etl-design-v2", contractVersion: "1",
      links: Object.entries(relations).map(([name, href]) => ({ rel: `etl-v2-${name}`, href: `etl/v2/${href}` })) }] };
    else if (path === "etl/v2/bindings") json = { items: [
      { alias: "source", role: "Source", kind: "http" }, { alias: "csv-only", role: "Source", kind: "csv" },
      { alias: "output", role: "MainOutput", kind: "http" }, { alias: "errors", role: "ErrorOutput", kind: "http" },
      { alias: "error-file", role: "ErrorOutput", kind: "jsonl" }, { alias: "warehouse", role: "MainOutput", kind: "sql" },
      { alias: "people-db", role: "Source", kind: "sql" }, { alias: "events", role: "MainOutput", kind: "rabbitMq" },
      { alias: "events-in", role: "Source", kind: "rabbitMq" }
    ], nextCursor: null };
    else if (path === "etl/v2/definitions") {
      if (request.method() === "POST") {
        const body = request.postDataJSON();
        expect(body.id).toBe("orders");
        expect(body.pipeline.source.kind).toBe("httpJson");
        pipeline = body.pipeline; created = true;
        json = { definitionId: "orders", revision, lastPublishedVersion: 0, pipeline, updatedAt: "2026-10-21T12:00:00Z" };
      } else json = { items: created ? [{ definitionId: "orders", revision,
        lastPublishedVersion: published ? 1 : 0, updatedAt: "2026-10-21T12:00:00Z" }] : [], nextCursor: null };
    }
    else if (path === "etl/v2/definitions/orders" || path.endsWith("/draft")) {
      if (request.method() === "PUT") {
        const body = request.postDataJSON();
        expect(body.expectedRevision).toBe(revision);
        if (options.sqlSource) {
          expect(body.pipeline.source).toEqual({ kind: "sql", binding: "people-db", table: "dbo.people",
            columns: [{ name: "id", type: "int64", required: true }, { name: "name", type: "string", required: true }], key: ["id"],
            watermark: "id" });
          expect(body.pipeline.continuous).toBeUndefined();
        } else if (options.rabbitMqSource) {
          expect(body.pipeline.source).toMatchObject({ kind: "rabbitmq", binding: "events-in", queue: "people", maxMessages: 250 });
          expect(body.pipeline.continuous).toBeUndefined();
        } else if (options.rabbitMqMain) {
          expect(body.pipeline.routes.main).toEqual({ kind: "rabbitmq", binding: "events", exchange: "etl.people", routingKey: "people.loaded" });
        } else if (options.sqlMain) {
          expect(body.pipeline.routes.main).toEqual({ kind: "sql", binding: "warehouse", table: "dbo.people", key: ["name"] });
        } else if (options.continuous) {
          expect(body.pipeline.continuous).toEqual({ pollIntervalSeconds: 30 });
        } else if (options.csvErrors) {
          expect(body.pipeline.routes.errors).toEqual({ kind: "csv", binding: "error-file" });
        } else if (options.jsonlSource) {
          expect(body.pipeline.source).toEqual({ kind: "jsonl", binding: "csv-only", fields: [
            { name: "id", pointer: "/id", type: "int64", required: true },
            { name: "name", pointer: "/customer/name", type: "string", required: true }
          ] });
        } else {
          expect(body.pipeline.mapping[0].name).toBe("displayName");
          expect(body.pipeline.routes.errors).toEqual({ kind: "jsonl", binding: "error-file" });
        }
        pipeline = body.pipeline; revision++;
      }
      json = { definitionId: "orders", revision, lastPublishedVersion: published ? 1 : 0,
        pipeline, updatedAt: "2026-10-21T12:00:00Z" };
    } else if (path.endsWith("/validate")) {
      expect(request.postDataJSON()).toEqual({ expectedRevision: revision });
      json = { revision, definitionVersion: 1, contentHash: hash, diagnostics: [] };
    } else if (path.endsWith("/publish")) {
      expect(request.postDataJSON()).toEqual({ expectedRevision: revision, definitionVersion: 1, contentHash: hash });
      published = true; json = { status: "published", version: tuple };
    } else if (path.endsWith("/versions")) json = { items: published ? [tuple] : [], nextCursor: null };
    else if (path.endsWith("/versions/1") && published) json = { version: tuple, pipeline };
    else return route.fulfill({ status: 404, json: { code: "etl.definition-not-found" } });
    await route.fulfill({ json });
  });
  return tuple;
}

for (const width of [320, 768, 1024, 1440]) {
  test(`connector authoring preserves publication and fits ${width}px`, async ({ page }) => {
    await page.setViewportSize({ width, height: 900 });
    const errors: string[] = [];
    page.on("pageerror", error => errors.push(error.message));
    const tuple = await arrangeApi(page);
    await page.goto("/etl-connectors.html");
    await page.getByRole("button", { name: "New definition", exact: true }).click();
    const createDialog = page.getByRole("dialog", { name: "New ETL definition" });
    await createDialog.getByRole("textbox").fill("orders");
    await createDialog.getByRole("button", { name: "Create draft", exact: true }).press("Enter");
    await expect(page.getByRole("heading", { name: "Draft: orders", exact: true })).toBeVisible();
    await page.getByLabel("Output field 1 name", { exact: true }).fill("displayName");
    await page.getByLabel("Rejected records destination kind", { exact: true }).selectOption("jsonl");
    await page.getByLabel("Rejected records binding", { exact: true }).selectOption("error-file");
    await expect(page.getByRole("button", { name: "Validate saved revision", exact: true })).toBeDisabled();
    await page.getByRole("button", { name: "Save draft", exact: true }).click();
    await expect(page.locator(".studio-toast").getByText("Saved orders at revision 2.", { exact: true })).toBeVisible();
    await page.getByRole("button", { name: "Validate saved revision", exact: true }).click();
    const publishButton = page.getByRole("button", { name: "Publish immutable version", exact: true });
    const primaryColors = await page.getByRole("button", { name: "New definition", exact: true }).evaluate(element => {
      const style = getComputedStyle(element);
      return { background: style.backgroundColor, text: style.color };
    });
    await expect(publishButton).toHaveCSS("background-color", primaryColors.background);
    await expect(publishButton).toHaveCSS("color", primaryColors.text);
    await publishButton.click();
    const dialog = page.getByRole("dialog", { name: "Publish immutable ETL version?" });
    await expect(dialog).toBeVisible();
    await dialog.getByRole("button", { name: "Publish version", exact: true }).press("Enter");
    await expect(page.getByText(`Selection: ${tuple.selection}`, { exact: false })).toBeVisible();
    await page.getByLabel("Pipeline catalog", { exact: true }).selectOption("connector");
    await page.getByLabel("Published ETL definition", { exact: true }).selectOption("orders");
    await page.getByLabel("Published ETL version", { exact: true }).selectOption(tuple.selection);
    await expect(page.getByLabel("Saved pipeline selection", { exact: true })).toHaveText(tuple.selection);
    await expect.poll(() => page.evaluate(() => document.documentElement.scrollWidth <= document.documentElement.clientWidth)).toBe(true);
    expect(errors).toEqual([]);
    await page.getByRole("heading", { name: "Connector pipelines", exact: true }).scrollIntoViewIfNeeded();
    await page.screenshot({ path: `/tmp/3151-studio-connectors-${width}.png` });
    await page.getByRole("heading", { name: "Source connector", exact: true }).scrollIntoViewIfNeeded();
    await page.screenshot({ path: `/tmp/3151-studio-connector-source-${width}.png` });
    await page.getByLabel("Source kind", { exact: true }).focus();
    await page.keyboard.press("Tab");
    await expect(page.getByLabel("Source HTTP binding", { exact: true })).toBeFocused();
    await page.addScriptTag({ content: axe.source });
    const violations = await page.evaluate(async () => (await (window as unknown as {
      axe: { run(element: Element, options: RunOptions): Promise<AxeResults> }
    }).axe.run(document.querySelector("main")!, {
      runOnly: { type: "tag", values: ["wcag2a", "wcag2aa", "wcag21aa"] }
    })).violations.map(item => ({ id: item.id, nodes: item.nodes.map(node => node.target) })));
    expect(violations).toEqual([]);
  });
}

// Spec 1190: a JSON Lines source selects from the host's source file approvals and edits only its fields.
for (const width of [320, 1024]) {
  test(`JSON Lines source authoring saves its fields and fits ${width}px`, async ({ page }) => {
    await page.setViewportSize({ width, height: 900 });
    const errors: string[] = [];
    page.on("pageerror", error => errors.push(error.message));
    await arrangeApi(page, { jsonlSource: true });
    await page.goto("/etl-connectors.html");
    await page.getByRole("button", { name: "New definition", exact: true }).click();
    const createDialog = page.getByRole("dialog", { name: "New ETL definition" });
    await createDialog.getByRole("textbox").fill("orders");
    await createDialog.getByRole("button", { name: "Create draft", exact: true }).press("Enter");
    await expect(page.getByRole("heading", { name: "Draft: orders", exact: true })).toBeVisible();
    await page.getByLabel("Source kind", { exact: true }).selectOption("jsonl");
    const binding = page.getByLabel("Source file binding", { exact: true });
    await expect(binding.locator("option")).toHaveText(["Choose an approved binding", "source (not approved for Source file)", "csv-only"]);
    await binding.selectOption("csv-only");
    await expect(page.getByText("Records JSON Pointer", { exact: true })).toHaveCount(0);
    await page.getByLabel("JSON Lines field 2 pointer", { exact: true }).fill("/customer/name");
    await page.getByRole("button", { name: "Save draft", exact: true }).click();
    await expect(page.locator(".studio-toast").getByText("Saved orders at revision 2.", { exact: true })).toBeVisible();
    await expect.poll(() => page.evaluate(() => document.documentElement.scrollWidth <= document.documentElement.clientWidth)).toBe(true);
    await page.getByRole("heading", { name: "Source connector", exact: true }).scrollIntoViewIfNeeded();
    await page.screenshot({ path: `/tmp/1190-studio-jsonl-source-${width}.png` });
    await page.addScriptTag({ content: axe.source });
    const violations = await page.evaluate(async () => (await (window as unknown as {
      axe: { run(element: Element, options: RunOptions): Promise<AxeResults> }
    }).axe.run(document.querySelector("main")!, {
      runOnly: { type: "tag", values: ["wcag2a", "wcag2aa", "wcag21aa"] }
    })).violations.map(item => ({ id: item.id, nodes: item.nodes.map(node => node.target) })));
    expect(violations).toEqual([]);
    expect(errors).toEqual([]);
  });
}

// Spec 1191: a CSV route selects from the host's output folder approvals and saves its destination kind.
for (const width of [320, 1024]) {
  test(`CSV destination authoring saves the route and fits ${width}px`, async ({ page }) => {
    await page.setViewportSize({ width, height: 900 });
    const errors: string[] = [];
    page.on("pageerror", error => errors.push(error.message));
    await arrangeApi(page, { csvErrors: true });
    await page.goto("/etl-connectors.html");
    await page.getByRole("button", { name: "New definition", exact: true }).click();
    const createDialog = page.getByRole("dialog", { name: "New ETL definition" });
    await createDialog.getByRole("textbox").fill("orders");
    await createDialog.getByRole("button", { name: "Create draft", exact: true }).press("Enter");
    await expect(page.getByRole("heading", { name: "Draft: orders", exact: true })).toBeVisible();
    await page.getByLabel("Rejected records destination kind", { exact: true }).selectOption("csv");
    const binding = page.getByLabel("Rejected records binding", { exact: true });
    await expect(binding.locator("option", { hasText: "error-file" })).toHaveCount(1);
    await binding.selectOption("error-file");
    await expect(page.getByText("Choose a host-approved rejected file output binding.", { exact: true })).toBeVisible();
    await page.getByRole("button", { name: "Save draft", exact: true }).click();
    await expect(page.locator(".studio-toast").getByText("Saved orders at revision 2.", { exact: true })).toBeVisible();
    await expect.poll(() => page.evaluate(() => document.documentElement.scrollWidth <= document.documentElement.clientWidth)).toBe(true);
    await page.getByLabel("Rejected records destination kind", { exact: true }).scrollIntoViewIfNeeded();
    await page.screenshot({ path: `/tmp/1191-studio-csv-route-${width}.png` });
    await page.addScriptTag({ content: axe.source });
    const violations = await page.evaluate(async () => (await (window as unknown as {
      axe: { run(element: Element, options: RunOptions): Promise<AxeResults> }
    }).axe.run(document.querySelector("main")!, {
      runOnly: { type: "tag", values: ["wcag2a", "wcag2aa", "wcag21aa"] }
    })).violations.map(item => ({ id: item.id, nodes: item.nodes.map(node => node.target) })));
    expect(violations).toEqual([]);
    expect(errors).toEqual([]);
  });
}

// Spec 1197: the accepted route can be a keyed SQL table selected from the host's SQL approvals.
for (const width of [320, 1024]) {
  test(`SQL destination authoring saves the table and key and fits ${width}px`, async ({ page }) => {
    await page.setViewportSize({ width, height: 900 });
    const errors: string[] = [];
    page.on("pageerror", error => errors.push(error.message));
    await arrangeApi(page, { sqlMain: true });
    await page.goto("/etl-connectors.html");
    await page.getByRole("button", { name: "New definition", exact: true }).click();
    const createDialog = page.getByRole("dialog", { name: "New ETL definition" });
    await createDialog.getByRole("textbox").fill("orders");
    await createDialog.getByRole("button", { name: "Create draft", exact: true }).press("Enter");
    await expect(page.getByRole("heading", { name: "Draft: orders", exact: true })).toBeVisible();
    await expect(page.getByLabel("Rejected records destination kind", { exact: true }).locator("option", { hasText: "SQL table" })).toHaveCount(0);
    await page.getByLabel("Accepted records destination kind", { exact: true }).selectOption("sql");
    await page.getByLabel("Accepted records binding", { exact: true }).selectOption("warehouse");
    await page.getByLabel("Accepted records table", { exact: true }).fill("dbo.people");
    await page.getByRole("checkbox", { name: "name", exact: true }).check();
    await page.getByRole("button", { name: "Save draft", exact: true }).click();
    await expect(page.locator(".studio-toast").getByText("Saved orders at revision 2.", { exact: true })).toBeVisible();
    await expect.poll(() => page.evaluate(() => document.documentElement.scrollWidth <= document.documentElement.clientWidth)).toBe(true);
    await page.getByLabel("Accepted records table", { exact: true }).scrollIntoViewIfNeeded();
    await page.screenshot({ path: `/tmp/1197-studio-sql-route-${width}.png` });
    await page.addScriptTag({ content: axe.source });
    const violations = await page.evaluate(async () => (await (window as unknown as {
      axe: { run(element: Element, options: RunOptions): Promise<AxeResults> }
    }).axe.run(document.querySelector("main")!, {
      runOnly: { type: "tag", values: ["wcag2a", "wcag2aa", "wcag21aa"] }
    })).violations.map(item => ({ id: item.id, nodes: item.nodes.map(node => node.target) })));
    expect(violations).toEqual([]);
    expect(errors).toEqual([]);
  });
}

// Spec 1200: the accepted route can be a RabbitMQ exchange selected from the host's RabbitMQ approvals.
for (const width of [320, 1024]) {
  test(`RabbitMQ destination authoring saves the exchange and routing key and fits ${width}px`, async ({ page }) => {
    await page.setViewportSize({ width, height: 900 });
    const errors: string[] = [];
    page.on("pageerror", error => errors.push(error.message));
    await arrangeApi(page, { rabbitMqMain: true });
    await page.goto("/etl-connectors.html");
    await page.getByRole("button", { name: "New definition", exact: true }).click();
    const createDialog = page.getByRole("dialog", { name: "New ETL definition" });
    await createDialog.getByRole("textbox").fill("orders");
    await createDialog.getByRole("button", { name: "Create draft", exact: true }).press("Enter");
    await expect(page.getByRole("heading", { name: "Draft: orders", exact: true })).toBeVisible();
    await expect(page.getByLabel("Rejected records destination kind", { exact: true }).locator("option", { hasText: "RabbitMQ exchange" })).toHaveCount(0);
    await page.getByLabel("Accepted records destination kind", { exact: true }).selectOption("rabbitmq");
    await page.getByLabel("Accepted records binding", { exact: true }).selectOption("events");
    await page.getByLabel("Accepted records exchange", { exact: true }).fill("etl.people");
    await page.getByLabel("Accepted records routing key", { exact: true }).fill("people.loaded");
    await page.getByRole("button", { name: "Save draft", exact: true }).click();
    await expect(page.locator(".studio-toast").getByText("Saved orders at revision 2.", { exact: true })).toBeVisible();
    await expect.poll(() => page.evaluate(() => document.documentElement.scrollWidth <= document.documentElement.clientWidth)).toBe(true);
    await page.getByLabel("Accepted records routing key", { exact: true }).scrollIntoViewIfNeeded();
    await page.screenshot({ path: `/tmp/1200-studio-rabbitmq-route-${width}.png` });
    await page.addScriptTag({ content: axe.source });
    const violations = await page.evaluate(async () => (await (window as unknown as {
      axe: { run(element: Element, options: RunOptions): Promise<AxeResults> }
    }).axe.run(document.querySelector("main")!, {
      runOnly: { type: "tag", values: ["wcag2a", "wcag2aa", "wcag21aa"] }
    })).violations.map(item => ({ id: item.id, nodes: item.nodes.map(node => node.target) })));
    expect(violations).toEqual([]);
    expect(errors).toEqual([]);
  });
}

// Spec 1192: a cursor-paginated HTTP source can run continuously with a poll interval.
for (const width of [320, 1024]) {
  test(`continuous run authoring saves the poll interval and fits ${width}px`, async ({ page }) => {
    await page.setViewportSize({ width, height: 900 });
    const errors: string[] = [];
    page.on("pageerror", error => errors.push(error.message));
    await arrangeApi(page, { continuous: true });
    await page.goto("/etl-connectors.html");
    await page.getByRole("button", { name: "New definition", exact: true }).click();
    const createDialog = page.getByRole("dialog", { name: "New ETL definition" });
    await createDialog.getByRole("textbox").fill("orders");
    await createDialog.getByRole("button", { name: "Create draft", exact: true }).press("Enter");
    await expect(page.getByRole("heading", { name: "Draft: orders", exact: true })).toBeVisible();
    await page.getByLabel("Run continuously", { exact: true }).check();
    await expect(page.getByText("Continuous connector pipeline", { exact: true })).toBeVisible();
    await page.getByLabel("Poll interval (seconds)", { exact: true }).fill("30");
    await page.getByRole("button", { name: "Save draft", exact: true }).click();
    await expect(page.locator(".studio-toast").getByText("Saved orders at revision 2.", { exact: true })).toBeVisible();
    await expect.poll(() => page.evaluate(() => document.documentElement.scrollWidth <= document.documentElement.clientWidth)).toBe(true);
    await page.getByLabel("Poll interval (seconds)", { exact: true }).scrollIntoViewIfNeeded();
    await page.screenshot({ path: `/tmp/1192-studio-continuous-${width}.png` });
    await page.addScriptTag({ content: axe.source });
    const violations = await page.evaluate(async () => (await (window as unknown as {
      axe: { run(element: Element, options: RunOptions): Promise<AxeResults> }
    }).axe.run(document.querySelector("main")!, {
      runOnly: { type: "tag", values: ["wcag2a", "wcag2aa", "wcag21aa"] }
    })).violations.map(item => ({ id: item.id, nodes: item.nodes.map(node => node.target) })));
    expect(violations).toEqual([]);
    expect(errors).toEqual([]);
  });
}

// Spec 1198: the source can be a SQL table snapshot selected from the host's SQL source approvals.
for (const width of [320, 1024]) {
  test(`SQL source authoring saves the table, columns and key and fits ${width}px`, async ({ page }) => {
    await page.setViewportSize({ width, height: 900 });
    const errors: string[] = [];
    page.on("pageerror", error => errors.push(error.message));
    await arrangeApi(page, { sqlSource: true });
    await page.goto("/etl-connectors.html");
    await page.getByRole("button", { name: "New definition", exact: true }).click();
    const createDialog = page.getByRole("dialog", { name: "New ETL definition" });
    await createDialog.getByRole("textbox").fill("orders");
    await createDialog.getByRole("button", { name: "Create draft", exact: true }).press("Enter");
    await expect(page.getByRole("heading", { name: "Draft: orders", exact: true })).toBeVisible();
    await page.getByLabel("Source kind", { exact: true }).selectOption("sql");
    await expect(page.getByText("Read one snapshot of the table", { exact: true })).toBeVisible();
    await expect(page.getByText("Run continuously", { exact: true })).toHaveCount(0);
    await page.getByLabel("Source SQL binding", { exact: true }).selectOption("people-db");
    await page.getByLabel("Source table", { exact: true }).fill("dbo.people");
    await page.getByRole("group", { name: "Source key" }).getByRole("checkbox", { name: "id", exact: true }).check();
    await page.getByLabel("Watermark column", { exact: true }).selectOption("id");
    await page.getByRole("button", { name: "Save draft", exact: true }).click();
    await expect(page.locator(".studio-toast").getByText("Saved orders at revision 2.", { exact: true })).toBeVisible();
    await expect.poll(() => page.evaluate(() => document.documentElement.scrollWidth <= document.documentElement.clientWidth)).toBe(true);
    await page.getByLabel("Source table", { exact: true }).scrollIntoViewIfNeeded();
    await page.screenshot({ path: `/tmp/1198-studio-sql-source-${width}.png` });
    await page.addScriptTag({ content: axe.source });
    const violations = await page.evaluate(async () => (await (window as unknown as {
      axe: { run(element: Element, options: RunOptions): Promise<AxeResults> }
    }).axe.run(document.querySelector("main")!, {
      runOnly: { type: "tag", values: ["wcag2a", "wcag2aa", "wcag21aa"] }
    })).violations.map(item => ({ id: item.id, nodes: item.nodes.map(node => node.target) })));
    expect(violations).toEqual([]);
    expect(errors).toEqual([]);
  });
}

// Spec 1201: the source can be a RabbitMQ queue selected from the host's RabbitMQ source approvals.
for (const width of [320, 1024]) {
  test(`RabbitMQ source authoring saves the queue and message maximum and fits ${width}px`, async ({ page }) => {
    await page.setViewportSize({ width, height: 900 });
    const errors: string[] = [];
    page.on("pageerror", error => errors.push(error.message));
    await arrangeApi(page, { rabbitMqSource: true });
    await page.goto("/etl-connectors.html");
    await page.getByRole("button", { name: "New definition", exact: true }).click();
    const createDialog = page.getByRole("dialog", { name: "New ETL definition" });
    await createDialog.getByRole("textbox").fill("orders");
    await createDialog.getByRole("button", { name: "Create draft", exact: true }).press("Enter");
    await expect(page.getByRole("heading", { name: "Draft: orders", exact: true })).toBeVisible();
    await page.getByLabel("Source kind", { exact: true }).selectOption("rabbitmq");
    await expect(page.getByText("Drain queued messages", { exact: true })).toBeVisible();
    await expect(page.getByText("Run continuously", { exact: true })).toHaveCount(0);
    await page.getByLabel("Source RabbitMQ binding", { exact: true }).selectOption("events-in");
    await page.getByLabel("Source queue", { exact: true }).fill("people");
    await page.getByLabel("Messages per run", { exact: true }).fill("250");
    await page.getByRole("button", { name: "Save draft", exact: true }).click();
    await expect(page.locator(".studio-toast").getByText("Saved orders at revision 2.", { exact: true })).toBeVisible();
    await expect.poll(() => page.evaluate(() => document.documentElement.scrollWidth <= document.documentElement.clientWidth)).toBe(true);
    await page.getByLabel("Source queue", { exact: true }).scrollIntoViewIfNeeded();
    await page.screenshot({ path: `/tmp/1201-studio-rabbitmq-source-${width}.png` });
    await page.addScriptTag({ content: axe.source });
    const violations = await page.evaluate(async () => (await (window as unknown as {
      axe: { run(element: Element, options: RunOptions): Promise<AxeResults> }
    }).axe.run(document.querySelector("main")!, {
      runOnly: { type: "tag", values: ["wcag2a", "wcag2aa", "wcag21aa"] }
    })).violations.map(item => ({ id: item.id, nodes: item.nodes.map(node => node.target) })));
    expect(violations).toEqual([]);
    expect(errors).toEqual([]);
  });
}
