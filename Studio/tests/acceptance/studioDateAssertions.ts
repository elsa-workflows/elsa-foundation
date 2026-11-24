import { expect, type Page } from "@playwright/test";

/** An independent browser-locale oracle; does not call the Studio formatter. */
export async function expectedStudioDate(page: Page, instant: string, zone: string) {
  return page.evaluate(({ instant, zone }) => {
    const date = new Date(instant);
    const format = (options: Intl.DateTimeFormatOptions) => new Intl.DateTimeFormat(undefined, { timeZone: zone, ...options }).format(date);
    const name = new Intl.DateTimeFormat("en-US", { timeZone: zone, timeZoneName: "longOffset" }).formatToParts(date).find(part => part.type === "timeZoneName")!.value;
    const offset = `UTC${name === "GMT" ? "+00:00" : name.replace(/^GMT/, "")}`;
    return { full: `${format({ dateStyle: "medium", timeStyle: "medium" })} ${offset}`,
      date: format({ dateStyle: "medium" }), time: `${format({ timeStyle: "medium" })} ${offset}` };
  }, { instant, zone });
}

/** Changes the actual preference through Settings, preserving the inspected page and its mounted components. */
export async function selectStudioTimeZone(page: Page, zone: string) {
  const settings = await page.context().newPage();
  try {
    await settings.goto(new URL("/settings/appearance", page.url()).toString());
    const picker = settings.getByLabel("Time zone", { exact: true });
    await picker.selectOption(zone);
    await expect(picker).toHaveValue(zone);
  } finally { await settings.close(); }
}
