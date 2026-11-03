import axe from "axe-core";
import { expect, test } from "@playwright/test";

const themes = ["classic", "meridian", "drift", "schematic", "atelier", "elsa-cloud", "signal", "dusk"];
const statuses = ["Pending", "Running", "Suspended", "Completed", "Faulted", "Cancelled", "Unknown"];
const modes = ["light", "dark"] as const;

type BadgeMeasurement = {
  label: string;
  status: string;
  foreground: string;
  background: string;
  contrast: number;
};

type AxeViolation = {
  id: string;
  impact: string | null;
  help: string;
  targets: unknown[];
};

async function measureRenderedBadgeContrast(page: import("@playwright/test").Page): Promise<BadgeMeasurement[]> {
  const screenshot = await page.screenshot({ fullPage: true, scale: "css" });
  return page.evaluate(async screenshotBase64 => {
    const image = new Image();
    image.src = `data:image/png;base64,${screenshotBase64}`;
    await image.decode();

    const backgroundCanvas = document.createElement("canvas");
    backgroundCanvas.width = image.width;
    backgroundCanvas.height = image.height;
    const backgroundContext = backgroundCanvas.getContext("2d");
    if (!backgroundContext) throw new Error("Could not sample the rendered page screenshot");
    backgroundContext.drawImage(image, 0, 0);

    const foregroundCanvas = document.createElement("canvas");
    foregroundCanvas.width = 1;
    foregroundCanvas.height = 1;
    const foregroundContext = foregroundCanvas.getContext("2d");
    if (!foregroundContext) throw new Error("Could not resolve the rendered badge text color");

    const luminance = (rgb: number[]) => {
      const channels = rgb.slice(0, 3).map(value => value / 255).map(value =>
        value <= 0.04045 ? value / 12.92 : ((value + 0.055) / 1.055) ** 2.4
      );
      return channels[0] * 0.2126 + channels[1] * 0.7152 + channels[2] * 0.0722;
    };

    return Array.from(document.querySelectorAll<HTMLElement>(".wf-status-badge")).map(badge => {
      const bounds = badge.getBoundingClientRect();
      const x = Math.min(Math.round(bounds.left + window.scrollX + 3), image.width - 1);
      const y = Math.min(Math.round(bounds.top + window.scrollY + bounds.height / 2), image.height - 1);
      const background = Array.from(backgroundContext.getImageData(x, y, 1, 1).data).slice(0, 3);

      foregroundContext.clearRect(0, 0, 1, 1);
      foregroundContext.fillStyle = `rgb(${background.join(",")})`;
      foregroundContext.fillRect(0, 0, 1, 1);
      const foreground = getComputedStyle(badge).color;
      foregroundContext.fillStyle = foreground;
      foregroundContext.fillRect(0, 0, 1, 1);
      const foregroundRgb = Array.from(foregroundContext.getImageData(0, 0, 1, 1).data).slice(0, 3);

      const foregroundLuminance = luminance(foregroundRgb);
      const backgroundLuminance = luminance(background);
      const contrast = (Math.max(foregroundLuminance, backgroundLuminance) + 0.05) /
        (Math.min(foregroundLuminance, backgroundLuminance) + 0.05);

      return {
        label: badge.textContent?.trim() ?? "",
        status: badge.dataset.status ?? "",
        foreground,
        background: `rgb(${background.join(", ")})`,
        contrast
      };
    });
  }, screenshot.toString("base64"));
}

async function inspectFullPageAxe(page: import("@playwright/test").Page): Promise<AxeViolation[]> {
  await page.addScriptTag({ content: axe.source });
  return page.evaluate(async () => {
    const result = await window.axe.run(document, { resultTypes: ["violations"] });
    return result.violations.map(violation => ({
      id: violation.id,
      impact: violation.impact ?? null,
      help: violation.help,
      targets: violation.nodes.map(node => node.target)
    }));
  });
}

for (const mode of modes) {
  test(`Runs lifecycle status labels meet WCAG AA in ${mode} mode across built-in themes`, async ({ page }) => {
    await page.addInitScript(() => {
      const params = new URLSearchParams(window.location.search);
      localStorage.setItem("elsa-studio-theme", params.get("theme") ?? "classic");
      localStorage.setItem("elsa-studio-theme-mode", params.get("mode") ?? "light");
    });

    const report: Array<{ theme: string; badges: BadgeMeasurement[]; axe: AxeViolation[] }> = [];
    for (const theme of themes) {
      await page.goto(`/workflow-run-deletion.html?statuses=all&theme=${theme}&mode=${mode}`);
      await expect(page.locator("html")).toHaveAttribute("data-theme", theme);
      await expect(page.locator("html")).toHaveAttribute("data-theme-mode", mode);

      for (const status of statuses) {
        await expect(page.locator(`.wf-status-badge[data-status="${status.toLowerCase()}"]`)).toHaveText(status);
      }

      const badges = await measureRenderedBadgeContrast(page);
      const axeViolations = await inspectFullPageAxe(page);
      report.push({ theme, badges, axe: axeViolations });
      console.log(JSON.stringify({
        theme,
        mode,
        badgeContrast: badges.map(({ status, contrast }) => ({ status, contrast: Number(contrast.toFixed(3)) })),
        axeViolations
      }));

      if (theme === "classic") {
        const screenshotPath = test.info().outputPath(`runs-status-${mode}.png`);
        await page.screenshot({ path: screenshotPath, fullPage: true });
        await test.info().attach(`runs-status-${mode}.png`, {
          path: screenshotPath,
          contentType: "image/png"
        });
      }
    }

    await test.info().attach(`runs-status-contrast-${mode}.json`, {
      body: JSON.stringify(report, null, 2),
      contentType: "application/json"
    });

    const lowContrastBadges = report.flatMap(({ theme, badges }) =>
      badges.filter(badge => badge.contrast < 4.5).map(badge => ({ theme, ...badge }))
    );
    const fullPageAxeViolations = report.flatMap(({ theme, axe }) => axe.map(violation => ({ theme, ...violation })));

    expect(report.every(({ badges }) => badges.map(badge => badge.label).sort().join(",") === [...statuses].sort().join(",")),
      "The opt-in Runs fixture must keep every lifecycle status visible in every theme.").toBe(true);
    expect(lowContrastBadges, "Every rendered status badge must meet the 4.5:1 normal-text threshold.").toEqual([]);
    expect(fullPageAxeViolations, "Full-page Runs axe must report no violations; status badges remain in scope.").toEqual([]);
  });
}
