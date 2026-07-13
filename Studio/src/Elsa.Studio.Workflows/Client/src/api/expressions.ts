import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import type {
  ExpressionDescriptor,
  ExpressionDescriptorsResponse,
  VariableTypeDescriptor,
  VariableTypeDescriptorsResponse
} from "../workflowTypes";
import { capabilityIds, resolveCapabilityLink } from "./capabilities";

export async function listExpressionDescriptors(context: StudioEndpointContext): Promise<ExpressionDescriptor[]> {
  const path = await resolveCapabilityLink(context, capabilityIds.expressions, "expression-descriptors");
  const response = await context.http.getJson<ExpressionDescriptorsResponse | ExpressionDescriptor[]>(path);
  if (Array.isArray(response)) return response;
  return response.items ?? response.descriptors ?? response.expressionDescriptors ?? [];
}

export async function listVariableTypeDescriptors(context: StudioEndpointContext): Promise<VariableTypeDescriptor[]> {
  const path = await resolveCapabilityLink(context, capabilityIds.expressions, "variable-types");
  const response = await context.http.getJson<VariableTypeDescriptorsResponse | VariableTypeDescriptor[]>(path);
  const descriptors = Array.isArray(response) ? response : response.items ?? response.descriptors ?? [];
  return descriptors.filter((descriptor): descriptor is VariableTypeDescriptor => {
    if (!descriptor || typeof descriptor !== "object") return false;
    return typeof descriptor.alias === "string" && descriptor.alias.length > 0;
  });
}
