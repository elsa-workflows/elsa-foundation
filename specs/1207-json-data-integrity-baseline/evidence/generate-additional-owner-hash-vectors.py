"""Independent additional owner recipes. No production serializer or hasher is invoked.

Every pinned recipe freezes at the first stable release; subsequent changes need
an explicit identity/migration decision.
"""
from pathlib import Path
import base64
from datetime import datetime, timezone
import hashlib
import hmac
import importlib.util
import json
import re
import struct

spec = importlib.util.spec_from_file_location("canonical_fixture", Path(__file__).with_name("generate-variable-hash-vectors.py"))
helper = importlib.util.module_from_spec(spec)
spec.loader.exec_module(helper)


def canonical(value):
    def numbers(item):
        if isinstance(item, helper.Number) or isinstance(item, bool) or item is None or isinstance(item, str): return item
        if isinstance(item, (int, float)): return helper.load_json(json.dumps(item))
        if isinstance(item, list): return [numbers(value) for value in item]
        return {key: numbers(value) for key, value in item.items()}
    return helper.canonical(numbers(value))


def generate():
    rows = []
    variants = [
        ("base", '{"OwnerKey":"René","n":9007199254740993,"rows":[1,null]}'),
        ("spelling", ' { "rows": [1e0,null], "n":9007199254740993.0,"OwnerKey":"Ren\\u00e9" } '),
        ("missing", '{"OwnerKey":"René","n":9007199254740993}'),
        ("null", '{"OwnerKey":"René","n":9007199254740993,"rows":null}'),
        ("order", '{"OwnerKey":"René","n":9007199254740993,"rows":[null,1]}'),
        ("number", '{"OwnerKey":"René","n":9007199254740992,"rows":[1,null]}'),
    ]

    def add(name, assembly, owner, method, arguments, projection, prefix="", uppercase=False, frame=None, **extra):
        text = canonical(projection)
        digest = hashlib.sha256(frame if frame is not None else text.encode()).hexdigest()
        rows.append(dict(name=name, assembly=assembly, type=owner, method=method, arguments=arguments,
                         canonical=text, expected=prefix + (digest.upper() if uppercase else digest), **extra))

    core = "Elsa.Activities.Design.Core"
    api = "Elsa.Activities.Design.Api"
    ef = "Elsa.Activities.Design.Persistence.EntityFrameworkCore"
    for name, raw in variants:
        data = helper.load_json(raw)
        add("selection/" + name, api, "Elsa.Activities.Design.Api.Services.ActivityDataSelectionSnapshot", "Hash", [{"rawJson": raw}], data, "sha256:")
        add("fork-material/" + name, core, "Elsa.Activities.Design.Core.Services.ActivityForkMaterialFingerprint", "Compute", [{"rawJson": raw}], data, "sha256:")
        add("default/" + name, core, "Elsa.Activities.Design.Core.Services.ActivityVersionDiffer", "DefaultHash",
            [{"typedJson": '{"syntax":"Literal","value":' + raw + '}'}], dict(syntax="Literal", value=data), "sha256-")
        add("element/" + name, core, "Elsa.Activities.Design.Core.Services.ActivityVersionDiffer", "HashElement", [{"rawJson": raw}], data, "sha256-")
        add("ui/" + name, core, "Elsa.Activities.Design.Core.Services.ActivityVersionDiffer", "HashUi",
            [{"text": "json-editor"}, {"rawJson": raw}], dict(hint="json-editor", specifications=data), "sha256-")
        add("atomic-command/" + name, ef, "Elsa.Activities.Design.Persistence.EntityFrameworkCore.EfDesignAtomicCommand", "Fingerprint",
            [{"text": raw}], data, uppercase=True)
        add("human-review/" + name, "Elsa.Agent.Review", "Elsa.Agent.Review.Services.ReviewDecisionCodec", "HashContent", [{"rawJson": raw}], data)
        add("weaver-typed/" + name, "Elsa.Weaver", "Elsa.Weaver.Services.WeaverInvocationIntegrity", "Digest",
            [{"typedJson": '{"typeId":"form-é","schemaVersion":"1","data":' + raw + '}'}],
            dict(typeId="form-é", schemaVersion="1", data=data), genericType="Elsa.Weaver.Core.Models.WeaverPayload", genericAssembly="Elsa.Weaver.Core")
        inputs = '[{"nodeId":"node-B","data":' + raw + '},{"nodeId":"node-A","data":null}]'
        frame = b""
        for node, value in [("node-A", None), ("node-B", data)]:
            for part in [node, canonical(value)]:
                encoded = part.encode(); frame += struct.pack("<i", len(encoded)) + encoded
        add("layout/" + name, core, "Elsa.Activities.Design.Core.Services.ActivityLayoutHasher", "Compute", [{"typedJson": inputs}],
            [dict(nodeId="node-A", data=None), dict(nodeId="node-B", data=data)], "sha256-", frame=frame, frameHex=frame.hex())

    values = {"OwnerKey": dict(type="string", literal="René"), "Count": dict(type="integer", literal="1")}
    add("processing-group/ordered-fields", "Acme.Processing", "Acme.Processing.ProcessingDashboardSource", "GroupKey",
        [{"json": ["OwnerKey", "Count"]}, {"json": values}], [dict(key=key, type=values[key]["type"], literal=values[key]["literal"]) for key in ["OwnerKey", "Count"]])
    scalars = ["tenant-é", "reader", "Mixed"]
    add("management-scope/opaque-strings", api, "Elsa.Activities.Design.Api.Services.ActivityDefinitionManagementProjectionService", "Scope",
        [{"objectArray": scalars}], [dict(representation="clr", value=base64.b64encode(value.encode("utf-16-le", errors="surrogatepass")).decode()) for value in scalars])
    projection = dict(rebuildId="rebuild-é", sequence=3, asOf="2026-07-15T09:30:00+00:00", items=[])
    add("dependency-snapshot/owned-fields", ef, "Elsa.Activities.Design.Persistence.EntityFrameworkCore.Stores.EfActivityDesignStores", "Fingerprint",
        [{"json": projection}], projection, uppercase=True)
    for tenant in [None, "tenant-é"]:
        identity = dict(tenantId=tenant, persistencePartition="partition", workflowExecutionId="run", activityExecutionId="activity",
                        operationKey="operation", artifactId="artifact", artifactHash="artifact-hash", definitionVersionId="version", templateHash="template")
        add("agent-operation/" + ("missing-tenant" if tenant is None else "tenant"), "Elsa.Agent.Core", "Elsa.Agent.Core.Models.AgentTurnDocument",
            "CalculateOperationKey", [{"json": identity}], {key: identity[key] for key in ["tenantId", "persistencePartition", "workflowExecutionId", "operationKey"]})
    for method, arguments in [("HashElement", [{"json": None}]), ("HashUi", [{"json": None}, {"json": None}])]:
        rows.append(dict(name="absent/" + method, assembly=core, type="Elsa.Activities.Design.Core.Services.ActivityVersionDiffer",
                         method=method, arguments=arguments, expected=None))
    add("explicit-root-null/HashElement", core, "Elsa.Activities.Design.Core.Services.ActivityVersionDiffer", "HashElement",
        [{"rawJson": "null"}], None, "sha256-")
    add("explicit-root-null/HashUi", core, "Elsa.Activities.Design.Core.Services.ActivityVersionDiffer", "HashUi",
        [{"json": None}, {"rawJson": "null"}], dict(hint=None, specifications=None), "sha256-")

    def opaque(value):
        if isinstance(value, str): return base64.b64encode(value.encode("utf-16-le", errors="surrogatepass")).decode()
        if isinstance(value, list): return [opaque(item) for item in value]
        if isinstance(value, dict): return {key: opaque(item) for key, item in value.items()}
        return value

    decision_owners = [
        ("design", "Elsa.Decisions.Design", "Elsa.Decisions.Design.DecisionDesignService", {}),
        ("store", "Elsa.Decisions.Persistence.EntityFrameworkCore", "Elsa.Decisions.Persistence.EntityFrameworkCore.EfDecisionStore`1",
         dict(declaringType="Elsa.Decisions.Persistence.EntityFrameworkCore.DecisionsSqliteDbContext", declaringAssembly="Elsa.Decisions.Persistence.EntityFrameworkCore")),
    ]
    etl_store = "Elsa.Etl.Persistence.EntityFrameworkCore"
    for name, raw in variants:
        data = helper.load_json(raw)
        for owner_name, assembly, owner, extra in decision_owners:
            add(f"decision-{owner_name}/json-{name}", assembly, owner, "Fingerprint", [{"rawJson": raw}],
                dict(representation="json", value=data), **extra)
        add("etl-runtime-text/" + name, etl_store, "Elsa.Etl.Persistence.EntityFrameworkCore.Stores.EtlRuntimeRecordCodec", "HashText", [{"text": raw}], data)
        for side in ["invocation", "result"]:
            invocation, result = (data, dict(status="Completed")) if side == "invocation" else (dict(invocationId="call-é"), data)
            add(f"etl-result/{side}-{name}", "Elsa.Etl.Core", "Elsa.Etl.Core.Runtime.EtlRuntimeJson", "ResultEnvelopeHash",
                [{"text": raw if side == "invocation" else '{"invocationId":"call-é"}'}, {"text": raw if side == "result" else '{"status":"Completed"}'}],
                dict(purpose="etl-result-envelope-v1", invocation=invocation, result=result))
        for kind, number in [("Http", 1), ("Sql", 3), ("RabbitMq", 4), ("Csv", 0), ("Jsonl", 2)]:
            add(f"etl-route/{kind}-{name}", "Elsa.Etl.Runtime", "Elsa.Etl.Runtime.Execution.EtlConnectorOutputProcessor", "RouteHash",
                [{"json": number}, {"utf8": raw}], data, frame=raw.encode() if kind in ["Csv", "Jsonl"] else None)
        digest = hashlib.sha256(canonical(helper.load_json(variants[0][1])).encode()).hexdigest()
        rows.append(dict(name="etl-input-verifier/" + name, assembly="Elsa.Etl.Runtime", type="Elsa.Etl.Runtime.Execution.EtlConnectorInputBatchBuilder",
                         method="HasJsonHash", arguments=[dict(utf8=raw), dict(text=digest)], expected=name in ["base", "spelling"]))
    rows.append(dict(name="etl-input-verifier/malformed", assembly="Elsa.Etl.Runtime", type="Elsa.Etl.Runtime.Execution.EtlConnectorInputBatchBuilder",
                     method="HasJsonHash", arguments=[dict(utf8='{"x":'), dict(text=digest)], expected=False))
    for owner_name, assembly, owner, extra in decision_owners:
        lifecycle = dict(assetId="asset-é", expectedRevision="revision")
        add(f"decision-{owner_name}/clr-record", assembly, owner, "Fingerprint",
            [dict(clrType="Elsa.Decisions.Persistence.Core.DecisionAssetLifecycle", clrAssembly="Elsa.Decisions.Persistence.Core", json=lifecycle)],
            dict(representation="clr", value=opaque(lifecycle)), **extra)
        add(f"decision-{owner_name}/json-record-lookalike", assembly, owner, "Fingerprint", [dict(json=lifecycle)], dict(representation="json", value=lifecycle), **extra)
    _, assembly, owner, extra = decision_owners[1]
    for label, tenant, actor, operation in [("base", "tenant-é", "actor-é", "operation"), ("tenant-only", "other", "actor-é", "operation"),
                                          ("actor", "tenant-é", "other-actor", "operation"), ("operation", "tenant-é", "actor-é", "other-operation")]:
        add("decision-operation/" + label, assembly, owner, "OperationId", [dict(json=dict(tenantId=tenant, actorId=actor, operationId=operation))], [actor, operation], **extra)
    key = dict(definitionId="definition-é", sourceAlias="source", sourceHandle="handle", table="events", column="sequence")
    for changed in [None, *key]:
        value = dict(key)
        if changed: value[changed] += "|changed"
        add("etl-source-watermark/" + (changed or "base"), etl_store, "Elsa.Etl.Persistence.EntityFrameworkCore.Stores.EfEtlRuntimeStore", "SourceWatermarkKey",
            [dict(json=value)], dict(formatVersion=1, **opaque(value)))
    for tenant in [None, "tenant-é"]:
        scope = dict(tenantId=tenant, persistencePartition="partition-é")
        add("etl-native-run/" + ("none" if tenant is None else "tenant"), "Elsa.Etl.Workflows.Runtime", "Elsa.Etl.Workflows.Runtime.Services.EtlNativeBoundary", "RunId",
            [dict(json=scope), dict(text="workflow"), dict(text="activity")], opaque(dict(format="etl-native-run-v1", scope=scope, workflowId="workflow", activityId="activity")), "etl-")
    identity = dict(tenantId=None, persistencePartition="partition-é", workflowExecutionId="run", activityExecutionId="activity", operationKey="operation",
                    artifactId="artifact", artifactHash="hash", definitionVersionId="version", templateHash="template")
    deadline = "2026-07-15T09:30:00+02:00"
    operation_key = hashlib.sha256(canonical({key: identity[key] for key in ["tenantId", "persistencePartition", "workflowExecutionId", "operationKey"]}).encode()).hexdigest()
    document = dict(handle="a" * 64, key=operation_key, requestHash="c" * 64, identity=identity, protectedRequest="opaque", deadline=deadline,
                    status="Prepared", childInvocationId=None, fence=None, protectedResult=None, failureCode=None, maximumModelCalls=1,
                    modelRequests=None, responseObservation=None, conversation=None)
    for label in ["prepared", "retained-authorities"]:
        value = dict(document)
        if label == "retained-authorities":
            value.update(status="Accepted", childInvocationId="child", fence="fence", protectedRequest=None,
                         contentPurgedAt="2026-07-16T09:30:00+02:00", deliveryProtocol=1, deliveries=[dict(identity=identity, childInvocationId="child")],
                         applications=[dict(identity=identity, childInvocationId="child", sourceId="source", sourceFingerprint="d" * 64, appliedAt=deadline)],
                         activeDeliveryActivityId="activity", admissionGeneration=2, leaseExpiresAt=deadline)
        add("agent-retention/" + label, "Elsa.Agent.Core", "Elsa.Agent.Core.Models.AgentRetentionRules", "Snapshot", [dict(json=value)], value,
            validateWith=dict(assembly="Elsa.Agent.Core", type="Elsa.Agent.Core.Models.AgentTurnDocumentRules", method="Validate"))
    for kind, date_property, child in [("Cancelled", "cancelledAt", None), ("Applied", "appliedAt", "child")]:
        value = dict(version=1, identity=identity, childInvocationId=child, **{date_property: deadline})
        if kind == "Applied": value["handle"] = "handle-é"
        prefix = "agent-cancel:" if kind == "Cancelled" else "agent-applied:"
        digest = prefix + hashlib.sha256(canonical(value).encode()).hexdigest()
        add("agent-intent/" + kind, "Elsa.Agent", "Elsa.Agent.Services.AgentSettlementBoundary", "Intent", [dict(json=value)], value, prefix,
            parameterType="Elsa.Agent.Services.Agent" + kind + "Payload", returnProperty="IntentId",
            returnMembers=dict(IdempotencyKey=digest, WorkflowExecutionId="run", ActivityExecutionId="activity", RecordedAt=deadline,
                               Kind="elsa.agent.cancel/1" if kind == "Cancelled" else "elsa.agent.applied/1", Payload=value))
    following = [
        ("authoring-schema", "Elsa.Workflows.Design.Api", "Elsa.Workflows.Design.Api.Services.AuthoringSchemaExporter", "ComputeFingerprint", "sha256:", "rawJson", {}),
        ("authoring-json", "Elsa.Workflows.Design.Api", "Elsa.Workflows.Design.Api.Services.WorkflowAuthoringRevision", "JsonDigest", "", "rawJson", {}),
        ("draft-state", "Elsa.Workflows.Design.Persistence.Core", "Elsa.Workflows.Design.Persistence.Core.Models.WorkflowDraftStateHash", "Compute", "", "text", {}),
        ("git-file", "Elsa.Workflows.Design.Reconciliation.Git", "Elsa.Workflows.Design.Reconciliation.Git.Services.GitCanonicalJson", "HashFile", "", "text", {}),
        ("git-compact", "Elsa.Workflows.Design.Reconciliation.Git", "Elsa.Workflows.Design.Reconciliation.Git.Services.GitCanonicalJson", "Sha256Hex", "", "text", {}),
        ("archive-json", "Elsa.Workflows.Publishing.Releases.Persistence.EntityFrameworkCore", "Elsa.Workflows.Publishing.Releases.Persistence.EntityFrameworkCore.EfReleaseArchiveStore`1", "JsonDigest", "", "utf8",
         dict(declaringType="Elsa.Decisions.Persistence.EntityFrameworkCore.DecisionsSqliteDbContext", declaringAssembly="Elsa.Decisions.Persistence.EntityFrameworkCore")),
    ]
    for label, assembly, owner, method, prefix, adapter, extra in following:
        for name, raw in [*variants, ("root-null", "null")]:
            add(f"{label}/{name}", assembly, owner, method, [{adapter: raw}], helper.load_json(raw), prefix, **extra)
        if adapter != "rawJson":
            rows.append(dict(name=label + "/malformed", assembly=assembly, type=owner, method=method, arguments=[{adapter: '{"x":'}],
                             expectedException="Elsa.Workflows.Publishing.Releases.Persistence.Core.ReleaseArchiveStoreException" if label == "archive-json" else "System.Text.Json.JsonReaderException",
                             **(dict(exceptionCode="archive_stored_content_invalid") if label == "archive-json" else {}), **extra))
    for raw in [None, ""]:
        add("draft-state/absent-" + ("null" if raw is None else "empty"), "Elsa.Workflows.Design.Persistence.Core", "Elsa.Workflows.Design.Persistence.Core.Models.WorkflowDraftStateHash", "Compute",
            [dict(json=raw)], None, frame=b"")
    archive = following[-1]
    for name, raw in variants:
        for kind in [1, 2]:
            add(f"archive-content/{kind}-{name}", archive[1], archive[2], "ContentDigest", [dict(json=kind), dict(utf8=raw)],
                helper.load_json(raw), frame=raw.encode() if kind == 2 else None, **archive[-1])
    rows.append(dict(name="archive-content/invalid-kind", assembly=archive[1], type=archive[2], method="ContentDigest",
                     arguments=[dict(json=3), dict(utf8="null")], expectedException="Elsa.Workflows.Publishing.Releases.Persistence.Core.ReleaseArchiveStoreException",
                     exceptionCode="archive_stored_content_invalid", **archive[-1]))
    rows.append(dict(name="draft-state/whitespace-refused", assembly="Elsa.Workflows.Design.Persistence.Core", type="Elsa.Workflows.Design.Persistence.Core.Models.WorkflowDraftStateHash",
                     method="Compute", arguments=[dict(text=" ")], expectedException="System.Text.Json.JsonReaderException"))
    for label, assembly, owner, method in [
        ("authoring-parts", "Elsa.Workflows.Design.Api", "Elsa.Workflows.Design.Api.Services.WorkflowAuthoringRevision", "Digest"),
        ("user-task-parts", "Elsa.Workflows.Runtime", "Elsa.Workflows.Runtime.UserTasks.UserTaskRules", "Id"),
    ]:
        for index, parts in enumerate([["tenant-é", "a|b", "c"], ["tenant-é", "a", "b|c"], ["c", "a|b", "tenant-é"], ["tenant-é", "", "c"], []]):
            add(f"{label}/{index}", assembly, owner, method, [dict(json=parts)], parts)
    values = {"OwnerKey": dict(type="string", literal="René", parameter="ignored"), "Count": dict(type="integer", literal="1", parameter=None)}
    for label, keys, members in [("base", ["OwnerKey", "Count"], values), ("order", ["Count", "OwnerKey"], values),
                                 ("parameter-only", ["OwnerKey", "Count"], {**values, "OwnerKey": {**values["OwnerKey"], "parameter": "other"}})]:
        add("workflow-dashboard-group/" + label, "Elsa.Workflows.Dashboard.Queries", "Elsa.Workflows.Dashboard.Queries.WorkflowDashboardQuerySource", "GroupKey",
            [dict(json=keys), dict(json=members)], [dict(key=key, type=members[key]["type"], literal=members[key]["literal"]) for key in keys])
    for label, values in [("strings", ["tenant-é", "reader", "Mixed"]), ("null", ["tenant-é", None]), ("scalars", [True, 3]),
                          ("carried", [{"OwnerKey": "René", "rows": [1, None]}])]:
        projection = [None if value is None else dict(representation="json", value=value) if isinstance(value, dict)
                      else dict(representation="clr", value=opaque(value)) for value in values]
        add("identity-scope/" + label, "Elsa.Foundation.Identity.Persistence.EntityFrameworkCore", "Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.Stores.EfIdentityStoreSupport", "JsonFingerprint",
            [dict(objectArray=values)], projection)
    for kind in ["Draft", "Version"]:
        labels = [*variants, ("root-null", "null"), ("absent", None), ("idempotency-only", variants[0][1]), ("insertion-order", variants[0][1])]
        for name, raw in labels:
            data = helper.load_json(raw) if raw is not None else None
            inputs = dict(aInput=dict(constructorArguments=dict(State=dict(text="Present"), Value=dict(rawJson=raw) if raw is not None else dict(json=None))),
                          ZInput=dict(constructorArguments=dict(State=dict(text="Present"), Value=dict(rawJson="1"))))
            if name == "insertion-order": inputs = dict(reversed(list(inputs.items())))
            parts = dict(IdempotencyKey=dict(text="other" if name == "idempotency-only" else "idempotency-only"),
                         Inputs=dict(dictionaryEntries=inputs), CorrelationId=dict(text="correlation-é"))
            projected_inputs = [dict(name="ZInput", state="Present", hasValue=True, value=helper.load_json("1")),
                                dict(name="aInput", state="Present", hasValue=raw is not None, value=data)]
            if kind == "Draft":
                parts.update(DraftId=dict(text="draft-é"), ExpectedRevision=dict(json=3))
                projected_inputs = [{**item, "name": opaque(item["name"]), "state": opaque(item["state"])} for item in projected_inputs]
                projection = dict(draftId=opaque("draft-é"), expectedRevision=3, correlationId=opaque("correlation-é"), inputs=projected_inputs)
            else:
                parts.update(VersionId=dict(text="version-é"))
                projection = dict(versionId="version-é", correlationId="correlation-é", inputs=projected_inputs)
            add(f"activity-{kind.lower()}-request/{name}", "Elsa.Workflows.Publishing.Api", "Elsa.Workflows.Publishing.Api.Services.ActivityDraftTestRunService", "RequestFingerprint",
                [dict(constructorArguments=parts)], projection, parameterType="Elsa.Workflows.Publishing.Api.Models.StartActivity" + kind + "TestRun")
    for name, raw in [*variants, ("root-null", "null")]:
        data = helper.load_json(raw)
        digest = hashlib.sha256(canonical(data).encode()).hexdigest()
        add("activity-json-comparer/" + name, ef, "Elsa.Activities.Design.Persistence.EntityFrameworkCore.ActivitiesDesignDbContext", "JsonHashCode",
            [dict(rawJson=raw), dict(baselineOptions=True)], data, genericObject=True, expectedOrdinalDigest=digest)
    rows.append(dict(name="activity-json-comparer/clr-null", assembly=ef, type="Elsa.Activities.Design.Persistence.EntityFrameworkCore.ActivitiesDesignDbContext", method="JsonHashCode",
                     arguments=[dict(clrNull=True), dict(baselineOptions=True)], genericObject=True, expected=0))
    runtime_store = "Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore"
    schedule = dict(scheduleId="schedule-B", artifactId="artifact-é", executableNodeId="node", stimulusType="timer", stimulusHash="stimulus",
                    kind=0, expression="PT5M", nextOccurrence="2026-07-15T09:30:00+02:00", createdAt="2026-01-15T08:00:00+00:00",
                    activationId=None, slotId=None, isActive=True)

    def schedule_projection(value, immutable=False):
        result = dict(value, isActive=False)
        for key in ["scheduleId", "artifactId", "executableNodeId", "stimulusType", "stimulusHash", "expression", "activationId", "slotId"]:
            result[key] = opaque(result[key])
        result['kind'] = ["Interval", "Cron"][value['kind']]
        if immutable: result['nextOccurrence'] = "0001-01-01T00:00:00+00:00"
        return result

    for label, changes in [("base", {}), ("active-only", dict(isActive=False)), ("cursor-only", dict(nextOccurrence="2026-07-15T10:30:00+02:00")),
                           ("expression", dict(expression="PT10M")), ("created", dict(createdAt="2026-01-15T09:00:00+00:00")), ("identity", dict(scheduleId="other"))]:
        value = dict(schedule, **changes)
        add("recurring-immutable/" + label, runtime_store, "Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores.EfRecurringTriggerScheduleStore", "ImmutableFingerprint",
            [dict(json=value)], schedule_projection(value, True), uppercase=True)
        members = [value, dict(schedule, scheduleId="schedule-A")]
        if label == "active-only": members = list(reversed(members))
        add("recurring-projection/" + label, runtime_store, "Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores.EfRecurringTriggerScheduleStore", "ProjectionFingerprint",
            [dict(json=members)], [schedule_projection(item) for item in sorted(members, key=lambda item: item['scheduleId'])], uppercase=True)
    binding = dict(triggerBindingId="binding-B", artifactId="artifact-é", definitionId="definition", artifactVersion="1.0.0", artifactHash="artifact-hash", executableNodeId="node",
                   stimulusType="timer", stimulusHash="stimulus", correlationScope=None, metadata={"OwnerKey": "René"}, createdAt="2026-01-15T08:00:00+00:00",
                   activationId=None, slotId=None, cardinality=1, isActive=True)

    def binding_projection(value):
        result = dict(value, isActive=False)
        for key in ["triggerBindingId", "artifactId", "definitionId", "artifactVersion", "artifactHash", "executableNodeId", "stimulusType", "stimulusHash", "correlationScope", "activationId", "slotId"]:
            result[key] = opaque(result[key])
        result['metadata'] = {opaque(key): opaque(item) for key, item in value['metadata'].items()}
        result['cardinality'] = ["Exclusive", "FanOut"][value['cardinality']]
        return result

    for label, changes in [("base", {}), ("active-only", dict(isActive=False)), ("metadata-case", dict(metadata={"ownerKey": "René"})),
                           ("metadata-value", dict(metadata={"OwnerKey": "other"})), ("artifact-hash", dict(artifactHash="other")), ("cardinality", dict(cardinality=0)),
                           ("slot", dict(slotId="slot")), ("empty-correlation", dict(correlationScope=""))]:
        members = [dict(binding, **changes), dict(binding, triggerBindingId="binding-A")]
        if label == "active-only": members = list(reversed(members))
        add("trigger-projection/" + label, runtime_store, "Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores.EfWorkflowTriggerBindingStore", "Fingerprint",
            [dict(json=members)], [binding_projection(item) for item in sorted(members, key=lambda item: item['triggerBindingId'])], uppercase=True)
    for label, tenant, partition, workflow, activity in [
        ("base", None, "partition-é", "workflow|a", "activity|b"),
        ("tenant", "tenant-é", "partition-é", "workflow|a", "activity|b"),
        ("partition", None, "partition-other", "workflow|a", "activity|b"),
        ("workflow", None, "partition-é", "workflow", "a|activity|b"),
        ("activity", None, "partition-é", "workflow|a", "activity"),
    ]:
        scope = dict(tenantId=tenant, persistencePartition=partition)
        add("rules-run/" + label, "Elsa.Rules.Workflows.Runtime", "Elsa.Rules.Workflows.Runtime.Services.RulesNativeBoundary", "RunId",
            [dict(json=scope), dict(text=workflow), dict(text=activity)],
            dict(format="rules-native-run-v1", scope=scope, workflowId=workflow, activityId=activity), "rules-")

    for label, changed in [("base", {}), ("run", dict(runId="run-other")), ("source-handle", dict(source="source-other")),
                           ("lookup-handle", dict(lookup="lookup-other")), ("pipeline", dict(pipeline="hash-other")),
                           ("ignored-metadata", dict(alias="other", revision="other", partition="other"))]:
        value = {**dict(runId="run-é", source="source-handle", lookup="lookup-handle", pipeline="pipeline-hash", alias="source", revision="revision", partition="partition-é"), **changed}
        scope = dict(tenantId=None, persistencePartition=value['partition'])
        snapshot = dict(scope=scope, runId=value['runId'],
                        sourcePin=dict(scope=scope, alias=value['alias'], role=0, revision=value['revision'], handle=value['source'], expectedSha256=None),
                        lookupPin=dict(scope=scope, alias="lookup", role=0, revision="revision", handle=value['lookup'], expectedSha256=None),
                        sourceChunk=None, lookupChunk=None)
        add("etl-source-index/" + label, "Elsa.Etl.Runtime", "Elsa.Etl.Runtime.Preparation.EtlFilePreparer", "SourceIndexIdentity",
            [dict(json=snapshot), dict(text=value['pipeline'])],
            dict(formatVersion=1, sourceIndexFormatVersion=1, runId=value['runId'], pipelineHash=value['pipeline'], sourcePinHandle=value['source'], lookupPinHandle=value['lookup']))

    def ticks(value):
        match = re.fullmatch(r'(.*T\d{2}:\d{2}:\d{2})(?:\.(\d{1,7}))?(Z|[+-]\d{2}:\d{2})', value)
        date = datetime.fromisoformat(match[1] + match[3].replace('Z', '+00:00')).astimezone(timezone.utc)
        return ((date.toordinal() - 1) * 86400 + date.hour * 3600 + date.minute * 60 + date.second) * 10_000_000 + int((match[2] or '').ljust(7, '0'))

    def authority_key(system, root, metadata):
        def frame(value):
            data = value.encode('utf-16-le')
            return struct.pack('>i', len(data)) + data
        data = frame('workflow-execution-authority/v1') + frame(system) + frame(root) + struct.pack('>i', len(metadata))
        for key, value in sorted(metadata.items()): data += frame(key) + frame(value)
        return hashlib.sha256(data).hexdigest()

    authority = dict(tenantPartition="partition-é", systemIdentity="system-é", rootInitiator="operator-é", authorityMetadata={"ZOwner": "René", "aOwner": "a|b"})
    selector = dict(definitionId="definition-é", status=3, runKind=2, from_="2026-07-15T09:30:00+02:00", to="2026-07-16T09:30:00+02:00",
                    correlationId="correlation-é", workflowExecutionId=None, artifactId=None, matchAllAuthorized=False)
    selector['from'] = selector.pop('from_')
    capture_cases = [("base", {}, {}), ("metadata-order", dict(authorityMetadata=dict(reversed(list(authority['authorityMetadata'].items())))), {}),
                     ("metadata-value", dict(authorityMetadata={"ZOwner": "other", "aOwner": "a|b"}), {}),
                     ("one-tick", {}, {"from": "2026-07-15T09:30:00.0000001+02:00"}),
                     ("same-instant-offset", {}, {"from": "2026-07-15T07:30:00+00:00"}),
                     ("status", {}, dict(status=4)), ("run-kind", {}, dict(runKind=1)),
                     ("null-dates", {}, {"from": None, "to": None}), ("page-only", dict(pageSize=37, cursor="ignored-cursor"), {})]
    for label, authority_changes, selector_changes in capture_cases:
        a = {**authority, **authority_changes}
        s = {**selector, **selector_changes}
        query = {**a, 'selector': s, 'pageSize': a.pop('pageSize', 10), 'cursor': a.pop('cursor', None)}
        common = dict(tenantPartition=a['tenantPartition'], systemIdentity=a['systemIdentity'], rootInitiator=a['rootInitiator'],
                      authorityPartitionKey=authority_key(a['systemIdentity'], a['rootInitiator'], a['authorityMetadata']),
                      definitionId=s['definitionId'], status=None, runKind=None, correlationId=s['correlationId'], workflowExecutionId=s['workflowExecutionId'],
                      artifactId=s['artifactId'], matchAllAuthorized=s['matchAllAuthorized'])
        common = opaque(common)
        common['status'] = ['Pending', 'Running', 'Suspended', 'Completed', 'Faulted', 'Cancelled'][s['status']] if s['status'] is not None else None
        common['runKind'] = ['Unknown', 'TestRun', 'PublishedRun', 'BackgroundWeaverRun'][s['runKind']] if s['runKind'] is not None else None
        metadata = [opaque(dict(key=key, value=value)) for key, value in sorted(a['authorityMetadata'].items())]
        for memory in [False, True]:
            projected = {**common, ('authorityMetadata' if memory else 'metadata'): metadata,
                         ('fromTicks' if memory else 'from'): ticks(s['from']) if s['from'] else None,
                         ('toTicks' if memory else 'to'): ticks(s['to']) if s['to'] else None}
            add(('memory' if memory else 'ef') + '-alteration-capture/' + label,
                'Elsa.Workflows.Runtime' if memory else runtime_store,
                'Elsa.Workflows.Runtime.Services.Executions.InMemoryWorkflowExecutionStateStore' if memory else 'Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores.EfWorkflowExecutionStateStore',
                'AlterationCaptureScope' if memory else 'CaptureScope', [dict(json=query)], projected, uppercase=True)

    for branch in ['ids', 'query']:
        for label, raw in variants:
            data = helper.load_json(raw)
            auth = dict(tenantPartition=authority['tenantPartition'], systemIdentity=authority['systemIdentity'], rootInitiator=authority['rootInitiator'], metadata=authority['authorityMetadata'])
            target = dict(executionIds=['z-execution', 'a-execution', 'z-execution'], query=None) if branch == 'ids' else dict(executionIds=None, query=selector)
            submission = dict(constructorArguments=dict(authorityScope=dict(json=auth), target=dict(json=target),
                alterations=dict(arrayArguments=[dict(constructorArguments=dict(kind=dict(text='replace-é'), schemaVersion=dict(json=1), payload=dict(rawJson=raw)))])))
            projected_authority = {**opaque({key: value for key, value in auth.items() if key != 'metadata'}),
                                   'metadata': {opaque(key): opaque(value) for key, value in auth['metadata'].items()}}
            if branch == 'ids': projected_target = opaque(dict(executionIds=['a-execution', 'z-execution']))
            else:
                normalized = {**selector}
                normalized['from'] = '2026-07-15T07:30:00.0000000+00:00'
                normalized['to'] = '2026-07-16T07:30:00.0000000+00:00'
                normalized['status'] = 'Completed'; normalized['runKind'] = 'PublishedRun'
                projected_target = opaque(dict(query=normalized))
            projected = dict(authority=projected_authority, target=projected_target, alterations=[dict(kind=opaque('replace-é'), schemaVersion=1, payload=data)])
            add('alteration-request/' + branch + '-' + label, 'Elsa.Workflows.Runtime',
                'Elsa.Workflows.Runtime.Services.Alterations.WorkflowAlterationRequestCanonicalizer', 'Hash',
                [submission], projected, terminalMethod='HashRequest')
    # Source-reviewed typed projections; no production serializer contributes expected bytes.
    version = dict(id="version-é", definitionId="omitted-definition", version="1.2.3", hash=None,
        providerKey="provider-é", providerSchemaVersion="1.0.0", consumerKey="consumer-é", consumerSchemaVersion="2.0.0",
        sourceKind="ClrDiscovery", sourceId="source-é", executionType=0)
    for label, raw in variants:
        value = dict(version, descriptorPayload=helper.load_json(raw))
        typed = dict(version, descriptorPayload=None)
        projection = opaque({key: value[key] for key in ["id", "version", "hash", "providerKey", "providerSchemaVersion", "consumerKey", "consumerSchemaVersion", "sourceKind", "sourceId"]})
        projection.update(descriptorPayload=value['descriptorPayload'], projectionKey=opaque('projection-é'))
        add('serialized-shape-revision/' + label, 'Elsa.Activities.Design.Reconciliation.Clr',
            'Elsa.Activities.Design.Reconciliation.Clr.Services.ClrActivitySerializedShapeSource', 'ResultRevision',
            [dict(clrType='Elsa.Activities.Design.Persistence.Core.Entities.ActivityDefinitionVersion', clrAssembly='Elsa.Activities.Design.Persistence.Core', value=dict(json=typed), propertyArguments={'DescriptorPayload':dict(rawJson=raw)}), dict(text='projection-é')], projection, 'sha256:')
    for label, changes in [('selected-id', dict(id='other')), ('selected-hash', dict(hash='other')), ('selected-version', dict(version='2.0.0')),
                           ('selected-provider', dict(providerKey='other')), ('selected-consumer', dict(consumerSchemaVersion='other')),
                           ('ignored-definition', dict(definitionId='other'))]:
        value = dict(version, descriptorPayload=None, **changes)
        projection = opaque({key: value[key] for key in ['id','version','hash','providerKey','providerSchemaVersion','consumerKey','consumerSchemaVersion','sourceKind','sourceId']})
        projection.update(descriptorPayload=None, projectionKey=opaque('projection-é'))
        add('serialized-shape-revision/' + label, 'Elsa.Activities.Design.Reconciliation.Clr',
            'Elsa.Activities.Design.Reconciliation.Clr.Services.ClrActivitySerializedShapeSource', 'ResultRevision',
            [dict(clrType='Elsa.Activities.Design.Persistence.Core.Entities.ActivityDefinitionVersion', clrAssembly='Elsa.Activities.Design.Persistence.Core', json=value), dict(text='projection-é')], projection, 'sha256:')
    for unit in [0xD800, 0xD801]:
        value = dict(version, descriptorPayload=None)
        selected = dict(value, id=chr(unit))
        projection = opaque({key: selected[key] for key in ['id','version','hash','providerKey','providerSchemaVersion','consumerKey','consumerSchemaVersion','sourceKind','sourceId']})
        projection.update(descriptorPayload=None, projectionKey=opaque('projection-é'))
        add('serialized-shape-revision/utf16-' + str(unit), 'Elsa.Activities.Design.Reconciliation.Clr',
            'Elsa.Activities.Design.Reconciliation.Clr.Services.ClrActivitySerializedShapeSource', 'ResultRevision',
            [dict(clrType='Elsa.Activities.Design.Persistence.Core.Entities.ActivityDefinitionVersion', clrAssembly='Elsa.Activities.Design.Persistence.Core', value=dict(json=value), propertyArguments={'Id':dict(utf16=[unit])}), dict(text='projection-é')], projection, 'sha256:')
    for key in ['Fixed Unicode Revision Key é雪', 'Rotated Unicode Revision Key é雪']:
        instance = dict(constructorArguments=dict(environment=dict(clrNull=True), shellSettings=dict(clrNull=True),
            options=dict(optionsValue=dict(json=dict(revisionKey=key))), hostContext=dict(clrNull=True)))
        for label, raw in [*variants, ('root-null', 'null')]:
            text = helper.canonical(helper.load_json(raw))
            rows.append(dict(name='feature-revision/' + ('rotated-' if key.startswith('Rotated') else '') + label,
                assembly='Elsa.Modularity.Api', type='Elsa.Modularity.Api.Services.JsonShellFeatureConfigurationStore', method='CreateRevision',
                arguments=[dict(rawJson=raw)], instance=instance, canonical=text,
                expected=hmac.new(key.encode('utf-8'), text.encode('utf-8'), hashlib.sha256).hexdigest()))
    pin = dict(artifactId='artifact-é', definitionId='definition-é', definitionVersionId='version-é', artifactVersion='1.0.0', artifactHash='hash-é')
    for label, changed, completed in [('base', {}, '2026-07-15T09:30:00+02:00'), ('offset', {}, '2026-07-15T07:30:00+00:00'),
        ('one-tick', {}, '2026-07-15T07:30:00.0000001+00:00'), ('tenant', dict(tenantId='tenant-é'), '2026-07-15T09:30:00+02:00'),
        ('status', dict(status=4), '2026-07-15T09:30:00+02:00'), ('ignored-completed', dict(completedAt='2025-01-01T00:00:00Z'), '2026-07-15T09:30:00+02:00')]:
        parent = dict(workflowExecutionId='run-é', pinnedExecutable=pin, status=5, subStatus=None, createdAt='2026-01-01T00:00:00Z',
            startedAt=None, updatedAt=None, completedAt=None, correlationId=None, parentWorkflowExecutionId=None, tenantId=None, systemMetadata={})
        parent.update(changed)
        projected = dict(version=1, workflowExecutionId=parent['workflowExecutionId'], partition='partition-é', tenantId=parent['tenantId'], **pin,
                         status=['Pending','Running','Suspended','Completed','Faulted','Cancelled'][parent['status']], completedAtUtcTicks=ticks(completed))
        add('rules-parent-terminal/' + label, 'Elsa.Rules.Workflows.Runtime', 'Elsa.Rules.Workflows.Runtime.Services.RulesNativeDeliveryInspector',
            'ParentTerminalIdentity', [dict(json=parent), dict(text='partition-é'), dict(json=completed)], projected, 'rules-parent-terminal-v1:')
    reservation = dict(reservationId='reservation-é', scope=dict(persistence=dict(value='partition-é'), tenantId=None), stimulusType='omitted-type', stimulusHash='stimulus-é',
        ownerKind='omitted-owner-kind', ownerId='owner-é', ownerRevision=9007199254740993, configurationRevision=7, metadata={'ZOwner':'René','aOwner':'a|b'})
    for label, changes in [('base',{}), ('metadata-order',dict(metadata={'aOwner':'a|b','ZOwner':'René'})),
        ('metadata-case',dict(metadata={'zOwner':'René','aOwner':'a|b'})), ('owner-revision',dict(ownerRevision=9007199254740992)),
        ('configuration-revision',dict(configurationRevision=8)), ('ignored-stimulus',dict(stimulusType='other')), ('ignored-kind',dict(ownerKind='other'))]:
        value = dict(reservation, **changes)
        projected = {key:value[key] for key in ['reservationId','ownerId','ownerRevision','configurationRevision','scope','stimulusHash']}
        projected['metadata'] = [dict(key=key,value=item) for key,item in sorted(value['metadata'].items())]
        add('http-reservation/' + label, 'Elsa.Workflows.Runtime.Http', 'Elsa.Workflows.Runtime.Http.Services.HttpEndpointRoutesResolver',
            'ReservationIdentity', [dict(json=value)], projected, 'standalone-http:', uppercase=True)
    for label, value in [('absent',{}), ('explicit-null',dict(variables=None,rootActivity=None,inputs=None,outputs=None,strategyOptions=None)),
                         ('empty',dict(variables=[],rootActivity=None,inputs=[],outputs=[],strategyOptions=None))]:
        add('authoring-snapshot/' + label, 'Elsa.Workflows.Design.Api', 'Elsa.Workflows.Design.Api.Services.WorkflowAuthoringRevision', 'Snapshot',
            [dict(json=value)], dict(variables=[],rootActivity=None,inputs=[],outputs=[],strategyOptions=None), returnField='Item2')
    return rows


if __name__ == "__main__":
    Path(__file__).with_name("additional-owner-hash-vectors.json").write_text(json.dumps(generate(), ensure_ascii=True, indent=2) + "\n", encoding="utf-8")
