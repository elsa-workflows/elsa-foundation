import { expect, test } from "@playwright/test";
import { capture } from "../acceptance/capture";

test("acceptance capture completes with a paused finite animation", async ({ page }, info) => {
  test.setTimeout(10_000);
  await page.setContent(`<style>
    @keyframes settle { from { opacity: 1; } to { opacity: .25; } }
    #retained-panel { animation: settle 1s both paused; }
  </style><main><h1>Capture fixture</h1><div id="retained-panel">Retained panel</div></main>`);
  await expect.poll(() => page.locator("#retained-panel").evaluate(element => element.getAnimations()[0]?.playState)).toBe("paused");

  await capture(page, info, "paused-animation");

  await expect(page.locator("#retained-panel")).toHaveCSS("opacity", "0.25");
  expect(info.attachments.map(attachment => attachment.name)).toEqual(expect.arrayContaining(["paused-animation.png", "paused-animation.aria.yml"]));
});

test("acceptance capture rejects horizontal overflow", async ({ page }, info) => {
  await page.setContent('<main style="width:200vw">Overflow must remain a failed acceptance check.</main>');

  await expect(capture(page, info, "overflow")).rejects.toThrow();

  expect(info.attachments.some(attachment => attachment.name === "overflow.png")).toBe(false);
});

test("acceptance capture rejects overflow introduced by an animation's final frame", async ({ page }, info) => {
  await page.setContent(`<style>
    @keyframes expand { from { width: 100px; } to { width: 200vw; } }
    main { animation: expand 1s both paused; }
  </style><main>Final geometry must still fit the viewport.</main>`);

  await expect(capture(page, info, "animated-overflow")).rejects.toThrow();

  expect(info.attachments.some(attachment => attachment.name === "animated-overflow.png")).toBe(false);
});

test("acceptance capture completes with a nonfinishing finite Web Animation", async ({ page }, info) => {
  test.setTimeout(15_000);
  await page.setContent('<main id="retained-panel">Retained static animation</main>');
  await page.locator("main").evaluate(element => {
    const animation = element.animate([{ opacity: 1 }, { opacity: .25 }], { duration: 1000, fill: "both" });
    animation.playbackRate = 0;
  });

  await capture(page, info, "nonfinishing-animation");

  expect(info.attachments.map(attachment => attachment.name)).toEqual(expect.arrayContaining(["nonfinishing-animation.png", "nonfinishing-animation.aria.yml"]));
});

test("acceptance capture rejects fonts that do not settle", async ({ page }, info) => {
  test.setTimeout(15_000);
  let releaseFont = () => {};
  let fontRequested = false;
  const responseReady = new Promise<void>(resolve => { releaseFont = resolve; });
  await page.route("https://capture.test/pending.woff2", async route => {
    fontRequested = true;
    await responseReady;
    await route.abort();
  });
  await page.setContent(`<style>
    @font-face { font-family: Pending; src: url(https://capture.test/pending.woff2); }
    main { font-family: Pending; }
  </style><main>Font readiness remains required.</main>`, { waitUntil: "domcontentloaded" });
  await expect.poll(() => fontRequested).toBe(true);
  await expect.poll(() => page.evaluate(() => document.fonts.status)).toBe("loading");

  try {
    await expect(capture(page, info, "pending-font")).rejects.toThrow();
    expect(info.attachments.some(attachment => attachment.name === "pending-font.png")).toBe(false);
  } finally {
    releaseFont();
    await page.unrouteAll({ behavior: "wait" });
  }
});
