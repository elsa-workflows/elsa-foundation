import type { StudioActivityDescriptor, StudioActivityInputOption } from "@elsa-workflows/studio-sdk";

export const supportedMethodOptions: StudioActivityInputOption[] = ["GET", "POST", "PUT", "HEAD", "DELETE"]
  .map(value => ({ label: value, value }));

export function activityInputOptionsDescriptor(
  input: Partial<StudioActivityDescriptor["inputs"][number]> = {}
): StudioActivityDescriptor {
  return {
    typeName: "Tests.OptionActivity",
    name: "OptionActivity",
    displayName: "Option Activity",
    inputs: [{
      name: "Choice",
      typeName: "System.String",
      displayName: "Choice",
      isWrapped: true,
      defaultSyntax: "Literal",
      ...input
    }],
    outputs: [],
    ports: []
  };
}
