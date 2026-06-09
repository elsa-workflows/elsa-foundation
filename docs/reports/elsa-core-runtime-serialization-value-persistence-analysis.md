# Elsa Core Runtime Serialization And Value Persistence Analysis

Status: source-backed analysis for brainstorm topic 1. This is not a design decision, Speckit spec, or implementation plan.

Program goal state: [Runtime Execution Seam](../program-goals/runtime-execution-seam.md).

Parent report: [Elsa Core runtime broken windows brainstorm](elsa-core-runtime-broken-windows-brainstorm.md), topic 1: Serialization and runtime value persistence.

## Inspection Scope

Elsa 3 source inspected from local checkout `/Users/sipke/Projects/Elsa/elsa-core`.

- Repository: `https://github.com/elsa-workflows/elsa-core.git`
- Branch: `release/3.8.0`
- Commit: `20c1064ca5ce705baba934cf77239b8db2ccdc56`
- Working tree note: the checkout had unrelated local changes in modular server/platform integration files. The serialization/runtime files referenced below were inspected read-only.

This report focuses on workflow definition serialization, workflow instance serialization, workflow variables, workflow input/output and activity output persistence, custom converters, `ObjectConverter`, Newtonsoft versus `System.Text.Json`, API/import/export JSON shapes, and compatibility risks for existing user databases.

## Executive Finding

The maintainer concern is confirmed, with one correction.

Confirmed: Elsa 3 has several overlapping JSON serialization paths, each with different converter sets and persisted shapes. Workflow definitions use API/activity serialization plus EF shadow JSON state. Workflow instances use a separate workflow-state serializer with cross-scope reference handling and polymorphic object conversion. Variables, workflow inputs, workflow outputs, activity inputs, activity outputs, bookmarks, queue options, and execution logs each use different persistence rules and sometimes different serializers.

Correction: activity outputs are not only "not persisted" in Elsa 3. They are available through the in-memory `ActivityOutputRegister` during execution, and selected outputs can be persisted in activity execution records/log history. What is not present is a single durable runtime value model that makes activity outputs first-class workflow state for later execution unless the value is captured into persisted state such as variables or workflow output.

## Serializer Surfaces

Elsa registers several serializer services in the core workflow feature:

- `IWorkflowStateSerializer` -> `JsonWorkflowStateSerializer`
- `IPayloadSerializer` -> `JsonPayloadSerializer`
- `IActivitySerializer` -> `JsonActivitySerializer`
- `IApiSerializer` -> `ApiSerializer`
- `ISafeSerializer` -> `SafeSerializer`

Source refs:

- `src/modules/Elsa.Workflows.Core/ShellFeatures/WorkflowsFeature.cs:187`
- `src/modules/Elsa.Workflows.Core/ShellFeatures/WorkflowsFeature.cs:188`
- `src/modules/Elsa.Workflows.Core/ShellFeatures/WorkflowsFeature.cs:189`
- `src/modules/Elsa.Workflows.Core/ShellFeatures/WorkflowsFeature.cs:190`
- `src/modules/Elsa.Workflows.Core/ShellFeatures/WorkflowsFeature.cs:191`
- `src/modules/Elsa.Workflows.Core/ShellFeatures/WorkflowsFeature.cs:192`
- `src/modules/Elsa.Workflows.Core/ShellFeatures/WorkflowsFeature.cs:193`

The serializers share a `ConfigurableSerializer` base for some paths. The base defaults to camel case, case-insensitive properties, null omission, Unicode encoder, string enums, TimeSpan, integer, big integer, and decimal converters, then runs registered configurators.

Source refs:

- `src/modules/Elsa.Common/Serialization/ConfigurableSerializer.cs:34`
- `src/modules/Elsa.Common/Serialization/ConfigurableSerializer.cs:48`
- `src/modules/Elsa.Common/Serialization/ConfigurableSerializer.cs:71`
- `src/modules/Elsa.Common/Serialization/ConfigurableSerializer.cs:79`
- `src/modules/Elsa.Common/Serialization/ConfigurableSerializer.cs:107`

The important divergence is that each specialized serializer adds different converters:

- `ApiSerializer` adds type and function-expression converters.
- `JsonActivitySerializer` adds type, input, output, expression, and function-expression converters.
- `JsonPayloadSerializer` creates fresh options with polymorphic object, type, variable, function-expression, string enum, and TimeSpan converters.
- `SafeSerializer` adds string enum camel case, type, safe-value, expression, and function-expression converters.
- `JsonWorkflowStateSerializer` adds type, polymorphic object, variable, function-expression converters and wraps options with `CrossScopedReferenceHandler`.

Source refs:

- `src/modules/Elsa.Workflows.Core/Serialization/Serializers/ApiSerializer.cs:34`
- `src/modules/Elsa.Workflows.Core/Serialization/Serializers/ApiSerializer.cs:40`
- `src/modules/Elsa.Workflows.Core/Serialization/Serializers/JsonActivitySerializer.cs:34`
- `src/modules/Elsa.Workflows.Core/Serialization/Serializers/JsonPayloadSerializer.cs:71`
- `src/modules/Elsa.Workflows.Core/Serialization/Serializers/JsonPayloadSerializer.cs:80`
- `src/modules/Elsa.Workflows.Core/Serialization/Serializers/SafeSerializer.cs:71`
- `src/modules/Elsa.Workflows.Core/Serialization/Serializers/JsonWorkflowStateSerializer.cs:128`
- `src/modules/Elsa.Workflows.Core/Serialization/Serializers/JsonWorkflowStateSerializer.cs:135`

