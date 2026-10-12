import { useEffect, useState } from "react";
import type { StudioAiContributionApi } from "@elsa-workflows/studio-sdk";

// Tracks whether the AI provider backing Weaver surfaces is currently available, so provider-gated
// affordances (e.g. the "Risks" toolbar action) can disable themselves instead of opening a dock that
// can only report "Weaver is unavailable". Defaults to available until the host reports otherwise.
export function useAiProviderAvailability(ai: StudioAiContributionApi): boolean {
  const [available, setAvailable] = useState(() => ai.providerAvailability.get());
  useEffect(() => ai.providerAvailability.subscribe(setAvailable), [ai]);
  return available;
}
