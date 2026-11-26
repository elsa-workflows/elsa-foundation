import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { capabilityIds, resolveCapabilityLink } from "./capabilities";

export async function getHostPersistenceMode(context: StudioEndpointContext): Promise<string> {
  const path = await resolveCapabilityLink(context, capabilityIds.runtime, "runtime-persistence-mode");
  const value = await context.http.getJson<unknown>(path);
  if (!value || typeof value !== "object" || !("persistenceMode" in value)
    || typeof value.persistenceMode !== "string" || !value.persistenceMode.trim()) {
    throw new Error("The host did not return a persistence mode.");
  }
  return value.persistenceMode.trim();
}