## Workflow Definition Serialization

The management entity is not itself the API JSON shape. `WorkflowDefinition` contains metadata plus `Options`, `Variables`, `Inputs`, `Outputs`, `Outcomes`, `CustomProperties`, `MaterializerName`, `MaterializerContext`, `StringData`, `OriginalSource`, and `BinaryData`.

Source refs:

- `src/modules/Elsa.Workflows.Management/Entities/WorkflowDefinition.cs:10`
- `src/modules/Elsa.Workflows.Management/Entities/WorkflowDefinition.cs:35`
- `src/modules/Elsa.Workflows.Management/Entities/WorkflowDefinition.cs:40`
- `src/modules/Elsa.Workflows.Management/Entities/WorkflowDefinition.cs:45`
- `src/modules/Elsa.Workflows.Management/Entities/WorkflowDefinition.cs:50`
- `src/modules/Elsa.Workflows.Management/Entities/WorkflowDefinition.cs:60`
- `src/modules/Elsa.Workflows.Management/Entities/WorkflowDefinition.cs:70`
- `src/modules/Elsa.Workflows.Management/Entities/WorkflowDefinition.cs:80`
- `src/modules/Elsa.Workflows.Management/Entities/WorkflowDefinition.cs:87`

EF ignores several rich properties and stores a shadow `Data` string plus `UsableAsActivity`. The `Data` value is a `WorkflowDefinitionState` serialized through `IPayloadSerializer`, while `StringData` stores the serialized root activity.

Source refs:

- `src/modules/Elsa.Persistence.EFCore/Modules/Management/Configurations.cs:13`
- `src/modules/Elsa.Persistence.EFCore/Modules/Management/Configurations.cs:15`
- `src/modules/Elsa.Persistence.EFCore/Modules/Management/Configurations.cs:21`
- `src/modules/Elsa.Persistence.EFCore/Modules/Management/Configurations.cs:22`
- `src/modules/Elsa.Persistence.EFCore/Modules/Management/WorkflowDefinitionStore.cs:164`
- `src/modules/Elsa.Persistence.EFCore/Modules/Management/WorkflowDefinitionStore.cs:166`
- `src/modules/Elsa.Persistence.EFCore/Modules/Management/WorkflowDefinitionStore.cs:167`
- `src/modules/Elsa.Persistence.EFCore/Modules/Management/WorkflowDefinitionStore.cs:169`
- `src/modules/Elsa.Persistence.EFCore/Modules/Management/WorkflowDefinitionStore.cs:174`
- `src/modules/Elsa.Persistence.EFCore/Modules/Management/WorkflowDefinitionStore.cs:185`

There is also a newer `OriginalSource` round-trip path: if present, the mapper deserializes a full `WorkflowDefinitionModel`; otherwise it falls back to `StringData`. This is explicit backward compatibility, and it shows that Elsa 3 already has multiple definition shapes in circulation.

Source refs:

- `src/modules/Elsa.Workflows.Management/Mappers/WorkflowDefinitionMapper.cs:33`
- `src/modules/Elsa.Workflows.Management/Mappers/WorkflowDefinitionMapper.cs:35`
- `src/modules/Elsa.Workflows.Management/Mappers/WorkflowDefinitionMapper.cs:39`
- `src/modules/Elsa.Workflows.Management/Mappers/WorkflowDefinitionMapper.cs:43`
- `src/modules/Elsa.Workflows.Management/Mappers/WorkflowDefinitionMapper.cs:44`

## API And Import/Export JSON Shapes

The API/import/export model is `WorkflowDefinitionModel`, which includes ID/version metadata, variables, inputs, outputs, outcomes, custom properties, publication flags, options, obsolete `UsableAsActivity`, and `Root`.

Source refs:

- `src/modules/Elsa.Workflows.Management/Models/WorkflowDefinitionModel.cs:10`
- `src/modules/Elsa.Workflows.Management/Models/WorkflowDefinitionModel.cs:63`
- `src/modules/Elsa.Workflows.Management/Models/WorkflowDefinitionModel.cs:71`
- `src/modules/Elsa.Workflows.Management/Models/WorkflowDefinitionModel.cs:72`
- `src/modules/Elsa.Workflows.Management/Models/WorkflowDefinitionModel.cs:73`
- `src/modules/Elsa.Workflows.Management/Models/WorkflowDefinitionModel.cs:80`
- `src/modules/Elsa.Workflows.Management/Models/WorkflowDefinitionModel.cs:82`
- `src/modules/Elsa.Workflows.Management/Models/WorkflowDefinitionModel.cs:85`

The POST endpoint accepts `SaveWorkflowDefinitionRequest`, extracts the model root, serializes the root activity through API serializer options, and stores variables/inputs/outputs/outcomes separately on the draft.

Source refs:

- `src/modules/Elsa.Workflows.Api/Endpoints/WorkflowDefinitions/Post/Endpoint.cs:38`
- `src/modules/Elsa.Workflows.Api/Endpoints/WorkflowDefinitions/Post/Endpoint.cs:76`
- `src/modules/Elsa.Workflows.Api/Endpoints/WorkflowDefinitions/Post/Endpoint.cs:78`
- `src/modules/Elsa.Workflows.Api/Endpoints/WorkflowDefinitions/Post/Endpoint.cs:79`
- `src/modules/Elsa.Workflows.Api/Endpoints/WorkflowDefinitions/Post/Endpoint.cs:85`
- `src/modules/Elsa.Workflows.Api/Endpoints/WorkflowDefinitions/Post/Endpoint.cs:91`
- `src/modules/Elsa.Workflows.Api/Endpoints/WorkflowDefinitions/Post/Endpoint.cs:92`
- `src/modules/Elsa.Workflows.Api/Endpoints/WorkflowDefinitions/Post/Endpoint.cs:93`

