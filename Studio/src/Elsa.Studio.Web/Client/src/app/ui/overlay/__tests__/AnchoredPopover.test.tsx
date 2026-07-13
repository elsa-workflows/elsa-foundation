import React, { createRef } from "react";
import { flushSync } from "react-dom";
import { createRoot } from "react-dom/client";
import { afterEach, describe, expect, it, vi } from "vitest";
import {
  AnchoredPopover,
  calculateAnchoredPopoverPosition
} from "../AnchoredPopover";

let cleanup: (() => void) | null = null;

afterEach(() => {
  cleanup?.();
  cleanup = null;
  vi.unstubAllGlobals();
});

describe("calculateAnchoredPopoverPosition", () => {
  it("flips above and shifts inside the viewport when space is constrained", () => {
    expect(calculateAnchoredPopoverPosition(
      rect({ top: 260, right: 310, bottom: 292, left: 250, width: 60, height: 32 }),
      { width: 180, height: 160 },
      { width: 320, height: 300 },
      { gap: 6, viewportPadding: 8, maxHeight: 210, minWidth: 176 }
    )).toEqual({
      placement: "top",
      top: 94,
      left: 132,
      width: 180,
      maxHeight: 210
    });
  });

  it("stays below and bounds its height to the available viewport", () => {
    expect(calculateAnchoredPopoverPosition(
      rect({ top: 12, right: 132, bottom: 44, left: 12, width: 120, height: 32 }),
      { width: 120, height: 400 },
      { width: 320, height: 240 },
      { gap: 6, viewportPadding: 8, maxHeight: 210, minWidth: 176 }
    )).toEqual({
      placement: "bottom",
      top: 50,
      left: 12,
      width: 176,
      maxHeight: 182
    });
  });
});

describe("AnchoredPopover", () => {
  it("portals, coalesces viewport events, and reports anchor-hidden dismissal", () => {
    vi.stubGlobal("ResizeObserver", class {
      observe() {}
      disconnect() {}
    });
    const frames: FrameRequestCallback[] = [];
    vi.stubGlobal("requestAnimationFrame", vi.fn(callback => {
      frames.push(callback);
      return frames.length;
    }));
    vi.stubGlobal("cancelAnimationFrame", vi.fn());
    const anchorRef = createRef<HTMLButtonElement>();
    const host = document.createElement("div");
    document.body.appendChild(host);
    const root = createRoot(host);
    let anchorTop = 40;
    let rectReads = 0;
    const dismissals: string[] = [];

    flushSync(() => root.render(
      <>
        <button ref={anchorRef}>Anchor</button>
        <AnchoredPopover anchorRef={anchorRef} open onDismiss={reason => dismissals.push(reason)}>
          <div>Portal content</div>
        </AnchoredPopover>
      </>
    ));

    Object.defineProperty(anchorRef.current!, "getBoundingClientRect", {
      configurable: true,
      value: () => {
        rectReads++;
        return rect({
          top: anchorTop,
          right: 140,
          bottom: anchorTop + 32,
          left: 40,
          width: 100,
          height: 32
        });
      }
    });
    flushSync(() => {
      window.dispatchEvent(new Event("resize"));
      window.dispatchEvent(new Event("resize"));
      window.dispatchEvent(new Event("scroll"));
    });
    expect(rectReads).toBe(0);
    expect(frames).toHaveLength(1);
    flushSync(() => frames.shift()?.(0));
    expect(rectReads).toBe(1);

    const popover = document.body.querySelector<HTMLElement>(".studio-anchored-popover");
    expect(popover).not.toBeNull();
    expect(host.contains(popover)).toBe(false);
    expect(popover?.style.top).toBe("78px");

    anchorTop = 80;
    flushSync(() => {
      window.dispatchEvent(new Event("scroll"));
      window.dispatchEvent(new Event("scroll"));
    });
    expect(frames).toHaveLength(1);
    flushSync(() => frames.shift()?.(1));
    expect(rectReads).toBe(2);
    expect(popover?.style.top).toBe("118px");

    anchorTop = -100;
    flushSync(() => window.dispatchEvent(new Event("scroll")));
    flushSync(() => frames.shift()?.(2));
    expect(dismissals).toEqual(["anchor-hidden"]);

    cleanup = () => {
      flushSync(() => root.unmount());
      host.remove();
    };
  });
});

function rect(value: Partial<DOMRect>): DOMRect {
  return {
    x: value.left ?? 0,
    y: value.top ?? 0,
    top: 0,
    right: 0,
    bottom: 0,
    left: 0,
    width: 0,
    height: 0,
    toJSON: () => ({}),
    ...value
  } as DOMRect;
}
