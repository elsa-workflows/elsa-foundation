import type { RuntimeValueTypeDescriptor } from "./workflowTypes";

export function runtimeValueTypeLabel(type?: RuntimeValueTypeDescriptor | null) {
  return type?.displayName?.trim() ||
    type?.typeName?.trim() ||
    type?.alias?.trim() ||
    type?.id?.trim() ||
    type?.kind?.trim() ||
    undefined;
}
