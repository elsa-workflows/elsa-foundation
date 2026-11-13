# Dashboard contracts

Dashboard defines validated questions, saved resources and model-independent authoring. This package contains contracts only; later feature slices register their implementations.

## Replacements

Each of these contracts selects exactly one implementation per runtime context. Composition must diagnose missing or duplicate implementations rather than accept last-registration wins.

| Contract | Responsibility |
| --- | --- |
| `IDashboardQueryService` | Discover, validate, execute and drill a bounded native query |
| `IDashboardActorContext` | Resolve the trusted current tenant, canonical actor and effective permissions |
| `IDashboardResourceService` | Enforce definition access, revision history, keyed creation and conditional writes |
| `IDashboardResourceStore` | Persist definitions and revisions through separate read/write operations |
| `IDashboardDefinitionValidator` | Validate supported definitions and decide current disclosure authority |
| `IDashboardAudienceDirectory` | Resolve permitted same-tenant audience candidates |
| `IDashboardAuthoringService` | Prepare authoring context and delegate conversation to the configured Weaver runtime |

Actor/scope values are server-owned inputs. The native boundary resolves one canonical Dashboard actor from the current normalized identity. Query scope uses that actor's tenant and canonical UserId as SubjectId; AuthenticationSubjectId remains the owning authentication/Weaver identity and is never compared as though it were the canonical user ID. Hosts must derive both from the same current request and must not pair records from different callers. Constructing a record does not establish authority. Implementations recheck current resource and source access; sharing a definition grants no access to source rows or privileged cached results. Authoring proposals carry target, client generation and working revision and are never save receipts.

## Named contribution events

| Event | Contributor interface | Fan-in output |
| --- | --- | --- |
| `DashboardDatasetsCollecting` | `IDashboardDatasetExtension` | Authorized dataset declarations |
| `DashboardQueryValidating` | `IDashboardQueryValidationExtension` | Source-owned validation and interpretation |
| `DashboardQueryExecuting` | `IDashboardQueryExecutionExtension` | Current-viewer native query result |
| `DashboardQueryDrilling` | `IDashboardQueryDrillExtension` | Current-viewer source records |

The management feature publishes through Elsa Events using Sequential dispatch and one aggregating handler per event. It reads contributions back, so Background dispatch is unsuitable. Contributors declare dataset/schema ownership; duplicate or missing owners fail closed. Validation, execution and drill select the matching owner; unrelated contributors cannot supply a replacement result. Source modules own field/record/aggregate authorization, metric meaning, coverage and bounded reads. No contributor receives permission to execute arbitrary SQL, scripts or generated widget code.

See the [query contract](../../../../../specs/1217-ai-dashboards/contracts/query.md), [resource contract](../../../../../specs/1217-ai-dashboards/contracts/resources.md) and [authoring contract](../../../../../specs/1217-ai-dashboards/contracts/weaver.md) for admission and failure semantics.
