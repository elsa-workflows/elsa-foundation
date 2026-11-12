import { createRedirectAuthAdapter, type RedirectAuthAdapterOptions } from "./redirect";

export function createOidcAuthAdapter(options: Omit<RedirectAuthAdapterOptions, "kind">) {
  return createRedirectAuthAdapter({
    ...options,
    kind: "external-oidc"
  });
}
