import React from "react";
import { flushSync } from "react-dom";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { getRegisteredActivityIcon, registerActivityIcons } from "@elsa-workflows/studio-sdk";
import { ActivityIconGlyph } from "../activityIconGlyphs";

function SlackIcon({ size = 15 }: { size?: number }) {
  return <svg data-testid="slack-icon" width={size} height={size} viewBox="0 0 24 24"><circle cx="12" cy="12" r="8" /></svg>;
}

function MockRocket({ size = 15 }: { size?: number }) {
  mockRocketCalls.push(size);
  return <svg data-testid="rocket-icon" width={size} height={size} viewBox="0 0 24 24"><path d="M12 2L4 22h16z" /></svg>;
}

const mockRocketCalls: number[] = [];

vi.mock("lucide-react/dynamicIconImports", () => ({
  default: {
    rocket: () => Promise.resolve({ default: MockRocket })
  }
}));

let root: Root;
let container: HTMLDivElement;

beforeEach(() => {
  mockRocketCalls.length = 0;
  registerActivityIcons({ slack: null });
  container = document.createElement("div");
  document.body.appendChild(container);
  root = createRoot(container);
});

afterEach(() => {
  flushSync(() => root.unmount());
  container.remove();
  registerActivityIcons({ slack: null });
});

describe("ActivityIconGlyph extension contract", () => {
  it("renders a registered ext: icon", () => {
    registerActivityIcons({ slack: SlackIcon });
    expect(getRegisteredActivityIcon("ext:slack")).toBe(SlackIcon);

    flushSync(() => root.render(<ActivityIconGlyph icon="ext:slack" fallback="email" />));

    expect(container.querySelector("[data-testid='slack-icon']")).toBeTruthy();
  });

  it("matches ext: names case-insensitively and re-renders a late registration", () => {
    flushSync(() => root.render(<ActivityIconGlyph icon="ext:Slack" fallback="email" />));
    expect(container.querySelector("[data-testid='slack-icon']")).toBeNull();

    flushSync(() => registerActivityIcons({ Slack: SlackIcon }));

    expect(getRegisteredActivityIcon("ext:SLACK")).toBe(SlackIcon);
    expect(container.querySelector("[data-testid='slack-icon']")).toBeTruthy();
  });

  it("falls back to the category icon when ext: is unknown", () => {
    flushSync(() => root.render(<ActivityIconGlyph icon="ext:missing" fallback="email" />));

    expect(container.querySelector("[data-testid='slack-icon']")).toBeNull();
    expect(container.querySelector("svg")).toBeTruthy();
  });

  it("renders asset: values as an img", () => {
    flushSync(() => root.render(<ActivityIconGlyph icon="asset:Acme.Slack/icons/slack.svg" fallback="activity" />));

    const image = container.querySelector("img.wf-activity-icon-asset") as HTMLImageElement | null;
    expect(image?.getAttribute("src")).toBe("/_content/Acme.Slack/icons/slack.svg");
    expect(image?.getAttribute("alt")).toBe("");
  });

  it("falls back when an asset image fails to load", () => {
    flushSync(() => root.render(<ActivityIconGlyph icon="asset:Acme.Slack/icons/missing.svg" fallback="activity" />));

    const image = container.querySelector("img") as HTMLImageElement;
    flushSync(() => image.dispatchEvent(new Event("error")));

    expect(container.querySelector("img")).toBeNull();
    expect(container.querySelector("svg")).toBeTruthy();
  });

  it("stores a lazily loaded lucide component without invoking it as a setState updater", async () => {
    flushSync(() => root.render(<ActivityIconGlyph icon="Rocket" fallback="activity" />));

    await vi.waitFor(() => {
      expect(container.querySelector("[data-testid='rocket-icon']")).toBeTruthy();
    });
    expect(mockRocketCalls.length).toBeGreaterThan(0);
    expect(mockRocketCalls.every(size => size === 15)).toBe(true);
  });
});
