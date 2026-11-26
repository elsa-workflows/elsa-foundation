import { useQuery } from "@tanstack/react-query";
import type { StudioHttpClient, StudioModulesResponse } from "../../sdk";
import { shellKeys } from "../queryKeys";

// The shell boot manifest (`/_elsa/studio/modules`) served by the Studio host. Fetched through
// TanStack Query so the shell shares one cache/retry policy with the rest of the app; the returned
// data drives module loading in AppContent. The raw manifest carries no side effects — building the
// registry from it is done in an effect keyed on this query's data.
//
// The request is issued through the authenticated shell HTTP client (see createStudioEndpointContext),
// so the boot document attaches the bearer token when a user provider is configured and the #183
// management-key header always. When no client is supplied it falls back to a plain fetch (the
// anonymous/no-context path used by tests), preserving the "Manifest request failed with {status}"
// error contract that parseJsonResponse also surfaces.
export async function fetchStudioManifest(
  http?: StudioHttpClient,
  signal?: AbortSignal
): Promise<StudioModulesResponse> {
  if (http) {
    return http.getJson<StudioModulesResponse>("/_elsa/studio/modules", { signal });
  }

  const response = await fetch("/_elsa/studio/modules", { signal });
  if (!response.ok) {
    throw new Error(`Manifest request failed with ${response.status}.`);
  }

  return (await response.json()) as StudioModulesResponse;
}

export function useStudioManifest(http?: StudioHttpClient) {
  return useQuery({
    queryKey: shellKeys.manifest,
    queryFn: ({ signal }) => fetchStudioManifest(http, signal),
    // The manifest is a boot-time document; don't auto-refetch it on focus or on a timer. Module
    // changes explicitly bump the registry revision, which re-runs the loader without a re-fetch.
    staleTime: Infinity,
    refetchOnWindowFocus: false
  });
}
