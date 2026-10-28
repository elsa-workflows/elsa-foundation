# BPMN hosting API extension points

`ActivitiesBpmnHostingApiFeature` is a public inheritable web feature with virtual service registration and endpoint mapping. It consumes the scoped [`IBpmnModelRuntime`](../Core/IBpmnModelRuntime.cs) replacement port; it owns no interpreter or persistence transaction.

Internal `BpmnHostingPermissionContributor` contributes the four permissions listed in the [README](README.md) through Foundation Identity's single permission aggregator (cross-domain contribution). `BpmnApiCaller` requires a normalized principal, captures its actor/tenant and evaluates resource authorization. The generated NativeEndpoints map owns JSON metadata; the keyed scoped fault renderer translates expected missing/conflict/validation/admission/schema refusals with the existing problem writer. Endpoint and transport DTO implementations are internal.

The feature depends on `ActivitiesBpmnHosting` and `FoundationIdentityAbstractions`; callers must provide the normal authenticated host and persistence scope. The [spec1017 hosting contract](../../../../../specs/1017-direct-bpmn-hosting/contracts/hosting.md) includes remaining delivery requirements beyond this initial profile.
