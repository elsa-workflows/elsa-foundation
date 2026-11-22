import { expect, test } from "@playwright/test";

for (const style of ["quiet", "focus", "open"]) for (const theme of ["light", "dark"]) {
  test(`draft header has a clear title and secondary status (${style}, ${theme})`, async ({ page }, testInfo) => {
    await page.setViewportSize({ width: 1440, height: 950 });
    await page.addInitScript(style => {
      localStorage.setItem("elsa-studio-workspace-style", style);
      localStorage.setItem("elsa-studio-color-scheme", "drift");
    }, style);
    await page.goto(`/?mode=activity-authoring&theme=${theme}`);
    const title = page.getByRole("heading", { name: "Prepare order quote", exact: true });
    const saved = page.locator(".ad-save-chip");
    await expect(title).toBeVisible();
    await expect(page.getByRole("complementary", { name: "Inspector panel", exact: true })).toBeVisible();
    await expect(saved).toHaveText("Saved");
    const titleBox = (await title.boundingBox())!;
    const statusBox = (await saved.boundingBox())!;
    expect(statusBox.y).toBeGreaterThanOrEqual(titleBox.y + titleBox.height);
    const back = page.getByRole("button", { name: "Back to Activity Definition", exact: true });
    await back.focus();
    await expect(back).toBeFocused();
    await title.click();
    const publication = (await page.getByRole("button", { name: "Review & publish", exact: true }).boundingBox())!;
    expect(publication.height).toBeLessThan(48);
    await page.screenshot({ path: testInfo.outputPath("draft-header-desktop.png") });

    await page.setViewportSize({ width: 390, height: 844 });
    const longName = "Reusable activity with a deliberately long name for order processing and fulfilment";
    await page.goto(`/?mode=activity-authoring&theme=${theme}&name=${encodeURIComponent(longName)}`);
    const longTitle = page.getByRole("heading", { name: longName, exact: true });
    await expect(longTitle).toBeVisible();
    await expect(page.getByRole("complementary", { name: "Inspector panel", exact: true })).toBeVisible();
    expect(await longTitle.evaluate(element => element.scrollWidth <= element.clientWidth && element.scrollHeight <= element.clientHeight)).toBe(true);
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
    const toolbar = (await page.locator(".ad-draft-editor-toolbar").boundingBox())!;
    const tabs = (await page.getByRole("tablist", { name: "Activity Definition authoring views" }).boundingBox())!;
    expect(toolbar.y + toolbar.height).toBeLessThanOrEqual(tabs.y + 1);
    for (const name of ["Review & publish", "Test Run", "Save now"]) {
      const action = page.getByRole("button", { name, exact: true });
      await expect(action).toBeVisible();
      const box = (await action.boundingBox())!;
      expect(box.x).toBeGreaterThanOrEqual(0);
      expect(box.x + box.width).toBeLessThanOrEqual(391);
    }
    await page.screenshot({ path: testInfo.outputPath("draft-header-narrow.png") });
  });
}

test("Activity Definition authoring tabs keep focus as they move between the canvas and page", async ({ page }) => {
  await page.goto("/?mode=activity-authoring&theme=dark");
  const views = page.getByRole("tablist", { name: "Activity Definition authoring views" });
  const designer = views.getByRole("tab", { name: "Designer", exact: true });
  const inputs = views.getByRole("tab", { name: /^Inputs/ });

  await inputs.click();
  await expect(inputs).toBeFocused();
  await designer.click();
  await expect(designer).toBeFocused();
});

