import { useQuery } from "@tanstack/react-query";
import type { StudioModulesResponse } from "../../sdk";
import { shellKeys } from "../queryKeys";

// The shell boot manifest (`/_elsa/studio/modules`) served by the Studio host. Fetched through
// TanStack Query so the shell shares one cache/retry policy with the rest of the app; the returned
// data drives module loading in AppContent. The raw manifest carries no side effects — building the
// registry from it is done in an effect keyed on this query's data.
export async function fetchStudioManifest(signal?: AbortSignal): Promise<StudioModulesResponse> {
  const response = await fetch("/_elsa/studio/modules", { signal });
  if (!response.ok) {
    throw new Error(`Manifest request failed with ${response.status}.`);
  }

  return (await response.json()) as StudioModulesResponse;
}

export function useStudioManifest() {
  return useQuery({
    queryKey: shellKeys.manifest,
    queryFn: ({ signal }) => fetchStudioManifest(signal),
    // The manifest is a boot-time document; don't auto-refetch it on focus or on a timer. Module
    // changes explicitly bump the registry revision, which re-runs the loader without a re-fetch.
    staleTime: Infinity,
    refetchOnWindowFocus: false
  });
}