Import accepts `WorkflowDefinitionModel`, then `WorkflowDefinitionImporter` serializes only the root into `StringData`, maps variable definitions, and assigns inputs/outputs/outcomes/options separately.

Source refs:

- `src/modules/Elsa.Workflows.Api/Endpoints/WorkflowDefinitions/Import/Endpoint.cs:46`
- `src/modules/Elsa.Workflows.Api/Endpoints/WorkflowDefinitions/Import/Endpoint.cs:82`
- `src/modules/Elsa.Workflows.Management/Services/WorkflowDefinitionImporter.cs:31`
- `src/modules/Elsa.Workflows.Management/Services/WorkflowDefinitionImporter.cs:56`
- `src/modules/Elsa.Workflows.Management/Services/WorkflowDefinitionImporter.cs:57`
- `src/modules/Elsa.Workflows.Management/Services/WorkflowDefinitionImporter.cs:60`
- `src/modules/Elsa.Workflows.Management/Services/WorkflowDefinitionImporter.cs:65`
- `src/modules/Elsa.Workflows.Management/Services/WorkflowDefinitionImporter.cs:66`
- `src/modules/Elsa.Workflows.Management/Services/WorkflowDefinitionImporter.cs:67`

File import reads JSON files directly or JSON files inside ZIP archives, then deserializes each file with `IApiSerializer`.

Source refs:

- `src/modules/Elsa.Workflows.Api/Endpoints/WorkflowDefinitions/ImportFiles/WorkflowDefinitionImportFileReader.cs:9`
- `src/modules/Elsa.Workflows.Api/Endpoints/WorkflowDefinitions/ImportFiles/WorkflowDefinitionImportFileReader.cs:20`
- `src/modules/Elsa.Workflows.Api/Endpoints/WorkflowDefinitions/ImportFiles/WorkflowDefinitionImportFileReader.cs:26`
- `src/modules/Elsa.Workflows.Api/Endpoints/WorkflowDefinitions/ImportFiles/WorkflowDefinitionImportFileReader.cs:44`
- `src/modules/Elsa.Workflows.Api/Endpoints/WorkflowDefinitions/ImportFiles/WorkflowDefinitionImportFileReader.cs:48`

Export maps a stored definition to `WorkflowDefinitionModel`, serializes it with `IApiSerializer`, and injects a `$schema` property. Multiple exports become a ZIP. This makes export JSON close to API JSON, not the EF row shape.

Source refs:

- `src/modules/Elsa.Workflows.Management/Services/WorkflowDefinitionExporter.cs:12`
- `src/modules/Elsa.Workflows.Management/Services/WorkflowDefinitionExporter.cs:59`
- `src/modules/Elsa.Workflows.Management/Services/WorkflowDefinitionExporter.cs:61`
- `src/modules/Elsa.Workflows.Management/Services/WorkflowDefinitionExporter.cs:68`
- `src/modules/Elsa.Workflows.Management/Services/WorkflowDefinitionExporter.cs:156`
- `src/modules/Elsa.Workflows.Management/Services/WorkflowDefinitionExporter.cs:158`
- `src/modules/Elsa.Workflows.Management/Services/WorkflowDefinitionExporter.cs:159`
- `src/modules/Elsa.Workflows.Management/Services/WorkflowDefinitionExporter.cs:165`
- `src/modules/Elsa.Workflows.Management/Services/WorkflowDefinitionExporter.cs:166`

## Workflow Instance Serialization

`WorkflowInstance` stores status/query metadata plus a rich `WorkflowState`.

Source refs:

- `src/modules/Elsa.Workflows.Management/Entities/WorkflowInstance.cs:9`
- `src/modules/Elsa.Workflows.Management/Entities/WorkflowInstance.cs:14`
- `src/modules/Elsa.Workflows.Management/Entities/WorkflowInstance.cs:34`
- `src/modules/Elsa.Workflows.Management/Entities/WorkflowInstance.cs:39`
- `src/modules/Elsa.Workflows.Management/Entities/WorkflowInstance.cs:44`
- `src/modules/Elsa.Workflows.Management/Entities/WorkflowInstance.cs:55`

EF ignores `WorkflowState` and stores it in shadow properties `Data` and `DataCompressionAlgorithm`. Save serializes the whole `WorkflowState` with `IWorkflowStateSerializer`, then compresses it. Load decompresses and deserializes it, and on failure logs a warning and reverts to default state.

Source refs:

- `src/modules/Elsa.Persistence.EFCore/Modules/Management/Configurations.cs:34`
- `src/modules/Elsa.Persistence.EFCore/Modules/Management/Configurations.cs:36`
- `src/modules/Elsa.Persistence.EFCore/Modules/Management/Configurations.cs:37`
- `src/modules/Elsa.Persistence.EFCore/Modules/Management/Configurations.cs:38`
- `src/modules/Elsa.Persistence.EFCore/Modules/Management/WorkflowInstanceStore.cs:221`
- `src/modules/Elsa.Persistence.EFCore/Modules/Management/WorkflowInstanceStore.cs:225`
- `src/modules/Elsa.Persistence.EFCore/Modules/Management/WorkflowInstanceStore.cs:226`
- `src/modules/Elsa.Persistence.EFCore/Modules/Management/WorkflowInstanceStore.cs:230`
- `src/modules/Elsa.Persistence.EFCore/Modules/Management/WorkflowInstanceStore.cs:239`
- `src/modules/Elsa.Persistence.EFCore/Modules/Management/WorkflowInstanceStore.cs:248`
- `src/modules/Elsa.Persistence.EFCore/Modules/Management/WorkflowInstanceStore.cs:249`
- `src/modules/Elsa.Persistence.EFCore/Modules/Management/WorkflowInstanceStore.cs:254`