for (const theme of ["light", "dark"]) {
  test(`selected Activity Definition tab text meets contrast in ${theme} mode`, async ({ page }) => {
    await page.goto(`/?mode=activity-authoring&theme=${theme}`);
    const selectedTab = page.getByRole("tablist", { name: "Activity Definition authoring views" }).getByRole("tab", { name: /^Inputs/ });
    await selectedTab.click();

    const contrast = await selectedTab.evaluate(element => {
      const context = document.createElement("canvas").getContext("2d");
      if (!context) return { ratio: 0, foreground: "canvas unavailable", background: "" };
      const parseColor = (color: string) => {
        context.clearRect(0, 0, 1, 1);
        context.fillStyle = color;
        context.fillRect(0, 0, 1, 1);
        const [red, green, blue, alpha] = context.getImageData(0, 0, 1, 1).data;
        return alpha ? [red, green, blue] : null;
      };
      const foreground = parseColor(getComputedStyle(element).color);
      let backgroundElement: Element | null = element;
      let background: number[] | null = null;
      let backgroundColor = "";
      while (backgroundElement && !background) {
        const style = getComputedStyle(backgroundElement);
        if (style.backgroundColor !== "rgba(0, 0, 0, 0)" && style.backgroundColor !== "transparent") {
          backgroundColor = style.backgroundColor;
          background = parseColor(style.backgroundColor);
        }
        backgroundElement = backgroundElement.parentElement;
      }
      if (!foreground || !background) return { ratio: 0, foreground: getComputedStyle(element).color, background: backgroundColor };
      const luminance = (channels: number[]) => {
        const [red, green, blue] = channels.map(channel => {
          const value = channel / 255;
          return value <= 0.04045 ? value / 12.92 : ((value + 0.055) / 1.055) ** 2.4;
        });
        return 0.2126 * red + 0.7152 * green + 0.0722 * blue;
      };
      const foregroundLuminance = luminance(foreground);
      const backgroundLuminance = luminance(background);
      return {
        ratio: (Math.max(foregroundLuminance, backgroundLuminance) + 0.05) / (Math.min(foregroundLuminance, backgroundLuminance) + 0.05),
        foreground: getComputedStyle(element).color,
        background: backgroundColor
      };
    });

    expect(contrast.ratio, JSON.stringify(contrast)).toBeGreaterThanOrEqual(4.5);
  });
}

for (const theme of ["light", "dark"]) {
  test(`activity variables have a direct tab and share the root inspector editor (${theme})`, async ({ page }, testInfo) => {
    await page.setViewportSize({ width: 1440, height: 950 });
    await page.goto(`/?mode=activity-authoring&theme=${theme}`);
    const views = page.getByRole("tablist", { name: "Activity Definition authoring views" });
    await views.getByRole("tab", { name: "Variables", exact: true }).click();
    const variables = page.getByRole("tabpanel", { name: "Variables", exact: true });
    await expect(variables.getByText("Private to each execution of this activity and available throughout its designer.")).toBeVisible();
    await variables.getByRole("button", { name: "Add variable", exact: true }).click();
    const storage = variables.getByRole("combobox", { name: "Variable storage driver", exact: true });
    await expect(storage).toBeEnabled();
    expect(await storage.locator("option").evaluateAll(options => options.map(option => (option as HTMLOptionElement).value))).toEqual(["", "elsa.json"]);
    await storage.selectOption("elsa.json");
    await variables.getByRole("textbox", { name: "Variable name", exact: true }).fill("LineCount");
    await variables.getByRole("textbox", { name: "Variable default value", exact: true }).fill("0");
    await expect(page.locator(".ad-revision-gate")).toHaveCount(0);
    await expect(page.locator(".ad-save-chip")).toHaveText("Saved");
    const fieldset = variables.locator("fieldset.wf-container-variables");
    expect(await fieldset.evaluate(element => getComputedStyle(element).borderWidth)).toBe("0px");
    await page.screenshot({ path: testInfo.outputPath("activity-variables.png") });

    await views.getByRole("tab", { name: "Designer", exact: true }).click();
    const inspector = page.getByRole("complementary", { name: "Inspector panel", exact: true });
    await inspector.getByRole("tab", { name: "Variables", exact: true }).click();
    await expect(inspector.getByRole("textbox", { name: "Variable name", exact: true })).toHaveValue("LineCount");
    await expect(inspector.getByRole("textbox", { name: "Variable default value", exact: true })).toHaveValue("0");
    expect(await inspector.locator("fieldset.wf-container-variables").evaluate(element => getComputedStyle(element).borderWidth)).toBe("0px");
    await inspector.getByRole("button", { name: "Add variable", exact: true }).click();
    await expect(page.locator(".ad-save-chip")).toHaveText("Saved");
    await page.reload();
    await views.getByRole("tab", { name: "Variables", exact: true }).click();
    await expect(variables.getByRole("textbox", { name: "Variable name", exact: true })).toHaveCount(2);
    await expect(variables.getByRole("textbox", { name: "Variable name", exact: true }).first()).toHaveValue("LineCount");

    await page.locator(".ad-authoring-advanced > summary").click();
    await page.getByRole("button", { name: "Edit code", exact: true }).click();
    const projection = JSON.parse(await page.getByRole("textbox", { name: "Activity Definition JSON" }).inputValue());
    expect(projection.implementation.payload.variables[0]).toMatchObject({ name: "LineCount", storageDriverKey: "elsa.json", initialValue: { syntax: "Literal", value: "0" } });
    expect(projection.contract.inputs.map((input: { name: string }) => input.name)).toEqual(["Subtotal", "Shipping"]);
    await expect(page.getByRole("tab", { name: "Code", exact: true })).toBeFocused();

    await page.setViewportSize({ width: 390, height: 844 });
    await views.getByRole("tab", { name: "Variables", exact: true }).click();
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
  });
}

