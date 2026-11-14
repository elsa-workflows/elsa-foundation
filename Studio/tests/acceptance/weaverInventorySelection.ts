export type InventoryFeature = { id: string; enabled: boolean; configuration: unknown };

/** Native Apply replaces the selected set: preserve every enabled feature and its masked configuration. */
export function desiredFeatureState(features: readonly InventoryFeature[], transitions: Readonly<Record<string, boolean>>) {
  for (const id of Object.keys(transitions)) {
    if (features.filter(feature => feature.id === id).length !== 1) throw new Error("Transition needs one discovered native feature");
  }
  return features.filter(feature => feature.enabled || Object.hasOwn(transitions, feature.id))
    .map(feature => ({ id: feature.id, enabled: Object.hasOwn(transitions, feature.id) ? transitions[feature.id] : feature.enabled,
      configuration: feature.configuration }));
}
