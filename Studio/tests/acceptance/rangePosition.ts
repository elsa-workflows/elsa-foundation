import type { Locator } from "@playwright/test";

/** Chromium's native range thumb travels inside the input's padded content box. */
export async function rangePositionX(range: Locator, index: number, total: number): Promise<number> {
  return range.evaluate((element, { index, total }) => {
    const style = getComputedStyle(element), bounds = element.getBoundingClientRect();
    const left = parseFloat(style.borderLeftWidth) + parseFloat(style.paddingLeft);
    const right = parseFloat(style.borderRightWidth) + parseFloat(style.paddingRight);
    const thumb = 16;
    return left + thumb / 2 + (bounds.width - left - right - thumb) * index / (total - 1);
  }, { index, total });
}
