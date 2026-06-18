import type { AuthProviderManager } from "../types";

export interface SignalRAccessTokenOptions {
  anonymousToken?: string;
}

export function createSignalRAccessTokenFactory(
  auth: Pick<AuthProviderManager, "getAccessToken">,
  options: SignalRAccessTokenOptions = {}
) {
  return async () => await auth.getAccessToken() ?? options.anonymousToken ?? "";
}

export function withAuthenticatedSignalROptions<TOptions extends Record<string, unknown>>(
  options: TOptions,
  auth: Pick<AuthProviderManager, "getAccessToken">
): TOptions & { accessTokenFactory: () => Promise<string> } {
  return {
    ...options,
    accessTokenFactory: createSignalRAccessTokenFactory(auth)
  };
}
