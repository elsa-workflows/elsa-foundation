import type { AuthProviderManager } from "../types";

export interface SignalRAccessTokenOptions {
  anonymousToken?: string;
  fallbackAccessTokenFactory?: () => string | Promise<string>;
}

export function createSignalRAccessTokenFactory(
  auth: Pick<AuthProviderManager, "getAccessToken">,
  options: SignalRAccessTokenOptions = {}
) {
  return async () => await auth.getAccessToken()
    ?? await options.fallbackAccessTokenFactory?.()
    ?? options.anonymousToken
    ?? "";
}

export function withAuthenticatedSignalROptions<TOptions extends Record<string, unknown>>(
  options: TOptions,
  auth: Pick<AuthProviderManager, "getAccessToken">
): TOptions & { accessTokenFactory: () => Promise<string> } {
  const fallbackAccessTokenFactory = isAccessTokenFactory(options.accessTokenFactory)
    ? options.accessTokenFactory.bind(options)
    : undefined;

  return {
    ...options,
    accessTokenFactory: createSignalRAccessTokenFactory(auth, { fallbackAccessTokenFactory })
  };
}

function isAccessTokenFactory(value: unknown): value is () => string | Promise<string> {
  return typeof value === "function";
}
