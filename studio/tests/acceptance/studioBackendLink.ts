import type { Page } from "@playwright/test";

export function studioBackendLink(page: Page, path: string): string {
  const current = new URL(page.url());
  const destination = new URL(path, current);
  const profile = current.searchParams.get("backend");
  if (profile !== null) destination.searchParams.set("backend", profile);
  return destination.href;
}
