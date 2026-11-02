import { expect, test, type Page } from "@playwright/test";
import axe, { type AxeResults, type RunOptions } from "axe-core";
import { createDefaultEtlConnectorPipeline } from "../../../Extensions/Etl/studio/Elsa.Studio.Etl/Client/src/connectorAuthoringHelpers";

// Browser-level product components against a deterministic API fixture. Actual Workbench HTTP acceptance is T019.
async function arrangeApi(page: Page) {
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
      { alias: "error-file", role: "ErrorOutput", kind: "jsonl" }
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
        expect(body.pipeline.mapping[0].name).toBe("displayName");
        expect(body.pipeline.routes.errors).toEqual({ kind: "jsonl", binding: "error-file" });
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
    await expect(page.getByText("Saved orders at revision 2.", { exact: false })).toBeVisible();
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
