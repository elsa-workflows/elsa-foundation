export type BrowserNavigationWindow = Pick<
  Window,
  "addEventListener" | "dispatchEvent" | "history" | "location" | "removeEventListener"
> & {
  PopStateEvent: typeof PopStateEvent;
};

export function createBrowserNavigationAdapter(browser: BrowserNavigationWindow = window) {
  return (nextPath: string) => {
    const currentUrl = routeUrl(browser.location);
    const target = new URL(nextPath, browser.location.href);
    const targetUrl = routeUrl(target);
    if (targetUrl === currentUrl) return;

    browser.history.pushState({}, "", targetUrl);
    browser.dispatchEvent(new browser.PopStateEvent("popstate"));
  };
}

export function subscribeToBrowserNavigation(
  onRoute: (path: string) => void,
  browser: BrowserNavigationWindow = window
) {
  const syncFromLocation = () => onRoute(browser.location.pathname);
  browser.addEventListener("popstate", syncFromLocation);
  return () => browser.removeEventListener("popstate", syncFromLocation);
}

export function routeMatchesPath(routePath: string, path: string) {
  const routeSegments = routePath.split("/").filter(Boolean);
  const pathSegments = path.split("/").filter(Boolean);
  return routeSegments.length === pathSegments.length &&
    routeSegments.every((segment, index) => segment.startsWith(":") || segment === pathSegments[index]);
}

function routeUrl(location: Pick<Location | URL, "hash" | "pathname" | "search">) {
  return `${location.pathname}${location.search}${location.hash}`;
}
