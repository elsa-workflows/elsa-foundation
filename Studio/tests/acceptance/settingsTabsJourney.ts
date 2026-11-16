import { expect, type Locator, type Page, type TestInfo } from "@playwright/test";
import axe from "axe-core";

async function assertLinkedTabs(scope: Locator) {
  const errors = await scope.evaluate(element => {
    const problems: string[] = [];
    const ids = Array.from(element.querySelectorAll("[id]"), node => node.id);
    if (new Set(ids).size !== ids.length) problems.push("Duplicate IDs");
    const tabs = element.querySelectorAll('[role="tab"]');
    if (!tabs.length) problems.push("No tabs rendered");
    for (const tab of tabs) {
      const panel = document.getElementById(tab.getAttribute("aria-controls") ?? "");
      const selected = tab.getAttribute("aria-selected") === "true";
      if (!panel || panel.getAttribute("role") !== "tabpanel" || panel.getAttribute("aria-labelledby") !== tab.id) {
        problems.push(`Missing or incorrectly labelled panel: ${tab.textContent}`);
      } else if (panel.hidden === selected || Boolean(panel.getClientRects().length) !== selected || (selected && !panel.textContent?.trim())) {
        problems.push(`Incorrect panel visibility or missing selected content: ${tab.textContent}`);
      }
    }
    return problems;
  });
  expect(errors).toEqual([]);
}

async function selectByKey(list: Locator, key: string, label: RegExp) {
  const selected = list.getByRole("tab", { selected: true });
  await selected.focus();
  await selected.press(key);
  const next = list.getByRole("tab", { selected: true });
  await expect(next).toHaveText(label);
  await expect(next).toBeFocused();
}

