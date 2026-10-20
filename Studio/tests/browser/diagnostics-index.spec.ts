import { expect, test } from "@playwright/test";

const destinations = [
  ["Console", "/diagnostics/console"],
  ["Structured logs", "/diagnostics/structured-logs"],
  ["OpenTelemetry", "/diagnostics/open-telemetry"],
  ["Database audit", "/diagnostics/audit"],
  ["Runtime evidence settings", "/workflows/runtime-diagnostics"]
] as const;

for (const theme of ["light", "dark"] as const) {
  for (const width of [320, 768, 1024, 1440]) {
    test(`Diagnostics index is keyboard usable without horizontal overflow at ${width}px in ${theme} mode`, async ({ page }, testInfo) => {
      await page.setViewportSize({ width, height: 900 });
      await page.emulateMedia({ colorScheme: theme });
      await page.goto(`/diagnostics-index.html?theme=${theme}`);

      const diagnostics = page.locator(".diagnostics-view");
      await expect(diagnostics).toBeVisible();
      await expect(page.locator("html")).toHaveAttribute("data-theme-appearance", theme);
      await expect(page.getByText("Host diagnostics widget remains visible.")).toBeVisible();

      const links = destinations.map(([label]) => diagnostics.getByRole("link", { name: label }));
      for (const [index, [label, path]] of destinations.entries()) {
        const link = links[index];
        await expect(link).toBeVisible();
        await expect(link).toHaveAttribute("href", path);
      }

      await page.screenshot({ path: testInfo.outputPath(`diagnostics-index-${theme}-${width}.png`), fullPage: true });

      let reachedFirstDestination = false;
      for (let attempt = 0; attempt < 80; attempt++) {
        await page.keyboard.press("Tab");
        if (await links[0].evaluate(element => element === document.activeElement)) {
          reachedFirstDestination = true;
          break;
        }
      }
      expect(reachedFirstDestination, "Tab traversal reaches the first Diagnostics destination").toBe(true);

      for (const [index, [, path]] of destinations.entries()) {
        const link = links[index];
        if (index > 0) await page.keyboard.press("Tab");
        await expect(link).toBeFocused();
        await page.keyboard.press("Enter");
        await expect(page.getByTestId("diagnostics-route")).toHaveText(path);
      }

      const contentOverflow = await diagnostics.evaluate(section => {
        const issues: string[] = [];
        const content = Array.from(section.querySelectorAll<HTMLElement>("a[href], p, [data-diagnostics-description]"));
        for (const element of content) {
          let ancestor: HTMLElement | null = element;
          while (ancestor && ancestor !== section) {
            if (ancestor.clientWidth > 0 && ancestor.scrollWidth - ancestor.clientWidth > 1) {
              issues.push(`${element.textContent?.trim() || element.tagName} overflows ${ancestor.tagName.toLowerCase()}`);
            }
            if (ancestor.clientHeight > 0 && ancestor.scrollHeight - ancestor.clientHeight > 1) {
              issues.push(`${element.textContent?.trim() || element.tagName} is clipped by ${ancestor.tagName.toLowerCase()}`);
            }
            ancestor = ancestor.parentElement;
          }
        }
        return issues;
      });
      expect(contentOverflow).toEqual([]);
      expect(await page.locator("html").evaluate(element => element.scrollWidth - window.innerWidth)).toBeLessThanOrEqual(1);
      expect(await page.locator("body").evaluate(element => element.scrollWidth - window.innerWidth)).toBeLessThanOrEqual(1);
    });
  }
}