test("edits a reusable activity interface without losing unapplied defaults or saved bindings", async ({ page }) => {
  await page.goto("/?mode=activity-authoring&theme=dark");
  await page.getByRole("tab", { name: /^Inputs \(\d+\)$/ }).click();
  await expect(page.getByText("Advanced interface details", { exact: true })).toBeVisible();
  await expect(page.getByRole("textbox", { name: "Stable reference key" })).toHaveValue("subtotal");
  await page.getByRole("button", { name: "Add input", exact: true }).click();
  await page.getByRole("textbox", { name: "Input name", exact: true }).fill("Discount");
  await page.getByRole("textbox", { name: "Input name", exact: true }).press("Enter");
  const member = page.getByRole("region", { name: "Discount", exact: true });
  await expect(member.getByRole("textbox", { name: "Name", exact: true })).toBeFocused();
  await member.getByRole("combobox", { name: "Type", exact: true }).selectOption("Decimal");
  await member.getByRole("combobox", { name: /^Default/ }).selectOption("literal");
  await member.getByRole("textbox", { name: "Value", exact: true }).fill("2.5");
  await expect(page.getByRole("button", { name: "Test Run", exact: true })).toBeDisabled();
  await page.getByRole("button", { name: "Shipping Number", exact: true }).click();
  await expect(member).toBeVisible();
  await expect(member.getByRole("textbox", { name: "Value", exact: true })).toHaveValue("2.5");
  await member.getByRole("textbox", { name: "Value", exact: true }).fill("2x");
  await expect(member.getByRole("textbox", { name: "Value", exact: true })).toHaveAttribute("aria-invalid", "true");
  await expect(member.getByRole("button", { name: "Apply default", exact: true })).toBeDisabled();
  await member.getByRole("textbox", { name: "Value", exact: true }).fill("2.5");
  await member.getByRole("button", { name: "Apply default", exact: true }).click();
  await expect(page.locator(".ad-save-chip")).toHaveText("Saved");
  await page.reload();
  await page.getByRole("tab", { name: /^Inputs \(\d+\)$/ }).click();
  await page.getByRole("button", { name: "Discount Number", exact: true }).click();
  await expect(member.getByRole("textbox", { name: "Value", exact: true })).toHaveValue("2.5");
  await page.locator(".ad-authoring-advanced > summary").click();
  await page.getByRole("button", { name: "Edit code", exact: true }).click();
  const projection = JSON.parse(await page.getByRole("textbox", { name: "Activity Definition JSON" }).inputValue());
  expect(projection.contract.inputs.find((input: { referenceKey: string }) => input.referenceKey === "discount").default).toEqual({ syntax: "Literal", value: 2.5 });
  expect(projection.implementation.payload.outputMappings).toEqual([{ outputReferenceKey: "amount", source: { syntax: "Input", value: { referenceKey: "subtotal" } } }]);
  expect(projection.implementation.payload.outcomeMappings).toEqual([{ sourceOutcomeReferenceKey: "done", boundaryOutcomeReferenceKey: "quoted" }]);
});

for (const theme of ["light", "dark"]) {
  test(`keeps focused interface editing inside a narrow ${theme} viewport`, async ({ page }) => {
    await page.setViewportSize({ width: 390, height: 844 });
    await page.goto(`/?mode=activity-authoring&theme=${theme}`);
    await page.getByRole("tab", { name: /^Inputs \(\d+\)$/ }).click();
    await expect(page.getByRole("region", { name: "Subtotal", exact: true })).toBeVisible();
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
    await page.getByRole("tab", { name: /^Outputs \(\d+\)$/ }).click();
    await page.getByRole("button", { name: "Add output", exact: true }).click();
    await expect(page.getByRole("textbox", { name: "Output name", exact: true })).toBeFocused();
    await page.getByRole("textbox", { name: "Output name", exact: true }).press("Escape");
    await expect(page.getByRole("button", { name: "Add output", exact: true })).toBeFocused();
  });
}

