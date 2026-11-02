import { qualifyStudioBackendPath } from "@elsa-workflows/studio-sdk";
import { type AnchorHTMLAttributes, type MouseEvent } from "react";
import { requestStudioNavigation } from "@elsa-workflows/studio-sdk";
import { pathBeforeStudioSegment } from "./studioHostPath";

export function defaultStudioPathNavigation(path: string) {
  window.history.pushState({}, "", qualifyStudioBackendPath(path));
  window.dispatchEvent(new PopStateEvent("popstate"));
}

/** Root-relative in-app Studio path, e.g. `/workflows/activity-definitions`. */
export type StudioInAppHref = `/${string}`;

function isUnmodifiedPrimaryClick(event: MouseEvent) {
  return event.button === 0 && !event.metaKey && !event.ctrlKey && !event.shiftKey && !event.altKey;
}

function resolveHref(href: string, locationHref: string): URL | null {
  try {
    return new URL(href, locationHref);
  } catch {
    return null;
  }
}

/** Host PathBase when location includes a `/studio` segment; otherwise the origin root. */
export function studioBasePath(locationHref = window.location.href): string {
  try {
    const prefix = pathBeforeStudioSegment(new URL(locationHref).pathname);
    return prefix ? prefix.replace(/\/+$/, "") || "/" : "/";
  } catch {
    return "/";
  }
}

function isInsideStudioBasePath(pathname: string, basePath: string) {
  if (basePath === "/") return pathname.startsWith("/");
  return pathname === basePath || pathname.startsWith(`${basePath}/`);
}

function isRootRelativeHref(href: string) {
  return href.startsWith("/") && !href.startsWith("//");
}

/**
 * True when `href` is an in-app Studio navigation.
 * Callers pass root-relative paths (`/workflows/...`) without a PathBase prefix; those stay
 * in-app. Hash-only, query-only, protocol-relative, and other relative hrefs fall through.
 * Absolute same-origin URLs are in-app only under a non-root PathBase, where `/admin` can be
 * distinguished from `/tenant/workflows`.
 */
export function isStudioInAppHref(href: string, locationHref = window.location.href): boolean {
  const resolved = resolveHref(href, locationHref);
  if (!resolved) return false;
  let location: URL;
  try {
    location = new URL(locationHref);
  } catch {
    return false;
  }
  if (resolved.origin !== location.origin) return false;
  if (isRootRelativeHref(href)) return true;
  if (!href.includes("://")) return false;
  const basePath = studioBasePath(locationHref);
  if (!isInsideStudioBasePath(resolved.pathname, basePath)) return false;
  return basePath !== "/";
}

function shouldInterceptStudioLinkClick(event: MouseEvent<HTMLAnchorElement>, href: string) {
  if (!isUnmodifiedPrimaryClick(event)) return false;
  const target = event.currentTarget.getAttribute("target");
  if (target && target !== "_self") return false;
  if (event.currentTarget.hasAttribute("download")) return false;
  return isStudioInAppHref(href);
}

/**
 * In-app Studio link. Pass a root-relative Studio path such as
 * `/workflows/activity-definitions`. A plain primary click stays in-app through
 * `navigateToStudioPath`. `target`, `download`, mailto, external, and
 * same-origin URLs outside the Studio base path are left to the browser.
 */
export function StudioLink({
  href,
  navigateToStudioPath = defaultStudioPathNavigation,
  onClick,
  children,
  ...rest
}: {
  href: string;
  navigateToStudioPath?(path: string): void;
} & Omit<AnchorHTMLAttributes<HTMLAnchorElement>, "href">) {
  const targetHref = isStudioInAppHref(href) ? qualifyStudioBackendPath(href) : href;
  const handleClick = (event: MouseEvent<HTMLAnchorElement>) => {
    onClick?.(event);
    if (event.defaultPrevented || !shouldInterceptStudioLinkClick(event, targetHref)) return;
    event.preventDefault();
    if (!requestStudioNavigation(window.location.pathname, targetHref)) return;
    navigateToStudioPath(targetHref);
  };
  return <a {...rest} href={targetHref} onClick={handleClick}>{children}</a>;
}