`WorkflowState` includes workflow identity/status, bookmarks, incidents, completion callbacks, active activity execution contexts, scheduled activities, workflow input, workflow output, and properties. These dictionaries contain `object` values, so the state serializer must handle arbitrary runtime objects.

Source refs:

- `src/modules/Elsa.Workflows.Core/State/WorkflowState.cs:9`
- `src/modules/Elsa.Workflows.Core/State/WorkflowState.cs:64`
- `src/modules/Elsa.Workflows.Core/State/WorkflowState.cs:70`
- `src/modules/Elsa.Workflows.Core/State/WorkflowState.cs:80`
- `src/modules/Elsa.Workflows.Core/State/WorkflowState.cs:86`
- `src/modules/Elsa.Workflows.Core/State/WorkflowState.cs:91`
- `src/modules/Elsa.Workflows.Core/State/WorkflowState.cs:101`
- `src/modules/Elsa.Workflows.Core/State/WorkflowState.cs:106`
- `src/modules/Elsa.Workflows.Core/State/WorkflowState.cs:111`

The extractor copies output and properties directly, but only persists workflow inputs that have a `WorkflowStorageDriver` or `WorkflowInstanceStorageDriver`. The source calls this temporary.

Source refs:

- `src/modules/Elsa.Workflows.Core/Services/WorkflowStateExtractor.cs:13`
- `src/modules/Elsa.Workflows.Core/Services/WorkflowStateExtractor.cs:29`
- `src/modules/Elsa.Workflows.Core/Services/WorkflowStateExtractor.cs:30`
- `src/modules/Elsa.Workflows.Core/Services/WorkflowStateExtractor.cs:38`
- `src/modules/Elsa.Workflows.Core/Services/WorkflowStateExtractor.cs:77`
- `src/modules/Elsa.Workflows.Core/Services/WorkflowStateExtractor.cs:79`
- `src/modules/Elsa.Workflows.Core/Services/WorkflowStateExtractor.cs:80`
- `src/modules/Elsa.Workflows.Core/Services/WorkflowStateExtractor.cs:86`

## Variables, Inputs, Outputs, And Activity Outputs

Workflow variables use a definition model that stores ID, name, type name, array flag, value string, and storage driver type name.

Source refs:

- `src/modules/Elsa.Workflows.Core/Models/VariableDefinition.cs:6`
- `src/modules/Elsa.Workflows.Management/Mappers/VariableDefinitionMapper.cs:20`
- `src/modules/Elsa.Workflows.Management/Mappers/VariableDefinitionMapper.cs:22`
- `src/modules/Elsa.Workflows.Management/Mappers/VariableDefinitionMapper.cs:41`
- `src/modules/Elsa.Workflows.Management/Mappers/VariableDefinitionMapper.cs:50`
- `src/modules/Elsa.Workflows.Management/Mappers/VariableDefinitionMapper.cs:68`
- `src/modules/Elsa.Workflows.Management/Mappers/VariableDefinitionMapper.cs:72`
- `src/modules/Elsa.Workflows.Management/Mappers/VariableDefinitionMapper.cs:74`
- `src/modules/Elsa.Workflows.Management/Mappers/VariableDefinitionMapper.cs:75`
- `src/modules/Elsa.Workflows.Management/Mappers/VariableDefinitionMapper.cs:108`

Runtime variables are loaded and saved through storage drivers. The workflow-instance storage driver writes variable values as `JsonNode` under `Properties["Variables"]` and reads them back with `ObjectConverter` plus `IPayloadSerializer` options. Serialization failures are logged and the variable is skipped.

Source refs:

- `src/modules/Elsa.Workflows.Core/Services/VariablePersistenceManager.cs:13`
- `src/modules/Elsa.Workflows.Core/Services/VariablePersistenceManager.cs:47`
- `src/modules/Elsa.Workflows.Core/Services/VariablePersistenceManager.cs:52`
- `src/modules/Elsa.Workflows.Core/Services/VariablePersistenceManager.cs:72`
- `src/modules/Elsa.Workflows.Core/Services/VariablePersistenceManager.cs:91`
- `src/modules/Elsa.Workflows.Core/Services/VariablePersistenceManager.cs:97`
- `src/modules/Elsa.Workflows.Core/VariableStorageDrivers/WorkflowInstanceStorageDriver.cs:21`
- `src/modules/Elsa.Workflows.Core/VariableStorageDrivers/WorkflowInstanceStorageDriver.cs:35`
- `src/modules/Elsa.Workflows.Core/VariableStorageDrivers/WorkflowInstanceStorageDriver.cs:40`
- `src/modules/Elsa.Workflows.Core/VariableStorageDrivers/WorkflowInstanceStorageDriver.cs:56`
- `src/modules/Elsa.Workflows.Core/VariableStorageDrivers/WorkflowInstanceStorageDriver.cs:61`
- `src/modules/Elsa.Workflows.Core/VariableStorageDrivers/WorkflowInstanceStorageDriver.cs:73`

