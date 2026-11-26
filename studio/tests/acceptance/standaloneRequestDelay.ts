import { expect, type Page, type Request } from "@playwright/test";

/** A controlled transport delay, matching the existing Activity Data chooser proof: no server result is replaced. */
export async function delayNextTriggerRead(page: Page, backend: string) {
  const address = new URL(`${backend}/runtime/standalone-triggers`);
  const pattern = (url: URL) => url.origin === address.origin && url.pathname === address.pathname;
  let held: Request | undefined, release: (() => void) | undefined;
  await page.route(pattern, async route => {
    if (!held && route.request().method() === "GET") {
      held = route.request();
      await new Promise<void>(resolve => { release = resolve; });
    }
    await route.continue();
  });
  return {
    async wait() {
      await expect.poll(() => !!release, { timeout: 45_000, message: "The actual authenticated collection request reached the controlled transport boundary" }).toBe(true);
      expect((await held!.allHeaders()).authorization?.startsWith("Bearer ")).toBe(true);
      return held!;
    },
    async release() { release?.(); await page.unrouteAll({ behavior: "wait" }); }
  };
}
