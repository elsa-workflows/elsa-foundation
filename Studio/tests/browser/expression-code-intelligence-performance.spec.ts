import { expect, test, type Locator, type Page } from "@playwright/test";

const sampleCount = 30;
const warmupActivationCount = 2;
const cpuThrottleRate = 4;
const fixtureRevision = "expression-code-intelligence-50-fields-v2";

interface ExpressionToolingReadiness {
  authoringContextRequests: number;
  catalogRequests: number;
  completionRequests: number;
  validationRequests: number;
}

test.describe("expression editor reference performance benchmark", () => {
  // The 4× CPU profile is meaningful only when the activation and typing samples do not contend for
  // the same host CPU.
  test.describe.configure({ mode: "serial" });
  test.slow();

  test("meets cold and warm compact activation p95 bounds", async ({ browser, baseURL, page }) => {
    const warmSamples: number[] = [];
    await throttleCpu(page);
    await page.goto("/?mode=expression-code-intelligence&fields=50");

    // Warm measurements start only after both the lazy language module and the compact-view
    // handoff path have run, and after the authoring-context, completion, and catalog paths have
    // returned. Those costs are measured separately by the cold samples below.
    for (let index = 0; index < warmupActivationCount; index++) {
      const preview = page.getByRole("button", { name: /JavaScript expression\. Activate to edit\./ }).first();
      await expect(preview).toBeVisible();
      const before = await toolingReadiness(page);
      await activateCompactEditor(preview);
      const editor = page.locator(".studio-code-editor-rich-compact .cm-content");
      await expect(editor).toBeVisible();
      await waitForAuthoringContext(page, before.authoringContextRequests);
      if (index === warmupActivationCount - 1) {
        await editor.press("Control+Space");
        await expect(page.locator(".cm-tooltip-autocomplete")).toContainText("formatTotal");
        await waitForCatalogAndCompletion(page);
        await page.keyboard.press("Escape");
      }
    }

    for (let index = 0; index < sampleCount; index++) {
      // Switching fields is the representative warm path: it blurs the preceding compact editor
      // while keeping its language/session resources warm. Exactly one rich editor remains mounted.
      const preview = page.getByRole("button", { name: /JavaScript expression\. Activate to edit\./ }).first();
      await expect(preview).toBeVisible();
      const before = await toolingReadiness(page);
      const activationMs = await activateCompactEditor(preview);
      await expect(page.locator(".studio-code-editor-rich-compact .cm-content")).toBeVisible();
      warmSamples.push(activationMs);
      await waitForAuthoringContext(page, before.authoringContextRequests);
      await expect(page.locator(".studio-code-editor-rich-compact")).toHaveCount(1);
      await expect(page.getByRole("button", { name: /JavaScript expression\. Activate to edit\./ })).toHaveCount(9);
    }

    const coldSamples: number[] = [];
    for (let index = 0; index < sampleCount; index++) {
      const context = await browser.newContext();
      const coldPage = await context.newPage();
      await throttleCpu(coldPage);
      await coldPage.goto(`${baseURL}/?mode=expression-code-intelligence&fields=50`);
      const preview = coldPage.getByRole("button", { name: /JavaScript expression\. Activate to edit\./ }).first();
      await expect(preview).toBeVisible();
      const activationMs = await activateCompactEditor(preview);
      await expect(coldPage.locator(".studio-code-editor-rich-compact .cm-content")).toBeVisible();
      coldSamples.push(activationMs);
      await waitForAuthoringContext(coldPage, 0);
      await context.close();
    }

    const readiness = await toolingReadiness(page);
    const evidence = {
      browser: await browser.version(),
      cpuThrottleRate,
      fixture: "50 fields: 10 rich-capable, 40 ordinary",
      fixtureRevision,
      runner: {
        architecture: process.arch,
        platform: process.platform,
        environment: process.env.CI ? "CI" : "local"
      },
      readiness,
      warmupActivationCount,
      warmSamples,
      warmP95: percentile95(warmSamples),
      coldSamples,
      coldP95: percentile95(coldSamples)
    };
    await test.info().attach("activation-benchmark.json", {
      body: JSON.stringify(evidence, null, 2),
      contentType: "application/json"
    });
    console.info(`expression activation p95 (4x CPU): warm=${evidence.warmP95.toFixed(2)} ms, cold=${evidence.coldP95.toFixed(2)} ms`);
    expect(evidence.warmP95).toBeLessThan(100);
    expect(evidence.coldP95).toBeLessThan(500);
  });

  test("keeps p95 longest typing task below 50 ms across 100 representative keystrokes", async ({ page }) => {
    await throttleCpu(page);
    await page.goto("/?mode=expression-code-intelligence&fields=50");
    await page.getByRole("button", { name: /JavaScript expression\. Activate to edit\./ }).first().click();
    const editor = page.locator(".studio-code-editor-rich-compact .cm-content");
    await expect(editor).toBeVisible();
    await waitForAuthoringContext(page, 0);
    await page.evaluate(() => {
      const durations: number[] = [];
      const starts = new WeakMap<Event, number>();
      const isEditorEvent = (event: Event) =>
        event.target instanceof Element && event.target.closest(".studio-code-editor-rich-compact .cm-content") != null;
      const capture = (event: KeyboardEvent) => {
        if (isEditorEvent(event)) starts.set(event, performance.now());
      };
      const bubble = (event: KeyboardEvent) => {
        const startedAt = starts.get(event);
        if (startedAt === undefined) return;
        queueMicrotask(() => durations.push(performance.now() - startedAt));
      };
      document.addEventListener("keydown", capture, true);
      document.addEventListener("keydown", bubble);
      (window as Window & {
        expressionTypingBenchmark?: {
          durations: number[];
          stop(): void;
        };
      }).expressionTypingBenchmark = {
        durations,
        stop() {
          document.removeEventListener("keydown", capture, true);
          document.removeEventListener("keydown", bubble);
        }
      };
    });

    await editor.pressSequentially("x".repeat(100));
    const typingTaskSamples = await page.evaluate(async () => {
      await new Promise<void>(resolve => requestAnimationFrame(() => requestAnimationFrame(() => resolve())));
      const benchmark = (window as Window & {
        expressionTypingBenchmark?: {
          durations: number[];
          stop(): void;
        };
      }).expressionTypingBenchmark;
      benchmark?.stop();
      return benchmark?.durations ?? [];
    });
    const longestTypingTaskP95 = percentile95(typingTaskSamples);
    await test.info().attach("typing-task-benchmark.json", {
      body: JSON.stringify({
        browser: await page.context().browser()?.version(),
        cpuThrottleRate,
        fixtureRevision,
        keystrokes: 100,
        samples: typingTaskSamples,
        longestTypingTaskP95
      }, null, 2),
      contentType: "application/json"
    });
    console.info(`expression typing-task p95 (4x CPU, 100 keys): ${longestTypingTaskP95.toFixed(2)} ms`);
    expect(typingTaskSamples).toHaveLength(100);
    expect(longestTypingTaskP95).toBeLessThan(50);
  });
});

