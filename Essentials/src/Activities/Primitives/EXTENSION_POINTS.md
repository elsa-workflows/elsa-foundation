# Primitive release inspection

`ActivitiesPrimitivesReleaseVerificationFeature` is an optional, inheritable feature with virtual service
configuration. It contributes one scoped internal `IReleaseOccurrenceInspector` strategy for the exact CLR activity
sites of `WriteLine` and `WriteLines`. It depends on the Runtime release bridge, without starting Runtime or invoking
the console sink. Other primitive classes are not claimed by this feature.

The supported profile checks typed input/result/activation contracts, actual replay/background declarations, empty
structure/child/output-capture/resume surfaces and direct secret refusal. Source-owned template roots may retain
required inputs unbound; concrete workflow bodies must satisfy their pinned required/default contract. Identity and
nullable input conversions are supported; other conversion operations remain unsupported in this profile.

Every additional opaque facet still requires its exact owner. A successful leaf answer grants no origin confidence,
target readiness, execution, import or serving authority. Captured typed views are read-only inputs to trusted inspectors.
