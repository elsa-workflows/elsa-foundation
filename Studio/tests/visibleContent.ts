/** Browser-evaluated geometry: ARIA focus and viewport intersection do not detect overlays. */
export function isFieldUnobscured(control: Element, includeFrame: boolean | void = true): boolean {
  const document = control.ownerDocument;
  const view = document.defaultView;
  if (!view) return false;
  const frame = control.closest(".studio-field") ?? control.closest(".studio-form-control, label") ?? control;
  const label = frame.querySelector(".studio-field-label") ?? (control as HTMLInputElement).labels?.[0];
  const descriptions = includeFrame ? (control.getAttribute("aria-describedby") ?? "").split(/\s+/)
    .map(id => document.getElementById(id)).filter((element): element is HTMLElement => Boolean(element && frame.contains(element) && element.getClientRects().length)) : [];
  let top = 0, bottom = view.innerHeight, left = 0, right = view.innerWidth;
  for (let parent = frame.parentElement; parent; parent = parent.parentElement) {
    const style = view.getComputedStyle(parent), bounds = parent.getBoundingClientRect();
    if (/(auto|scroll|hidden|clip)/.test(style.overflowY) && parent.clientHeight > 0) {
      top = Math.max(top, bounds.top + parent.clientTop);
      bottom = Math.min(bottom, bounds.top + parent.clientTop + parent.clientHeight);
    }
    if (/(auto|scroll|hidden|clip)/.test(style.overflowX) && parent.clientWidth > 0) {
      left = Math.max(left, bounds.left + parent.clientLeft);
      right = Math.min(right, bounds.left + parent.clientLeft + parent.clientWidth);
    }
  }
  for (const element of new Set([...(includeFrame ? [frame] : []), control, ...(label ? [label] : []), ...descriptions])) {
    const bounds = element.getBoundingClientRect();
    if (!bounds.width || !bounds.height || bounds.top < top - 1 || bounds.bottom > bottom + 1
      || bounds.left < left - 1 || bounds.right > right + 1) return false;
    // Include supporting messages as well as the label and control; ARIA presence alone cannot detect an overlay.
    if (element === frame && element !== control && element !== label) continue;
    for (const x of [bounds.left + Math.min(2, bounds.width / 2), bounds.right - Math.min(2, bounds.width / 2)]) {
      for (const y of [bounds.top + Math.min(2, bounds.height / 2), bounds.bottom - Math.min(2, bounds.height / 2)]) {
        const hit = document.elementFromPoint(x, y);
        if (!hit || !frame.contains(hit)) return false;
      }
    }
  }
  return true;
}

/** A short selected-activity name must fit without ellipsis, clipping or one-character lines. */
export function isActivityHeadingReadable(heading: Element): boolean {
  const element = heading as HTMLElement;
  const view = element.ownerDocument.defaultView;
  if (!view) return false;
  const bounds = element.getBoundingClientRect();
  const lineHeight = parseFloat(view.getComputedStyle(element).lineHeight);
  return Boolean(element.textContent?.trim()) && bounds.width > 0 && bounds.height > 0
    && element.scrollWidth <= element.clientWidth + 1 && element.scrollHeight <= element.clientHeight + 1
    && Number.isFinite(lineHeight) && bounds.height <= lineHeight * 3 + 1;
}