test("keeps existing members visible without misclassifying a capability outage", async ({ page }) => {
  await page.goto("/?mode=activity-authoring&theme=dark&capabilities=unavailable");
  await page.getByRole("tab", { name: /^Inputs \(\d+\)$/ }).click();
  await expect(page.getByText("Contract options unavailable", { exact: true })).toBeVisible();
  await expect(page.getByRole("region", { name: "Subtotal", exact: true })).toBeVisible();
  await expect(page.getByRole("button", { name: "Add input", exact: true })).toBeDisabled();
  await expect(page.getByText(/This mutable draft is invalid until/)).toHaveCount(0);
});

for (const theme of ["light", "dark", "material"]) for (const width of [390, 1440]) {
  test(`Add variable has shared themed hover, focus and disabled styling (${theme === "material" ? "custom material tokens" : theme}, ${width})`, async ({ page }) => {
    await page.setViewportSize({ width, height: 950 });
    await page.goto(`/?mode=activity-authoring&theme=${theme === "light" ? "light" : "dark"}`);
    await page.getByRole("tablist", { name: "Activity Definition authoring views" }).getByRole("tab", { name: "Variables", exact: true }).click();
    if (theme === "material") await page.evaluate(() => {
      document.documentElement.dataset.themeMaterial = "browser-fixture";
      document.documentElement.style.setProperty("--studio-surface", "var(--studio-accent-soft)");
      document.documentElement.style.setProperty("--studio-border", "var(--studio-accent)");
    });
    const panel = page.getByRole("tabpanel", { name: "Variables", exact: true });
    const add = panel.getByRole("button", { name: "Add variable", exact: true });
    const bounds = (await add.boundingBox())!;
    expect(bounds.height).toBeGreaterThanOrEqual(30);
    expect(bounds.width).toBeGreaterThanOrEqual(95);
    const normal = await add.evaluate(element => ({ color: getComputedStyle(element).color, background: getComputedStyle(element).backgroundColor }));
    await add.hover();
    expect(await add.evaluate(element => getComputedStyle(element).color)).not.toBe(normal.color);
    await add.focus();
    await page.keyboard.press("Tab");
    await page.keyboard.press("Shift+Tab");
    await expect(add).toBeFocused();
    expect(await add.evaluate(element => getComputedStyle(element).outlineStyle)).toBe("solid");
    expect(await add.evaluate(element => parseFloat(getComputedStyle(element).outlineWidth))).toBeGreaterThanOrEqual(2);
    await page.screenshot({ path: `/tmp/argument-button-${theme}-${width}.png` });
    await add.click();
    await expect(panel.getByRole("textbox", { name: "Variable name", exact: true })).toHaveCount(1);
    await expect(page.locator(".ad-save-chip")).toHaveText("Saved");
    // The shared editor also runs inside disabled fieldsets in read-only views.
    await panel.locator("fieldset.wf-container-variables").evaluate(element => { (element as HTMLFieldSetElement).disabled = true; });
    await expect(add).toBeDisabled();
    await expect.poll(() => add.evaluate(element => parseFloat(getComputedStyle(element).opacity))).toBeLessThan(1);
    expect(await add.evaluate(element => getComputedStyle(element).cursor)).toBe("not-allowed");
    expect(await page.evaluate(() => document.documentElement.scrollWidth - innerWidth)).toBeLessThanOrEqual(0);
  });
}

for (const theme of ["light", "dark"]) {
  test(`workflow Variables tab uses the same add-variable button (${theme})`, async ({ page }) => {
    await page.goto(`/?mode=activity-duplicate-editor&theme=${theme}`);
    await page.getByRole("tablist", { name: "Editor view tabs" }).getByRole("tab", { name: "Variables", exact: true }).click();
    const variables = page.locator(".wf-properties-section").filter({ has: page.getByRole("heading", { name: "Variables", exact: true }) });
    const add = variables.getByRole("button", { name: "Add variable", exact: true });
    await expect(add).toBeVisible();
    expect((await add.boundingBox())!.height).toBeGreaterThanOrEqual(30);
    await add.focus();
    await page.keyboard.press("Tab");
    await page.keyboard.press("Shift+Tab");
    await expect(add).toBeFocused();
    expect(await add.evaluate(element => getComputedStyle(element).outlineStyle)).toBe("solid");
    await add.click();
    await expect(variables.getByRole("textbox", { name: "Variable name", exact: true })).toHaveCount(1);
    await page.screenshot({ path: `/tmp/workflow-argument-button-${theme}.png` });
  });
}


