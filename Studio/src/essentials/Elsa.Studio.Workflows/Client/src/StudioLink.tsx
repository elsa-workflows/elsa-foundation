import { type AnchorHTMLAttributes, type MouseEvent } from "react";
import { requestStudioNavigation } from "@elsa-workflows/studio-sdk";

export function defaultStudioPathNavigation(path: string) {
  window.history.pushState({}, "", path);
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

/** Host PathBase when location includes `/studio`; otherwise the origin root. */
export function studioBasePath(locationHref = window.location.href): string {
  try {
    const path = new URL(locationHref).pathname;
    const studio = path.indexOf("/studio");
    if (studio > 0) return path.slice(0, studio).replace(/\/+$/, "") || "/";
    return "/";
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

/** True when `href` is same-origin and inside the Studio base path. */
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
  const basePath = studioBasePath(locationHref);
  if (!isInsideStudioBasePath(resolved.pathname, basePath)) return false;
  if (isRootRelativeHref(href)) return true;
  // Absolute same-origin URLs are in-app only under a non-root PathBase, where
  // `/admin` can be distinguished from `/tenant/workflows`.
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
  const handleClick = (event: MouseEvent<HTMLAnchorElement>) => {
    onClick?.(event);
    if (event.defaultPrevented || !shouldInterceptStudioLinkClick(event, href)) return;
    event.preventDefault();
    if (!requestStudioNavigation(window.location.pathname, href)) return;
    navigateToStudioPath(href);
  };
  return <a {...rest} href={href} onClick={handleClick}>{children}</a>;
}