Workflow inputs and outputs share `ArgumentDefinition`, but inputs add `UIHint` and `StorageDriverType`; outputs add no extra fields. Runtime workflow input/output values are dictionaries in `WorkflowState`.

Source refs:

- `src/modules/Elsa.Workflows.Core/Models/ArgumentDefinition.cs:6`
- `src/modules/Elsa.Workflows.Core/Models/ArgumentDefinition.cs:11`
- `src/modules/Elsa.Workflows.Core/Models/ArgumentDefinition.cs:16`
- `src/modules/Elsa.Workflows.Core/Models/InputDefinition.cs:6`
- `src/modules/Elsa.Workflows.Core/Models/InputDefinition.cs:11`
- `src/modules/Elsa.Workflows.Core/Models/InputDefinition.cs:16`
- `src/modules/Elsa.Workflows.Core/Models/OutputDefinition.cs:6`
- `src/modules/Elsa.Workflows.Core/State/WorkflowState.cs:101`
- `src/modules/Elsa.Workflows.Core/State/WorkflowState.cs:106`

Activity inputs are evaluated before execution and stored in activity state unless sensitive or marked non-serializable. The implementation currently stores the raw evaluated value after commented-out safe serialization/filtering, which is another special-case boundary.

Source refs:

- `src/modules/Elsa.Workflows.Core/Extensions/ActivityExecutionContextExtensions.InputEvaluation.cs:20`
- `src/modules/Elsa.Workflows.Core/Extensions/ActivityExecutionContextExtensions.InputEvaluation.cs:85`
- `src/modules/Elsa.Workflows.Core/Extensions/ActivityExecutionContextExtensions.InputEvaluation.cs:121`
- `src/modules/Elsa.Workflows.Core/Extensions/ActivityExecutionContextExtensions.InputEvaluation.cs:130`
- `src/modules/Elsa.Workflows.Core/Extensions/ActivityExecutionContextExtensions.InputEvaluation.cs:144`
- `src/modules/Elsa.Workflows.Core/Extensions/ActivityExecutionContextExtensions.InputEvaluation.cs:146`
- `src/modules/Elsa.Workflows.Core/Extensions/ActivityExecutionContextExtensions.InputEvaluation.cs:152`
- `src/modules/Elsa.Workflows.Core/Extensions/ActivityExecutionContextExtensions.InputEvaluation.cs:154`

Activity outputs are recorded in the in-memory `ActivityOutputRegister` by activity ID, activity instance ID, output name, container ID, and value. This is not the same as workflow state persistence.

Source refs:

- `src/modules/Elsa.Workflows.Core/Models/ActivityOutputRegister.cs:6`
- `src/modules/Elsa.Workflows.Core/Models/ActivityOutputRegister.cs:21`
- `src/modules/Elsa.Workflows.Core/Models/ActivityOutputRegister.cs:32`
- `src/modules/Elsa.Workflows.Core/Models/ActivityOutputRegister.cs:47`
- `src/modules/Elsa.Workflows.Core/Models/ActivityOutputRegister.cs:77`
- `src/modules/Elsa.Workflows.Core/Models/ActivityOutputRegister.cs:91`
- `src/modules/Elsa.Workflows.Core/Models/ActivityOutputRecord.cs:11`

Activity execution records persist selected inputs/activity state and selected outputs for log/history. The mapper uses log persistence settings, serializes activity state and outputs with `ISafeSerializer`, and stores them in runtime EF shadow properties. On load, outputs are deserialized with plain `JsonSerializer.Deserialize<IDictionary<string, object?>>`, while properties/metadata/payload use `IPayloadSerializer`.

Source refs:

- `src/modules/Elsa.Workflows.Runtime/Services/DefaultActivityExecutionMapper.cs:20`
- `src/modules/Elsa.Workflows.Runtime/Services/DefaultActivityExecutionMapper.cs:22`
- `src/modules/Elsa.Workflows.Runtime/Services/DefaultActivityExecutionMapper.cs:25`
- `src/modules/Elsa.Workflows.Runtime/Services/DefaultActivityExecutionMapper.cs:26`
- `src/modules/Elsa.Workflows.Runtime/Services/DefaultActivityExecutionMapper.cs:59`
- `src/modules/Elsa.Workflows.Runtime/Services/DefaultActivityExecutionMapper.cs:80`
- `src/modules/Elsa.Persistence.EFCore/Modules/Runtime/ActivityExecutionLogStore.cs:85`
- `src/modules/Elsa.Persistence.EFCore/Modules/Runtime/ActivityExecutionLogStore.cs:92`
- `src/modules/Elsa.Persistence.EFCore/Modules/Runtime/ActivityExecutionLogStore.cs:94`
- `src/modules/Elsa.Persistence.EFCore/Modules/Runtime/ActivityExecutionLogStore.cs:108`
- `src/modules/Elsa.Persistence.EFCore/Modules/Runtime/ActivityExecutionLogStore.cs:109`
- `src/modules/Elsa.Persistence.EFCore/Modules/Runtime/ActivityExecutionLogStore.cs:126`
- `src/modules/Elsa.Persistence.EFCore/Modules/Runtime/ActivityExecutionLogStore.cs:137`
- `src/modules/Elsa.Persistence.EFCore/Modules/Runtime/ActivityExecutionLogStore.cs:144`

## Converter Complexity And ObjectConverter