async function throttleCpu(page: Page) {
  const session = await page.context().newCDPSession(page);
  await session.send("Emulation.setCPUThrottlingRate", { rate: cpuThrottleRate });
}

/** Measures the browser interaction itself, excluding Playwright transport and actionability polling. */
async function activateCompactEditor(preview: Locator) {
  return preview.evaluate(button => new Promise<number>((resolve, reject) => {
    const surface = button.closest<HTMLElement>(".studio-code-editor");
    if (!surface) {
      reject(new Error("The compact preview is not inside a Studio code editor."));
      return;
    }

    const startedAt = performance.now();
    let finished = false;
    let frame = 0;
    const observer = new MutationObserver(check);
    const finish = () => {
      if (finished) return;
      finished = true;
      observer.disconnect();
      cancelAnimationFrame(frame);
      resolve(performance.now() - startedAt);
    };
    function check() {
      if (surface.querySelector(".studio-code-editor-rich-compact .cm-content")) finish();
      else if (!finished) frame = requestAnimationFrame(check);
    }

    observer.observe(surface, { childList: true, subtree: true });
    button.click();
    check();
  }));
}

async function toolingReadiness(page: Page) {
  return page.evaluate(() => {
    const readiness = (window as Window & { expressionToolingReadiness?: ExpressionToolingReadiness })
      .expressionToolingReadiness;
    if (!readiness) throw new Error("The expression tooling readiness probe is not installed.");
    return { ...readiness };
  });
}

async function waitForAuthoringContext(page: Page, previousCount: number) {
  await page.waitForFunction(
    count => {
      const readiness = (window as Window & { expressionToolingReadiness?: ExpressionToolingReadiness })
        .expressionToolingReadiness;
      return (readiness?.authoringContextRequests ?? 0) > count &&
        (readiness?.validationRequests ?? 0) > count;
    },
    previousCount
  );
}

async function waitForCatalogAndCompletion(page: Page) {
  await page.waitForFunction(() => {
    const readiness = (window as Window & { expressionToolingReadiness?: ExpressionToolingReadiness })
      .expressionToolingReadiness;
    return (readiness?.catalogRequests ?? 0) > 0 &&
      (readiness?.completionRequests ?? 0) > 0;
  });
}

function percentile95(samples: number[], emptyValue = Number.POSITIVE_INFINITY) {
  const sorted = [...samples].sort((left, right) => left - right);
  return sorted[Math.ceil(sorted.length * 0.95) - 1] ?? emptyValue;
}
