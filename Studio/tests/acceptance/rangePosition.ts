import type { Locator } from "@playwright/test";

/** Map Chromium's native range travel to Locator.click's padding-box coordinates. */
export async function rangePositionX(range: Locator, index: number, total: number): Promise<number> {
  return range.evaluate(async (element, { index, total }) => {
    // Appearance changes can animate padding without moving the outer bounding box.
    // Flush style and finish finite input transitions before measuring native travel.
    element.getBoundingClientRect();
    await Promise.all(element.getAnimations().filter(animation => {
      const end = animation.effect?.getComputedTiming().endTime;
      return typeof end === "number" && Number.isFinite(end);
    }).map(animation => animation.finished.catch(() => undefined)));
    const style = getComputedStyle(element), bounds = element.getBoundingClientRect();
    const borderLeft = parseFloat(style.borderLeftWidth), borderRight = parseFloat(style.borderRightWidth);
    const left = parseFloat(style.paddingLeft), right = parseFloat(style.paddingRight);
    const thumb = 16;
    return left + thumb / 2 + (bounds.width - borderLeft - borderRight - left - right - thumb) * index / (total - 1);
  }, { index, total });
}