for (const theme of ["light", "dark"]) for (const width of [1440, 360]) {
  test(`activity tabs keep their spacing (${theme}, ${width})`, async ({ page }, info) => {
    await page.setViewportSize({ width, height: 1000 });
    await page.goto(`/?mode=activity-authoring&theme=${theme}`);
    const tabs = page.getByRole("tablist", { name: "Activity Definition authoring views" });
    await expect(tabs.getByRole("tab", { name: "Designer", exact: true })).toBeVisible();
    const geometry = () => tabs.evaluate(element => {
      const style = getComputedStyle(element);
      return {
        gap: style.gap,
        tabs: Array.from(element.querySelectorAll<HTMLElement>("[role='tab']"), tab => {
          const tabBox = tab.getBoundingClientRect();
          const tabStyle = getComputedStyle(tab);
          return {
            width: Math.round(tabBox.width),
            height: Math.round(tabBox.height),
            padding: tabStyle.padding,
            borderBottomWidth: tabStyle.borderBottomWidth
          };
        })
      };
    });
    const original = await geometry();
    for (const id of ["inputs", "outputs", "outcomes", "variables", "designer"]) {
      await tabs.locator(`[data-tab-id="${id}"]`).click();
      expect(await geometry()).toEqual(original);
    }
    await page.screenshot({ path: info.outputPath("consistent-tabs.png") });
  });
}

for (const theme of ["light", "dark"]) for (const width of [1440, 360]) {
  test(`Activity Definition and workflow views share compact tabs (${theme}, ${width})`, async ({ page }) => {
    await page.setViewportSize({ width, height: 900 });
    await page.goto(`/?mode=activity-authoring&theme=${theme}`);
    const activityTabs = page.getByRole("tablist", { name: "Activity Definition authoring views" });
    const activityGeometry = await activityTabs.evaluate(element => {
      const style = getComputedStyle(element);
      const selected = element.querySelector<HTMLElement>('[aria-selected="true"]')!;
      const selectedStyle = getComputedStyle(selected);
      return {
        list: { gap: style.gap, flexWrap: style.flexWrap, overflowX: style.overflowX },
        tab: { height: selected.getBoundingClientRect().height, gap: selectedStyle.gap, padding: selectedStyle.padding, whiteSpace: selectedStyle.whiteSpace, borderBottomWidth: selectedStyle.borderBottomWidth }
      };
    });
    await expect(activityTabs.locator(".studio-tab-icon[aria-hidden='true']")).toHaveCount(5);
    await expect(activityTabs).toHaveClass(/studio-tabs--compact/);
    if (width < 600) {
      expect(await activityTabs.evaluate(element => element.scrollWidth > element.clientWidth)).toBe(true);
      await activityTabs.getByRole("tab", { name: "Variables", exact: true }).click();
      await expect(activityTabs.getByRole("tab", { name: "Variables", exact: true })).toBeFocused();
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
    }

    await page.goto(`/?mode=activity-duplicate-editor&theme=${theme}`);
    const workflowTabs = page.getByRole("tablist", { name: "Editor view tabs" });
    const workflowGeometry = await workflowTabs.evaluate(element => {
      const style = getComputedStyle(element);
      const selected = element.querySelector<HTMLElement>('[aria-selected="true"]')!;
      const selectedStyle = getComputedStyle(selected);
      return {
        list: { gap: style.gap, flexWrap: style.flexWrap, overflowX: style.overflowX },
        tab: { height: selected.getBoundingClientRect().height, gap: selectedStyle.gap, padding: selectedStyle.padding, whiteSpace: selectedStyle.whiteSpace, borderBottomWidth: selectedStyle.borderBottomWidth }
      };
    });
    expect(workflowGeometry).toEqual(activityGeometry);
    await expect(workflowTabs.locator(".studio-tab-icon[aria-hidden='true']")).toHaveCount(7);
    await expect(workflowTabs).toHaveClass(/studio-tabs--compact/);
    expect(await workflowTabs.locator("[role='tab'][aria-controls]").count()).toBe(0);

    const designer = workflowTabs.getByRole("tab", { name: "Designer", exact: true });
    await designer.focus();
    await designer.press("ArrowRight");
    const code = workflowTabs.getByRole("tab", { name: "Code", exact: true });
    await expect(code).toBeFocused();
    await expect(code).toHaveAttribute("aria-selected", "true");

    if (width < 600) {
      expect(await workflowTabs.evaluate(element => element.scrollWidth > element.clientWidth)).toBe(true);
      const versions = workflowTabs.getByRole("tab", { name: "Versions", exact: true });
      await versions.click();
      await expect(versions).toBeInViewport();
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
    }
  });
}
