import { type AnchorHTMLAttributes, type MouseEvent } from "react";
import { requestStudioNavigation } from "@elsa-workflows/studio-sdk";

export function defaultStudioPathNavigation(path: string) {
  window.history.pushState({}, "", path);
  window.dispatchEvent(new PopStateEvent("popstate"));
}

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
    if (event.defaultPrevented || event.button !== 0 || event.metaKey || event.ctrlKey || event.shiftKey || event.altKey) return;
    event.preventDefault();
    if (!requestStudioNavigation(window.location.pathname, href)) return;
    navigateToStudioPath(href);
  };
  return <a {...rest} href={href} onClick={handleClick}>{children}</a>;
}