The activity converter resolves activity descriptors from JSON `type` and `version`, handles missing activities by creating `NotFoundActivity`, stores original activity JSON, and has special workflow-as-activity lookup behavior using `workflowDefinitionVersionId` or `workflowDefinitionId`.

Source refs:

- `src/modules/Elsa.Workflows.Core/Serialization/Converters/ActivityJsonConverter.cs:25`
- `src/modules/Elsa.Workflows.Core/Serialization/Converters/ActivityJsonConverter.cs:31`
- `src/modules/Elsa.Workflows.Core/Serialization/Converters/ActivityJsonConverter.cs:43`
- `src/modules/Elsa.Workflows.Core/Serialization/Converters/ActivityJsonConverter.cs:45`
- `src/modules/Elsa.Workflows.Core/Serialization/Converters/ActivityJsonConverter.cs:54`
- `src/modules/Elsa.Workflows.Core/Serialization/Converters/ActivityJsonConverter.cs:76`
- `src/modules/Elsa.Workflows.Core/Serialization/Converters/ActivityJsonConverter.cs:130`
- `src/modules/Elsa.Workflows.Core/Serialization/Converters/ActivityJsonConverter.cs:145`

`InputJsonConverter`, `OutputJsonConverter`, and `VariableConverter` use separate custom models/construction logic. Inputs serialize `typeName` plus expression; outputs serialize `typeName` plus `memoryReference`; variables map through `VariableModel`.

Source refs:

- `src/modules/Elsa.Workflows.Core/Serialization/Converters/InputJsonConverter.cs:29`
- `src/modules/Elsa.Workflows.Core/Serialization/Converters/InputJsonConverter.cs:37`
- `src/modules/Elsa.Workflows.Core/Serialization/Converters/InputJsonConverter.cs:50`
- `src/modules/Elsa.Workflows.Core/Serialization/Converters/InputJsonConverter.cs:79`
- `src/modules/Elsa.Workflows.Core/Serialization/Converters/OutputJsonConverter.cs:27`
- `src/modules/Elsa.Workflows.Core/Serialization/Converters/OutputJsonConverter.cs:35`
- `src/modules/Elsa.Workflows.Core/Serialization/Converters/OutputJsonConverter.cs:57`
- `src/modules/Elsa.Workflows.Core/Serialization/Converters/VariableConverter.cs:26`
- `src/modules/Elsa.Workflows.Core/Serialization/Converters/VariableConverter.cs:38`

`PolymorphicObjectConverter` is especially broad. It writes and reads `_type`, `_items`, `_island`, `$id`, `$ref`, and `$values` conventions; it preserves primitives directly; it special-cases `JObject`, `JArray`, `JsonObject`, and `JsonArray` through a JSON island string; it removes `_type` before dictionary deserialization; and it sanitizes `ExpandoObject` property names.

Source refs:

- `src/modules/Elsa.Workflows.Core/Serialization/Converters/PolymorphicObjectConverter.cs:17`
- `src/modules/Elsa.Workflows.Core/Serialization/Converters/PolymorphicObjectConverter.cs:19`
- `src/modules/Elsa.Workflows.Core/Serialization/Converters/PolymorphicObjectConverter.cs:20`
- `src/modules/Elsa.Workflows.Core/Serialization/Converters/PolymorphicObjectConverter.cs:21`
- `src/modules/Elsa.Workflows.Core/Serialization/Converters/PolymorphicObjectConverter.cs:44`
- `src/modules/Elsa.Workflows.Core/Serialization/Converters/PolymorphicObjectConverter.cs:48`
- `src/modules/Elsa.Workflows.Core/Serialization/Converters/PolymorphicObjectConverter.cs:51`
- `src/modules/Elsa.Workflows.Core/Serialization/Converters/PolymorphicObjectConverter.cs:72`
- `src/modules/Elsa.Workflows.Core/Serialization/Converters/PolymorphicObjectConverter.cs:82`
- `src/modules/Elsa.Workflows.Core/Serialization/Converters/PolymorphicObjectConverter.cs:92`
- `src/modules/Elsa.Workflows.Core/Serialization/Converters/PolymorphicObjectConverter.cs:102`
- `src/modules/Elsa.Workflows.Core/Serialization/Converters/PolymorphicObjectConverter.cs:111`
- `src/modules/Elsa.Workflows.Core/Serialization/Converters/PolymorphicObjectConverter.cs:115`
- `src/modules/Elsa.Workflows.Core/Serialization/Converters/PolymorphicObjectConverter.cs:215`
- `src/modules/Elsa.Workflows.Core/Serialization/Converters/PolymorphicObjectConverter.cs:218`
- `src/modules/Elsa.Workflows.Core/Serialization/Converters/PolymorphicObjectConverter.cs:238`

`SafeSerializer` does not make values fully round-trippable. Its registered `SafeValueConverterFactory` creates `SafeValueConverter`, which attempts serialization and, on failure, writes a fallback object containing only the runtime type name. A similar `SafeDictionaryConverter` exists in the source tree, but no registration was found in the inspected serializer setup. The fallback preserves diagnostic evidence but loses value content.

Source refs:

- `src/modules/Elsa.Workflows.Core/Serialization/Serializers/SafeSerializer.cs:12`
- `src/modules/Elsa.Workflows.Core/Serialization/Serializers/SafeSerializer.cs:78`
- `src/modules/Elsa.Workflows.Core/Serialization/Converters/SafeValueConverterFactory.cs:12`
- `src/modules/Elsa.Workflows.Core/Serialization/Converters/SafeValueConverterFactory.cs:15`
- `src/modules/Elsa.Workflows.Core/Serialization/Converters/SafeValueConverter.cs:7`
- `src/modules/Elsa.Workflows.Core/Serialization/Converters/SafeValueConverter.cs:23`
- `src/modules/Elsa.Workflows.Core/Serialization/Converters/SafeValueConverter.cs:36`
- `src/modules/Elsa.Workflows.Core/Serialization/Converters/SafeValueConverter.cs:40`
- `src/modules/Elsa.Workflows.Core/Serialization/Converters/SafeDictionaryConverter.cs:6`
- `src/modules/Elsa.Workflows.Core/Serialization/Converters/SafeDictionaryConverter.cs:19`
- `src/modules/Elsa.Workflows.Core/Serialization/Converters/SafeDictionaryConverter.cs:38`
- `src/modules/Elsa.Workflows.Core/Serialization/Converters/SafeDictionaryConverter.cs:42`

`ObjectConverter` is a separate conversion layer, not just serializer configuration. It handles `JsonElement`, `JsonNode`, strings containing JSON, dictionaries, enumerables, type converters, enum conversion, date conversions, and `Convert.ChangeType`. It has global `StrictMode`, defaulting to false, and backward-compatible fallback to default target values when conversion fails unless strict mode is enabled.

Source refs:

- `src/modules/Elsa.Expressions/Helpers/ObjectConverter.cs:24`
- `src/modules/Elsa.Expressions/Helpers/ObjectConverter.cs:35`
- `src/modules/Elsa.Expressions/Helpers/ObjectConverter.cs:88`
- `src/modules/Elsa.Expressions/Helpers/ObjectConverter.cs:90`
- `src/modules/Elsa.Expressions/Helpers/ObjectConverter.cs:107`
- `src/modules/Elsa.Expressions/Helpers/ObjectConverter.cs:115`
- `src/modules/Elsa.Expressions/Helpers/ObjectConverter.cs:151`
- `src/modules/Elsa.Expressions/Helpers/ObjectConverter.cs:188`
- `src/modules/Elsa.Expressions/Helpers/ObjectConverter.cs:208`
- `src/modules/Elsa.Expressions/Helpers/ObjectConverter.cs:233`
- `src/modules/Elsa.Expressions/Helpers/ObjectConverter.cs:307`
- `src/modules/Elsa.Expressions/Helpers/ObjectConverter.cs:320`
- `src/modules/Elsa.Expressions/Helpers/ObjectConverter.cs:322`

## Newtonsoft Versus System.Text.Json

The dominant runtime serializer stack is `System.Text.Json`, not Newtonsoft. API, activity, payload, safe, workflow-state, HTTP JSON parsing, import/export, and EF payload paths all use `System.Text.Json`.

However, Newtonsoft is still part of persisted runtime compatibility because `JObject` and `JArray` are registered as type aliases and explicitly special-cased by `PolymorphicObjectConverter`. The converter writes Newtonsoft JSON values as `_island` strings because STJ cannot serialize those types directly.

Source refs:

- `src/modules/Elsa.Workflows.Core/ShellFeatures/WorkflowsFeature.cs:115`
- `src/modules/Elsa.Workflows.Core/ShellFeatures/WorkflowsFeature.cs:116`
- `src/modules/Elsa.Workflows.Core/Serialization/Converters/PolymorphicObjectConverter.cs:9`
- `src/modules/Elsa.Workflows.Core/Serialization/Converters/PolymorphicObjectConverter.cs:72`
- `src/modules/Elsa.Workflows.Core/Serialization/Converters/PolymorphicObjectConverter.cs:82`
- `src/modules/Elsa.Workflows.Core/Serialization/Converters/PolymorphicObjectConverter.cs:215`
- `src/modules/Elsa.Workflows.Core/Serialization/Converters/PolymorphicObjectConverter.cs:218`

The API pipeline uses `IApiSerializer` for FastEndpoints request/response serialization.

Source refs:

- `src/common/Elsa.Api.Common/FastEndpointConfigurators/ElsaFastEndpointsConfigurator.cs:24`
- `src/common/Elsa.Api.Common/FastEndpointConfigurators/ElsaFastEndpointsConfigurator.cs:31`
- `src/common/Elsa.Api.Common/FastEndpointConfigurators/ElsaFastEndpointsConfigurator.cs:33`
- `src/common/Elsa.Api.Common/FastEndpointConfigurators/ElsaFastEndpointsConfigurator.cs:41`
- `src/common/Elsa.Api.Common/FastEndpointConfigurators/ElsaFastEndpointsConfigurator.cs:43`
- `src/common/Elsa.Api.Common/Extensions/WebApplicationExtensions.cs:135`
- `src/common/Elsa.Api.Common/Extensions/WebApplicationExtensions.cs:157`
- `src/common/Elsa.Api.Common/Extensions/WebApplicationExtensions.cs:167`

## Compatibility Risks For Existing User Databases

1. Workflow definitions have at least three durable representations:
   - EF row scalar columns such as `StringData`, `OriginalSource`, materializer fields, and metadata.
   - EF shadow `Data` JSON containing options, variables, inputs, outputs, outcomes, and custom properties.
   - API/export JSON as `WorkflowDefinitionModel` plus `$schema` in exports.

2. Workflow instances persist compressed serialized `WorkflowState`. This captures dictionaries of arbitrary `object` values and depends on `JsonWorkflowStateSerializer`, polymorphic metadata, type aliases, reference handling, and variable conversion behavior.