export async function settingsTabsJourney(page: Page, info: TestInfo, capture: (page: Page, info: TestInfo, name: string) => Promise<void>) {
  // The app scrolls inside its viewport. Keep keyboard-state screenshots and also expose
  // actual panel content for visual review without changing the focused control.
  const read = async (name: string, target: Locator) => {
    const focused = await page.evaluateHandle(() => document.activeElement);
    await target.scrollIntoViewIfNeeded();
    await target.evaluate(element => {
      for (let parent = element.parentElement; parent; parent = parent.parentElement) parent.scrollLeft = 0;
    });
    await expect(target).toBeInViewport();
    expect(await focused.evaluate(element => element === document.activeElement)).toBe(true);
    await capture(page, info, name);
    await focused.dispose();
  };
  for (const [route, selector, prefix] of [
    ["modules", ".modules-page", "modules"],
    ["package-feeds", ".package-feeds-page", "feeds"],
    ["feature-management", ".feature-management-page", "features"]
  ]) {
    await page.goto(`/settings/${route}`);
    const section = page.locator(`${selector}:visible`);
    const hosts = section.getByRole("tablist", { name: "Hosts", exact: true });
    await expect(hosts).toBeVisible();
    for (const host of ["studio", "server"]) {
      await selectByKey(hosts, host === "studio" ? "Home" : "End", new RegExp(`^${host}`, "i"));
      await expect(section.getByRole("button", { name: /^Refresh/ }).first()).toBeEnabled();
      await assertLinkedTabs(section);
      await capture(page, info, `${prefix}-${host}`);
      if (prefix === "modules" && host === "server") await read("modules-unavailable-content", section.locator(".studio-alert").filter({ hasText: /module registry is unavailable/ }));
      if (prefix === "feeds" && host === "studio") {
        await read("feeds-operations-content", section.getByRole("heading", { name: "Operations", exact: true }));
        await read("feeds-retention-content", section.getByRole("heading", { name: "Retention", exact: true }));
        await read("feeds-ledger-content", section.getByRole("heading", { name: "Change ledger", exact: true }));
      }
      if (prefix === "feeds" && host === "server") await read("feeds-unavailable-content", section.locator(".studio-alert").filter({ hasText: /module registry is unavailable/ }));
      if (prefix === "features") {
        const feature = section.locator(".feature-management-feature-button").first();
        await read(`features-${host}-list-content`, feature);
        await feature.click();
        await read(`features-${host}-inspector-content`, section.locator(".feature-management-inspector-heading"));
      }
      const inspector = section.getByRole("tablist", { name: "Inspector", exact: true });
      if (prefix === "modules" && await inspector.count()) {
        await selectByKey(inspector, "Home", /^Overview$/);
        if (host === "studio") await read("modules-overview-content", section.getByRole("heading", { name: "Overview", exact: true }));
        for (const label of ["Contributions", "Package", "Manifest", "Diagnostics"]) {
          await selectByKey(inspector, "ArrowRight", new RegExp(`^${label}$`));
          await assertLinkedTabs(section);
          if (host === "studio" && ["Manifest", "Diagnostics"].includes(label)) {
            await capture(page, info, `modules-${label.toLowerCase()}`);
            await read(`modules-${label.toLowerCase()}-content`, section.getByRole("heading", { name: label, exact: true }));
          }
        }
      }
    }
    if (prefix === "features") {
      const pattern = /\/modularity\/features(?:\?.*)?$/;
      await page.route(pattern, route => route.fulfill({ status: 503, contentType: "application/problem+json", body: JSON.stringify({ title: "Acceptance catalog unavailable", status: 503 }) }));
      try {
        await section.getByRole("button", { name: "Refresh features", exact: true }).click();
        const panel = section.locator(".feature-management-host-panel");
        const alert = panel.locator(":scope > .studio-alert[data-tone='danger']");
        await expect(alert).toBeVisible();
        await info.attach("controlled-catalog-error", { body: "Injected HTTP 503 at the selected feature catalog request to inspect feedback spacing and recovery; this is a controlled UI fault, not an observed backend outage.", contentType: "text/plain" });
        const gap = await panel.evaluate(element => ({
          actual: element.querySelector(".feature-management-layout")!.getBoundingClientRect().top - element.querySelector(".studio-alert[data-tone='danger']")!.getBoundingClientRect().bottom,
          expected: parseFloat(getComputedStyle(element.parentElement!).rowGap)
        }));
        expect(gap.expected).toBeGreaterThan(0);
        expect(gap.actual, "Feature feedback must retain the page gap").toBeCloseTo(gap.expected, 1);
        await assertLinkedTabs(section);
        await capture(page, info, "features-error");
      } finally { await page.unroute(pattern); }
      await section.getByRole("button", { name: "Refresh features", exact: true }).click();
      await expect(section.locator(".feature-management-host-panel > .studio-alert[data-tone='danger']")).toHaveCount(0);
      await expect(section.getByRole("button", { name: "Refresh features", exact: true })).toBeEnabled();
      await assertLinkedTabs(section);
    }
    await page.addScriptTag({ content: axe.source });
    const violations = await section.evaluate(async element => {
      const result = await (window as unknown as Window & { axe: typeof axe }).axe.run(element, { runOnly: { type: "rule", values: ["aria-valid-attr-value"] } });
      return result.violations.map(violation => violation.id);
    });
    expect(violations).toEqual([]);
  }

  await page.goto("/settings/theme-builder");
  const theme = page.locator(".theme-builder-page:visible");
  const views = theme.getByRole("tablist", { name: "Theme Builder views", exact: true });
  await expect(views).toBeVisible();
  await assertLinkedTabs(theme);
  await capture(page, info, "theme-overview");
  await read("theme-overview-content", theme.getByRole("textbox", { name: "Name", exact: true }));
  await theme.getByRole("button", { name: "Duplicate", exact: true }).click();
  const name = theme.getByRole("textbox", { name: "Name", exact: true });
  await expect(name).toBeEditable();
  const edited = `Panel navigation ${info.project.name}`;
  await name.fill(edited);
  await selectByKey(views, "ArrowRight", /^Tokens$/);
  const primary = theme.getByRole("textbox", { name: "primary", exact: true });
  const editedPrimary = "oklch(0.5 0.15 250)";
  await primary.fill(editedPrimary);
  await assertLinkedTabs(theme);
  await capture(page, info, "theme-tokens");
  await read("theme-tokens-content", primary);
  const modes = theme.getByRole("tablist", { name: "Theme mode", exact: true });
  for (const [key, label] of [["Home", "Light"], ["ArrowRight", "Dark"], ["ArrowRight", "Dim"], ["End", "High contrast"]]) {
    await selectByKey(modes, key, new RegExp(`^${label}$`));
    await assertLinkedTabs(theme);
  }
  await capture(page, info, "theme-mode-last");
  await read("theme-mode-last-content", primary);
  await selectByKey(modes, "Home", /^Light$/);
  await expect(primary).toHaveValue(editedPrimary);
  await selectByKey(views, "ArrowRight", /^Assets$/);
  await assertLinkedTabs(theme);
  await capture(page, info, "theme-assets");
  await selectByKey(views, "End", /^Preview$/);
  await assertLinkedTabs(theme);
  await capture(page, info, "theme-preview");
  await read("theme-preview-content", theme.locator(".theme-preview-card"));
  await read("theme-preview-validation", theme.getByRole("button", { name: "Apply preview", exact: true }));
  await selectByKey(views, "Home", /^Overview$/);
  await expect(name).toHaveValue(edited);
  await assertLinkedTabs(theme);
  await capture(page, info, "theme-edited");
  await selectByKey(views, "ArrowRight", /^Tokens$/);
  await expect(primary).toHaveValue(editedPrimary);
  await selectByKey(views, "Home", /^Overview$/);
  await expect(name).toHaveValue(edited);
  await theme.getByRole("button", { name: "Delete", exact: true }).click();
  await page.getByRole("dialog", { name: "Delete theme", exact: true }).getByRole("button", { name: "Delete theme", exact: true }).click();
  await expect(name).toBeDisabled();
  await assertLinkedTabs(theme);
  await page.addScriptTag({ content: axe.source });
  const violations = await theme.evaluate(async element => {
    const result = await (window as unknown as Window & { axe: typeof axe }).axe.run(element, { runOnly: { type: "rule", values: ["aria-valid-attr-value"] } });
    return result.violations.map(violation => violation.id);
  });
  expect(violations).toEqual([]);
}
