import React from "react";
import { flushSync } from "react-dom";
import { createRoot } from "react-dom/client";
import { afterEach, describe, expect, it, vi } from "vitest";
import { StudioBreadcrumbs } from "../layout/Breadcrumbs";
import { StudioPageHeader } from "../layout/Page";

const mounted: { root: ReturnType<typeof createRoot>; host: HTMLDivElement }[] = [];

afterEach(() => {
  mounted.splice(0).forEach(({ root, host }) => {
    flushSync(() => root.unmount());
    host.remove();
  });
});

function render(node: React.ReactElement) {
  const host = document.createElement("div");
  document.body.append(host);
  const root = createRoot(host);
  mounted.push({ root, host });
  flushSync(() => root.render(node));
  return host;
}

describe("StudioBreadcrumbs", () => {
  it("renders ancestor links, decorative separators, and one explicit current location", () => {
    const host = render(<StudioBreadcrumbs items={[
      { href: "/workflows/definitions", label: "Definitions" },
      { href: "/workflows/tags", label: "Workflow tags" }
    ]} current="Customer-facing" />);
    const navigation = host.querySelector<HTMLElement>('nav[aria-label="Breadcrumb"]')!;

    expect(navigation.querySelectorAll("ol > li")).toHaveLength(3);
    expect(Array.from(navigation.querySelectorAll("li > a")).map(link => link.textContent)).toEqual(["Definitions", "Workflow tags"]);
    expect(navigation.querySelectorAll('[aria-hidden="true"]')).toHaveLength(2);
    expect(navigation.querySelectorAll('[aria-current="page"]')).toHaveLength(1);
    expect(navigation.querySelector('[aria-current="page"]')?.textContent).toBe("Customer-facing");
  });

  it("delegates ancestor rendering when a host supplies its router link", () => {
    const renderLink = vi.fn(({ href, label }) => <a href={href} data-custom-link="">{label}</a>);
    const host = render(<StudioBreadcrumbs items={[{ href: "/workflows/definitions", label: "Definitions" }]} current="Workflow tags" renderLink={renderLink} />);

    expect(renderLink).toHaveBeenCalledOnce();
    expect(host.querySelector('[data-custom-link][href="/workflows/definitions"]')?.textContent).toBe("Definitions");
  });

  it("places an opt-in trail above the page title and apart from header actions", () => {
    const host = render(<StudioPageHeader
      breadcrumbs={<StudioBreadcrumbs items={[{ href: "/workflows/definitions", label: "Definitions" }]} current="Workflow tags" />}
      title="Workflow tags"
      actions={<button type="button">Create tag</button>}
    />);
    const header = host.querySelector("header")!;
    const copy = header.querySelector<HTMLElement>(".studio-page-header-copy")!;

    expect(copy.firstElementChild?.matches('nav[aria-label="Breadcrumb"]')).toBe(true);
    expect(copy.children[1]?.tagName).toBe("H1");
    expect(header.querySelector(".studio-page-actions")?.parentElement).toBe(header);
  });
});