3. Existing persisted values may contain `_type`, `_items`, `_island`, `$id`, `$ref`, and `$values` conventions from `PolymorphicObjectConverter` and STJ reference preservation. Removing support would likely break live/suspended workflow instances and logs.

4. Newtonsoft `JObject` and `JArray` compatibility is not incidental. Those types are registered aliases and serialized via island strings. A pure-STJ future model needs a migration or compatibility read path if existing runtime payloads can contain those aliases.

5. Variable default values and variable runtime values are different shapes. Defaults are formatted to strings in `VariableDefinition`; workflow-instance stored values are JSON nodes under workflow properties. Both are converted through `ObjectConverter`, but with different serializer options and failure behavior.

6. Workflow input persistence is selective and storage-driver dependent. Existing users may assume input values are durable only when storage-driver configuration makes them durable.

7. Activity output persistence has two meanings. Runtime dataflow reads in-memory output records; log/history stores selected serialized outputs. A migration must distinguish operational state from audit/history data.

8. `SafeSerializer` fallback objects intentionally drop value content when serialization fails. Existing logs may contain type-name-only placeholders that cannot be recovered as real values.

9. Some deserialization failures currently degrade to default state or skipped values, not hard failure. Tightening this behavior would be observable.

Source refs:

- `src/modules/Elsa.Persistence.EFCore/Modules/Management/WorkflowInstanceStore.cs:252`
- `src/modules/Elsa.Persistence.EFCore/Modules/Management/WorkflowInstanceStore.cs:254`
- `src/modules/Elsa.Persistence.EFCore/Modules/Management/WorkflowDefinitionStore.cs:187`
- `src/modules/Elsa.Persistence.EFCore/Modules/Management/WorkflowDefinitionStore.cs:189`
- `src/modules/Elsa.Workflows.Core/Services/VariablePersistenceManager.cs:56`
- `src/modules/Elsa.Workflows.Core/Services/VariablePersistenceManager.cs:63`
- `src/modules/Elsa.Workflows.Core/VariableStorageDrivers/WorkflowInstanceStorageDriver.cs:38`
- `src/modules/Elsa.Workflows.Core/VariableStorageDrivers/WorkflowInstanceStorageDriver.cs:40`

## Clarification Questions

1. Which compatibility target matters most: reading existing Elsa 3 workflow definitions, resuming existing Elsa 3 workflow instances, preserving execution logs, preserving bookmarks/queues, or supporting API import/export?

2. Are active/suspended Elsa 3 workflow instances expected to be migrated into Elsa 4, or can migration require draining/rerunning workflows before upgrade?

3. Should Elsa 4 treat `StringData` root activity JSON, `OriginalSource`, or `WorkflowDefinitionModel` export JSON as the primary migration input?

4. Are `JObject` and `JArray` values known to exist in customer workflow state, variables, bookmarks, or activity logs?

5. Should variable default values remain string-formatted, or can migration normalize them into typed JSON/enveloped values?

6. Should workflow input persistence remain opt-in via storage driver, or should workflow invocation input have explicit persistence policy independent of variable storage?

7. Are activity execution records/logs considered audit data only, or does any runtime behavior depend on reading persisted activity outputs from those logs?

8. Should conversion failures be strict migration errors, warnings with lossy placeholders, or preserve Elsa 3's default/skip behavior?

9. Which third-party extension types need stable type aliases for persisted values, and who owns those aliases?

10. Does Elsa 4 need to read Elsa 3 API/export files directly, or is an explicit migration command acceptable?

## Design-Option Areas For Brainstorming

These are areas to discuss, not recommendations.

1. Canonical persisted value envelope:
   - Typed JSON object with alias metadata.
   - Raw JSON value plus declared schema/type.
   - External payload reference.
   - Integrator-owned serializer contract.

2. Compatibility read layers:
   - Elsa 3 workflow definition reader.
   - Elsa 3 workflow instance/state reader.
   - Log/bookmark/queue compatibility readers.
   - One-shot migration tool versus runtime dual-read.

3. Runtime value persistence policy:
   - Persist nothing arbitrary by default.
   - Persist declared variables only.
   - Persist workflow I/O by explicit contract.
   - Persist activity outputs only when opted in or referenced.

4. Type identity and aliases:
   - Stable Elsa aliases only.
   - Extension-provided alias registry.
   - No CLR assembly-qualified names in new durable data.
   - Separate migration alias map for Elsa 3 names.

5. JSON DOM boundary:
   - Normalize Newtonsoft and STJ JSON DOMs into one representation.
   - Preserve both for compatibility reads only.
   - Treat JSON DOMs as payload values, not runtime CLR objects.

6. Failure behavior:
   - Strict failures for runtime state migration.
   - Best-effort import with warnings for authored definitions.
   - Type-name-only placeholders limited to logs.
   - Explicit non-persistable value errors at runtime.

7. Definition shape separation:
   - Authored document JSON.
   - API/read model JSON.
   - Runtime executable artifact.
   - Migration import shape.

8. Activity output semantics:
   - Ephemeral in-memory outputs.
   - Durable output declarations.
   - Output references to external payload storage.
   - Log-only output snapshots.

## Suggested Next Step

Use this report as the source-evidence packet for the topic 1 brainstorm session. The brainstorm should decide compatibility scope first; without that, serializer design discussions will mix authored workflow import, live instance migration, log/history preservation, and future runtime value policy into one problem.
