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

    # Presentation is a producer of layout material, not merely a raw-layout alias. The expected
    # normalized records below are literal owner preimages, independently declared from its input.
    presentation_base = [dict(nodeId="node-A", displayName="  Approve order  ", description=" \nReview it.\n "),
                         dict(nodeId="node-B", displayName=None, description="Second-é\nline")]
    presentation_normal = [dict(nodeId="node-A", displayName="Approve order", description="Review it."),
                           dict(nodeId="node-B", displayName=None, description="Second-é\nline")]
    presentation_cases = [
        ("base", presentation_base, presentation_normal),
        ("normalized", presentation_normal, presentation_normal),
        ("reordered", list(reversed(presentation_base)), presentation_normal),
        ("display-name", [dict(nodeId="node-A", displayName="Changed", description="Review it."), presentation_normal[1]],
                         [dict(nodeId="node-A", displayName="Changed", description="Review it."), presentation_normal[1]]),
        ("description", [presentation_normal[0], dict(nodeId="node-B", displayName=None, description="Changed")],
                        [presentation_normal[0], dict(nodeId="node-B", displayName=None, description="Changed")]),
        ("opaque-node", [dict(nodeId=" node-A ", displayName="Approve order", description="Review it."), presentation_normal[1]],
                        [dict(nodeId=" node-A ", displayName="Approve order", description="Review it."), presentation_normal[1]]),
        ("unicode", [dict(nodeId="node-🚀", displayName="René", description="Line 🚀")],
                    [dict(nodeId="node-🚀", displayName="René", description="Line 🚀")]),
        ("null-collection", None, []),
        ("empty-collection", [], []),
        ("empty-record", [dict(nodeId="empty", displayName="  ", description="\n\t")], []),
    ]
    for label, inputs, normalized in presentation_cases:
        frame = b""
        material = []
        for record in normalized:
            data = dict(displayName=record["displayName"], description=record["description"])
            material.append(dict(nodeId=record["nodeId"], data=data))
            for part in [record["nodeId"], canonical(data)]:
                encoded = part.encode("utf-8")
                frame += struct.pack("<i", len(encoded)) + encoded
        add("presentation/" + label, core, "Elsa.Activities.Design.Core.Services.ActivityNodePresentationHasher", "Compute",
            [dict(clrNull=True) if inputs is None else dict(json=inputs)], material, "sha256-", frame=frame, frameHex=frame.hex())
    for label, inputs, exception in [
        ("duplicate-node", [presentation_normal[0], presentation_normal[0]], "System.ArgumentException"),
        ("null-record", [None], "System.ArgumentNullException"),
    ]:
        rows.append(dict(name="presentation/" + label, assembly=core,
            type="Elsa.Activities.Design.Core.Services.ActivityNodePresentationHasher", method="Compute",
            arguments=[dict(json=inputs)], expected=None, expectedException=exception))

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
    # Byte-facing verifiers must recognize canonical JSON while retaining physical byte-length checks.
    base_digest = hashlib.sha256(helper.canonical(helper.load_json(variants[0][1])).encode()).hexdigest()
    release = 'Elsa.Workflows.Publishing.Releases.Runtime'
    for label, raw in variants:
        actual_digest = hashlib.sha256(helper.canonical(helper.load_json(raw)).encode()).hexdigest()
        rows.append(dict(name='http-page-body-verify/' + label, assembly='Elsa.Etl.Runtime', type='Elsa.Etl.Runtime.Http.EtlHttpPageCache',
            method='FixedTimeEqualsSha256', arguments=[dict(utf8=raw),dict(text=base_digest)], expected=actual_digest == base_digest))
        page = dict(descriptor=dict(pageSequence=7, requestHash='request', body=dict(key='body',contentHash=base_digest,byteLength=len(variants[0][1].encode())),recordCount=1,nextContinuation=None),
            decodedPage=None,provenance=dict(attemptId='attempt',fence=3,responseReceivedAtUtc='2026-01-15T08:00:00+00:00'),rawBody=base64.b64encode(variants[0][1].encode()).decode())
        rows.append(dict(name='http-page-same-body/' + label, assembly='Elsa.Etl.Runtime', type='Elsa.Etl.Runtime.Http.EtlHttpPageCache',
            method='SameBody', arguments=[dict(json=page),dict(utf8=raw)], expected=actual_digest == base_digest))
        descriptor = dict(kind='test',identity='identity-é',codec='test-json',codecVersion=1,payloadDigest='sha256:' + base_digest,byteLength=len(raw.encode()))
        rows.append(dict(name='release-matches-bytes/' + label,assembly=release,type=release + '.RuntimeCapturedContentCache',method='MatchesBytes',
            arguments=[dict(json=descriptor),dict(utf8=raw)],expected=actual_digest == base_digest))
        payload = ('{"formatVersion":1,"content":' + raw.strip(' \t\r\n') + '}').encode()
        digest = hashlib.sha256(helper.canonical(dict(formatVersion=helper.Number('1'),content=helper.load_json(raw))).encode()).hexdigest()
        add('release-runtime-encode/' + label,release,release + '.ReleaseRuntimeArtifactCodec','Encode',
            [dict(rawJson=raw),dict(text='test'),dict(text='identity-é'),dict(text='test-json'),dict(json=None),dict(json=None),dict(json=1)],
            dict(formatVersion=1,content=helper.load_json(raw)),'sha256:',returnProperty='Descriptor.PayloadDigest',
            returnMembers=dict(Descriptor=dict(kind='test',identity='identity-é',codec='test-json',codecVersion=1,payloadDigest='sha256:' + digest,byteLength=len(payload)),OwnerVersion=None))
    for label, raw, expected_hash, length_delta in [('malformed','{',base_digest,0),('wrong-hash',variants[0][1],'0'*64,0),('wrong-length',variants[0][1],base_digest,1)]:
        descriptor=dict(kind='test',identity='identity',codec='test-json',codecVersion=1,payloadDigest='sha256:' + expected_hash,byteLength=len(raw.encode()) + length_delta)
        rows.append(dict(name='release-matches-bytes/' + label,assembly=release,type=release + '.RuntimeCapturedContentCache',method='MatchesBytes',arguments=[dict(json=descriptor),dict(utf8=raw)],expected=False))
        if length_delta == 0:
            rows.append(dict(name='http-page-body-verify/' + label,assembly='Elsa.Etl.Runtime',type='Elsa.Etl.Runtime.Http.EtlHttpPageCache',method='FixedTimeEqualsSha256',arguments=[dict(utf8=raw),dict(text=expected_hash)],expected=False))
    # Every signal collection participates, including ordered logs, nullable attributes and exact long counters.
    start, end = '2026-07-15T09:30:00+02:00', '2026-07-15T09:30:01+02:00'
    attrs = {'OwnerKey':'René','missing':None}
    telemetry = dict(resources=[dict(id='resource-é',serviceName='service-é',serviceInstanceId=None,telemetrySdkLanguage='dotnet',attributes=attrs,lastSeen=end,status=1)],
        instruments=[dict(id='instrument-é',resourceId='resource-é',name='duration',unit='ms',description=None,kind=2,attributes=attrs)],
        traces=[dict(traceId='trace-é',rootSpanId=None,name='trace-name',startTime=start,endTime=end,duration='00:00:01',status=1,resourceIds=['resource-é'],workflowInstanceIds=['run-é'],spanCount=1)],
        spans=[dict(id='span-row',traceId='trace-é',spanId='span-é',parentSpanId=None,resourceId='resource-é',name='step',kind='server',startTime=start,endTime=end,status=1,statusDescription=None,attributes=attrs,
            events=[dict(name='event-é',timestamp=end,attributes=attrs)],links=[dict(traceId='linked-trace',spanId='linked-span',attributes=attrs)])],
        metricPoints=[dict(id='point-é',instrumentId='instrument-é',instrumentName='duration',resourceId='resource-é',timestamp=end,value=1.25,sum=None,count=9007199254740993,attributes=attrs,traceId=None,spanId='span-é')],
        logs=[dict(id='log-a',resourceId='resource-é',timestamp=end,severityText='INFO',severityNumber=9,body='René <雪>',traceId='trace-é',spanId=None,attributes=attrs),
              dict(id='log-b',resourceId='resource-é',timestamp=end,severityText='WARN',severityNumber=13,body='second',traceId=None,spanId=None,attributes={})])
    def telemetry_projection(value, family):
        result = opaque(value)
        for field in ['lastSeen','startTime','endTime','timestamp','duration']:
            if field in value: result[field]=value[field]
        if 'attributes' in value:result['attributes']={opaque(key):opaque(item) for key,item in value['attributes'].items()}
        if 'status' in value:result['status']=(['Unknown','Active','Stale'] if family=='resources' else ['Unset','Ok','Error'])[value['status']]
        if family=='instruments':result['kind']=['Gauge','Sum','Histogram'][value['kind']]
        if 'events' in value:result['events']=[telemetry_projection(item,'events') for item in value['events']]
        if 'links' in value:result['links']=[telemetry_projection(item,'links') for item in value['links']]
        return result
    instance=dict(constructorArguments=dict(scopeFactory=dict(emptyScopeFactory=True),options=dict(optionsValue=dict(json={})),binding=dict(json=dict(tenantId='tenant',scopeId='scope',sourceId='source')),
        sourceRegistry=dict(clrNull=True),observer=dict(clrNull=True),timeProvider=dict(clrNull=True),keyBatchSize=dict(json=500)))
    import copy
    for label in ['base','log-order','metadata-case','metric-number','no-logs']:
        value=copy.deepcopy(telemetry)
        if label=='log-order':value['logs'].reverse()
        if label=='metadata-case':value['resources'][0]['attributes']={'ownerKey':'René','missing':None}
        if label=='metric-number':value['metricPoints'][0]['value']=1.5
        if label=='no-logs':value['logs']=[]
        projected={family:[telemetry_projection(item,family) for item in values] for family,values in value.items()}
        add('opentelemetry-batch/' + label,'Elsa.Diagnostics.OpenTelemetry.Persistence.EntityFrameworkCore',
            'Elsa.Diagnostics.OpenTelemetry.Persistence.EntityFrameworkCore.Stores.EfOpenTelemetryStore','Fingerprint',[dict(json=value)],projected,instance=instance)
    # The authoring owner hashes its normalized wire profile, including its own enum converters.
    data_type = dict(alias='Any',collectionKind='Single')
    for label, raw in variants:
        marker='__S9_EXACT_RAW__'
        argument=dict(referenceKey='argument-é',value=dict(value=marker,expressionType='Literal'),autoEvaluate=False,evaluatorType=None,storageDriverType=None,isSensitive=None,conversion=None)
        variable=dict(referenceKey='variable-é',name='OwnerValue',type=data_type,storageDriverType=None,default_=dict(value=marker,expressionType='Literal'))
        variable['default']=variable.pop('default_')
        input_definition=dict(referenceKey='input-é',name='Amount',type=data_type,storageDriverType=None,displayName='Amount',category=None,isNullable=True,
            isBrowsable=True,isSerializable=True,description='Raw data input',order=1.25,uiHint='json-editor',propertyInfo=marker,uiSpecifications=marker,isRequired=True,
            defaultValue=marker,defaultSyntax='Literal',isSensitive=True)
        output_definition=dict(referenceKey='output-é',name='Result',type=data_type,storageDriverType=None,displayName='Result',category=None,isNullable=True,
            isBrowsable=True,isSerializable=True,description='Raw data output',order=2.5,uiHint='json-editor',propertyInfo=marker,uiSpecifications=marker,isRequired=False,sourceRepresentation=None)
        root=dict(nodeId='root-é',activityVersionId='',inputs=[argument],outputs=[],structure=None,intrinsic=dict(kind='return',valueType=data_type,variable=None))
        projected=dict(variables=[variable],rootActivity=root,inputs=[input_definition],outputs=[output_definition],
            strategyOptions=dict(nullHandling=None,activationStrategyType='default',incidentStrategy=dict(alias='fault',version='1.0.0'),persistenceMode='Balanced'))
        actual=json.loads(json.dumps(projected));actual['rootActivity']['intrinsic']['kind']=3
        actual['strategyOptions'].pop('nullHandling')
        input_text=json.dumps(actual,ensure_ascii=True,separators=(',',':')).replace(json.dumps(marker),raw)
        # S2 derives workflow member nullability from the type and omits the CLR catalog flag from both authored wires.
        for member in [*projected['inputs'],*projected['outputs']]:member.pop('isNullable')
        wire_text=json.dumps(projected,ensure_ascii=True,separators=(',',':')).replace(json.dumps(marker),raw)
        expected_wire=helper.load_json(wire_text)
        for member in [*actual['inputs'],*actual['outputs']]:member.pop('isNullable')
        # Both the CLR state and the authored API wire retain S-2's absent strategy override as null.
        actual['strategyOptions']['nullHandling']=None
        reconstructed_text=json.dumps(actual,ensure_ascii=True,separators=(',',':')).replace(json.dumps(marker),raw)
        add('authoring-snapshot-rich/' + label,'Elsa.Workflows.Design.Api','Elsa.Workflows.Design.Api.Services.WorkflowAuthoringRevision','Snapshot',
            [dict(typedJson=input_text)],expected_wire,returnField='Item2',returnFieldsJson=dict(Item1=reconstructed_text),returnRawPaths={
                path:raw.strip(' \t\r\n') for path in ['Item1.Variables[0].Default.Value','Item1.RootActivity.Inputs[0].Value.Value',
                    'Item1.Inputs[0].PropertyInfo','Item1.Inputs[0].UISpecifications','Item1.Inputs[0].DefaultValue',
                    'Item1.Outputs[0].PropertyInfo','Item1.Outputs[0].UISpecifications']})
    # ETL identity framing and the two retained-row JSON digest owners are pinned separately.
    def etl_frame(parts):
        result=b''
        for part in parts:
            data=None if part is None else part.encode('utf-16-le',errors='surrogatepass')
            result+=struct.pack('<i',-1 if data is None else len(data)) + (data or b'')
        return result
    for label, parts in [('empty',[]),('null',[None]),('empty-string',['']),('unicode',['René','雪']),
        ('framing-a',['a|b','c']),('framing-b',['a','b|c']),('lone-a',[chr(0xD800)]),('lone-b',[chr(0xD801)])]:
        frame=etl_frame(parts)
        rows.append(dict(name='etl-identity-frame/' + label,assembly='Elsa.Etl.Core',type='Elsa.Etl.Core.Runtime.EtlRuntimeIdentity',method='Hash',
            arguments=[dict(arrayArguments=[dict(clrNull=True) if part is None else dict(utf16=[ord(char) for char in part]) for part in parts])],
            frameHex=frame.hex(),expected=hashlib.sha256(frame).hexdigest()))
    for label, raw in [*variants,('root-null','null')]:
        data=helper.load_json(raw)
        for family, owner, method in [('etl-metadata-digest','Elsa.Etl.Persistence.EntityFrameworkCore.Stores.EfEtlRuntimeStore','MetadataDigest'),
                                      ('etl-definition-digest','Elsa.Etl.Persistence.EntityFrameworkCore.Stores.EtlDefinitionRecordCodec','HashText')]:
            add(family + '/' + label,'Elsa.Etl.Persistence.EntityFrameworkCore',owner,method,[dict(text=raw)],data)
    address=dict(formatVersion=1,workflowExecutionId='workflow-é',activityExecutionId='activity-é',executableId='executable-é',executableHash='executable-hash',
        nodeId='node-é',bookmarkId='bookmark-é',registrationId='registration-é',resumeTargetId='resume-é',stimulusType='etl-completed',stimulusHash='stimulus-hash',
        payloadTypeAlias='EtlResult',payloadSchemaVersion=1,deduplicationPolicy='Once',providerId='elsa.etl.completion.v1')
    for label, raw in variants:
        digest=hashlib.sha256(helper.canonical(helper.load_json(raw)).encode()).hexdigest()
        artifact=dict(constructorArguments=dict(formatVersion=dict(json=1),artifactId=dict(text='artifact-é'),contentHash=dict(text='claimed-content-hash'),
            definitionId=dict(text='definition-é'),definitionVersion=dict(json=7),pipeline=dict(rawJson=raw)))
        invocation=dict(constructorArguments=dict(Scope=dict(json=dict(tenantId=None,persistencePartition='partition-é')),RunId=dict(text='run-é'),Artifact=artifact,ResultAddress=dict(json=address)))
        parts=['etl-invocation-v1',None,'partition-é','run-é','1','artifact-é','claimed-content-hash','definition-é','7','json-sha256:' + digest]
        parts += [str(address[key]) for key in ['formatVersion','workflowExecutionId','activityExecutionId','executableId','executableHash','nodeId','bookmarkId','registrationId','resumeTargetId','stimulusType','stimulusHash','payloadTypeAlias','payloadSchemaVersion','deduplicationPolicy','providerId']]
        frame=etl_frame(parts)
        rows.append(dict(name='etl-invocation-fingerprint/' + label,assembly='Elsa.Etl.Core',type='Elsa.Etl.Core.Runtime.EtlRuntimeIdentity',method='InvocationFingerprint',
            arguments=[invocation],frameHex=frame.hex(),expected=hashlib.sha256(frame).hexdigest(),
            scope='Hash owner only; a mismatched claimed artifact hash is intentionally retained to detect changed raw pipeline data, not asserted as runtime admission.'))
    # Background receipts select an explicit normal-JSON contract, not the opaque runtime projection.
    runtime = 'Elsa.Workflows.Runtime.Core'
    marker = '__S9_OWNER_RAW__'
    def typed_raw(value, raw):
        return dict(typedJson=json.dumps(value, ensure_ascii=True, separators=(',', ':')).replace(json.dumps(marker), raw))
    attempt = dict(attemptId='attempt-é', invocationId='invocation-é', ordinal=1, reason=0,
        startedAt='2026-07-15T09:30:00+02:00', endedAt=None, triggerDeliveryId=None, transitionKind=None, incidentId=None)
    coordinator = dict(workItemId='work-item-é', workflowExecutionId='workflow-é', commandId='command-é', commandKind=0,
        envelopeId='envelope-é', idempotencyKey='idempotency-é', enqueuedAt='2026-07-15T09:30:00+02:00',
        recordedAt='2026-07-15T09:30:01+02:00', sequence=9007199254740993, payload=marker,
        commandMetadata={'OwnerKey':'René'}, envelopeMetadata={}, executionScopeId='scope-é', attempt=None)
    request = dict(workId='work-é', workflowExecutionId='workflow-é', activityExecutionId='activity-é', attempt=attempt,
        executable=dict(artifactId='artifact-é', definitionId='definition-é', definitionVersionId='version-é', artifactVersion='7', artifactHash='hash-é'),
        executableNodeId='node-é', contractFingerprint='contract-é',
        inputSnapshot=dict(invocationId='invocation-é', contractFingerprint='contract-é', bindingFingerprint='binding-é', values={},
            materializedAt='2026-07-15T09:30:00+02:00'), coordinatorWorkItem=coordinator)
    def receipt_projection(value, raw, kind='Complete', selection=None, fault=None, cancellation=None, warnings=None):
        result = {key:copy.deepcopy(value[key]) for key in ['workId','workflowExecutionId','activityExecutionId','attempt',
            'coordinatorWorkItem','executable','executableNodeId','contractFingerprint','inputSnapshot']}
        result['attempt']['reason'] = ['Initial','Retry','Resume'][result['attempt']['reason']]
        result['coordinatorWorkItem']['commandKind'] = 'Start'
        result['coordinatorWorkItem']['payload'] = helper.load_json(raw)
        result.update(kind=kind, selection=selection if selection is not None else (dict(state='Unspecified',names=[]) if kind=='Complete' else None),
            resultJson=None, fault=fault, cancellationReason=cancellation, warnings=warnings or [])
        return result
    def terminal(kind=0, raw=None, selection=None, fault=None, cancellation=None, warnings=None):
        result = dict(constructorArguments=dict(kind=dict(json=kind),selection=dict(json=selection),resultJson=dict(clrNull=True) if raw is None else dict(text=raw),
            fault=dict(json=fault),cancellationReason=dict(json=cancellation)))
        if warnings is not None: result=dict(value=result,propertyArguments=dict(Warnings=dict(json=warnings)))
        return result
    for label, raw in variants:
        projected=receipt_projection(request, variants[0][1]);projected['resultJson']=helper.load_json(raw)
        add('background-receipt/result-' + label,runtime,'Elsa.Workflows.Runtime.Core.Models.BackgroundActivityWorkReceipt','ComputeDigest',
            [typed_raw(request,variants[0][1]),terminal(raw=raw)],projected)
        projected=receipt_projection(request,raw);projected['resultJson']=helper.load_json(variants[0][1])
        add('background-receipt/coordinator-' + label,runtime,'Elsa.Workflows.Runtime.Core.Models.BackgroundActivityWorkReceipt','ComputeDigest',
            [typed_raw(request,raw),terminal(raw=variants[0][1])],projected)
    for label, changes in [('operation-keys',dict(operationKey='omitted-operation',targetOperationKey='omitted-target')),
                           ('evaluation-one',{}),('attempt-retry',{}),('coordinator-sequence',{})]:
        value=copy.deepcopy(request);value.update(changes)
        if label=='evaluation-one':value['inputSnapshot']['evaluation']=1
        if label=='attempt-retry':value['attempt'].update(ordinal=2,reason=1)
        if label=='coordinator-sequence':value['coordinatorWorkItem']['sequence']+=1
        projected=receipt_projection(value,variants[0][1]);projected['resultJson']=helper.load_json(variants[0][1])
        add('background-receipt/' + label,runtime,'Elsa.Workflows.Runtime.Core.Models.BackgroundActivityWorkReceipt','ComputeDigest',
            [typed_raw(value,variants[0][1]),terminal(raw=variants[0][1])],projected)
    warning=[dict(code='warning-a',message='A path',severity='Warning',details={'path':'OwnerKey'}),
             dict(code='warning-b',message='Another path',severity='Information',details=None)]
    fault=dict(code='fault-é',exceptionType='FixtureFailure',message='Failed',sanitizedStackTrace=None,isRetryable=True,metadata={})
    for label, kind, raw, selection, fault_value, cancellation, warnings in [
        ('absent-result',0,None,None,None,None,None),('null-result',0,'null',None,None,None,None),
        ('empty-selection',0,variants[0][1],dict(state='Empty',names=[]),None,None,None),
        ('named-selection',0,variants[0][1],dict(state='Named',names=['Ready','Done']),None,None,None),
        ('warnings',0,variants[0][1],None,None,None,warning),('warning-order',0,variants[0][1],None,None,None,list(reversed(warning))),
        ('fault',1,None,None,fault,None,None),('cancel',2,None,None,None,'cancelled-é',None)]:
        projected=receipt_projection(request,variants[0][1],['Complete','Fault','Cancel'][kind],selection,fault_value,cancellation,warnings)
        projected['resultJson']=None if raw is None else helper.load_json(raw)
        add('background-receipt/' + label,runtime,'Elsa.Workflows.Runtime.Core.Models.BackgroundActivityWorkReceipt','ComputeDigest',
            [typed_raw(request,variants[0][1]),terminal(kind,raw,selection,fault_value,cancellation,warnings)],projected)

    # The hierarchy owner hashes the full retained record with opaque identity strings.
    hierarchy_item=dict(activityExecutionId='activity-é',workflowExecutionId='workflow-é',executableNodeId='node-é',authoredActivityId='authored-é',
        activityType='Fixture.Activity',activityTypeVersion='1.0.0',status=1,subStatus='Pending',executionSequence=9007199254740993,
        scheduledAt='2026-07-15T09:30:00+02:00',startedAt='2026-07-15T09:30:01+02:00',completedAt=None,parentActivityExecutionId=None,
        schedulingActivityExecutionId='scheduler-é',relativeDepth=1,branchId='branch-é',iterationId=None,outcomeNames=['Ready','Done'],
        bookmarkCount=2,incidentCount=1,blockingIncidentCount=0,attempt=dict(attemptNumber=2,firstAttemptActivityExecutionId='first-é',previousAttemptActivityExecutionId='previous-é'),
        boundary=dict(kind='activity',definitionId='definition-é',definitionVersionId='version-é',version='1.0.0',templateHash='template-é',
            invocationOrigin=dict(segments=[dict(kind=1,id='root-é'),dict(kind=3,id='template-é')]),executionScopeId='scope-é',hasChildren=True,
            directChildCount=2,committedDescendantCount=9007199254740993,aggregate=dict(status=1,total=3,scheduled=1,running=1,suspended=0,completed=1,
                faulted=0,cancelled=0,blockingIncidentCount=0,retryCount=1,lastExecutionSequence=9007199254740993),layoutAvailable=True,executableNodeId='boundary-node-é'),
        metadata={'OwnerKey':'René','json-looking':'{"a":1.0}'})
    hierarchy=dict(workflowExecutionId='workflow-é',executionScopeId='scope-é',activityExecutionId='activity-é',parentActivityExecutionId=None,
        executionSequence=9007199254740993,item=hierarchy_item)
    def hierarchy_projection(value):
        result=opaque(value);item=result['item'];original=value['item']
        item['status']=['Scheduled','Running','Waiting','Suspended','Completed','Faulted','Cancelled','Recovered','Superseded'][original['status']]
        for field in ['scheduledAt','startedAt','completedAt']:item[field]=original[field]
        item['metadata']={opaque(key):opaque(value) for key,value in original['metadata'].items()}
        if original['boundary'] is not None:
            item['boundary']['aggregate']['status']=['Empty','Running','Suspended','Completed','Faulted','Cancelled'][original['boundary']['aggregate']['status']]
            for segment, source in zip(item['boundary']['invocationOrigin']['segments'],original['boundary']['invocationOrigin']['segments']):
                segment['kind']={1:'WorkflowRoot',2:'AuthoredNode',3:'TemplateBoundary',4:'NestedPlacement'}[source['kind']]
        return result
    for label in ['base','metadata-order','metadata-case','json-looking-text','outcome-order','sequence','parent','without-boundary','without-attempt','date']:
        value=copy.deepcopy(hierarchy)
        if label=='metadata-order':value['item']['metadata']=dict(reversed(list(value['item']['metadata'].items())))
        if label=='metadata-case':value['item']['metadata']['ownerKey']=value['item']['metadata'].pop('OwnerKey')
        if label=='json-looking-text':value['item']['metadata']['json-looking']='{ "a":1 }'
        if label=='outcome-order':value['item']['outcomeNames'].reverse()
        if label=='sequence':value['executionSequence']+=1;value['item']['executionSequence']+=1
        if label=='parent':value['parentActivityExecutionId']=value['item']['parentActivityExecutionId']='parent-é'
        if label=='without-boundary':value['item']['boundary']=None
        if label=='without-attempt':value['item']['attempt']=None
        if label=='date':value['item']['startedAt']='2026-07-15T09:30:02+02:00'
        add('hierarchy-snapshot/' + label,'Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore',
            'Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores.EfActivityExecutionHierarchyStore','SnapshotFingerprint',
            [dict(json=value)],hierarchy_projection(value),uppercase=True)

    # Elsa 3's collection owner builds an ordered heterogeneous field list. Extension data is flattened on input.
    source=dict(id='version-é',definitionId='definition-é',name='Flow',description=None,version=7,createdAt='2026-07-15T09:30:00+02:00',toolVersion=None,
        isReadonly=False,isSystem=False,isLatest=True,isPublished=True,options=dict(usableAsActivity=True,autoUpdateConsumingWorkflows=False),
        inputs=[dict(name='amount',type='Any',storageDriverType=None,displayName=None,description=None)],
        outputs=[dict(name='result',type='String',storageDriverType=None,displayName='Result',description=None)],
        variables=[dict(id='variable-é',name='OwnerValue',typeName='Object',isArray=False,value='{"literal":1.0}',storageDriverTypeName=None)],outcomes=['Done','Error'],
        customProperties={'aData':marker,'ZKeep':None},root=dict(id='root',nodeId='root-node',name='Root',type='Elsa.Sequence',version=1,
            customProperties=dict(canStartWorkflow=False,runAsynchronously=True),metadata=dict(displayText='Root display',
                designer=dict(position=dict(x=1.25,y=-2.5),size=dict(width=120,height=80)),ownerMetadata=marker),ownerData=marker,
            activities=[dict(id='child-a',nodeId='node-a',name='First',type='Elsa.WriteLine',version=None),
                        dict(id='child-b',nodeId='node-b',name='Second',type='Elsa.WriteLine',version=None)],
            connections=[dict(source=dict(activity='child-a',port='Done'),target=dict(activity='child-b',port='In'))]))
    def source_projection(value, raw):
        fields=[]
        def clr(*values):fields.extend(dict(representation='clr',value=opaque(item)) if item is not None else None for item in values)
        def data(item):fields.append(dict(representation='json',value=helper.load_json(raw) if item==marker else item))
        def boolean(item):return '' if item is None else str(item)
        options=value.get('options') or {}
        clr(value['id'],value['definitionId'],value['name'],value.get('description') or '',str(value['version']),
            '2026-07-15T07:30:00.0000000+00:00',value.get('toolVersion') or '',boolean(value['isReadonly']),boolean(value['isSystem']),
            boolean(value['isLatest']),boolean(value['isPublished']),boolean(options.get('usableAsActivity')),boolean(options.get('autoUpdateConsumingWorkflows')))
        for family in ['inputs','outputs']:
            for item in value[family]:clr('input' if family=='inputs' else 'output',item['name'],item['type'],item.get('storageDriverType') or '',item.get('displayName') or '',item.get('description') or '')
        for item in value['variables']:clr('variable',item['id'],item['name'],item['typeName'],boolean(item['isArray']),item.get('value') or '',item.get('storageDriverTypeName') or '')
        for outcome in value['outcomes']:clr('outcome',outcome)
        for key,item in sorted(value['customProperties'].items()):clr('workflow-property',key);data(item)
        def visit(activity):
            properties=activity.get('customProperties');metadata=activity.get('metadata')
            coordinates=['1.25','-2.5','120','80'] if metadata and 'designer' in metadata else (['0']*4 if metadata is not None else ['']*4)
            clr('activity',activity['id'],activity['nodeId'],activity['name'],activity['type'],str(activity['version']) if activity.get('version') is not None else '',
                boolean(properties.get('canStartWorkflow',False)) if properties is not None else '',boolean(properties.get('runAsynchronously',False)) if properties is not None else '',
                (metadata or {}).get('displayText') or '',*coordinates)
            for key in sorted(set(activity)-{'id','nodeId','name','type','version','customProperties','metadata','activities','connections'}):clr(key);data(activity[key])
            for key in sorted(set(metadata or {})-{'displayText','designer'}):clr('activity-metadata',key);data(metadata[key])
            for connection in activity.get('connections') or []:clr('connection',connection['source']['activity'],connection['source']['port'],connection['target']['activity'],connection['target']['port'])
            for child in activity.get('activities') or []:visit(child)
        if value.get('root') is not None:visit(value['root'])
        return dict(kind=opaque('source'),fields=fields)
    for label, raw in [*variants,('json-string','"René"'),('root-null','null')]:
        add('elsa3-source/' + label,'Elsa3.Activities.Design.Import','Elsa3.Activities.Design.Import.Services.ReusableActivityCollectionAnalyzer','SourceFingerprint',
            [typed_raw(source,raw)],source_projection(source,raw),'elsa3-source-')
    for label in ['dictionary-order','dictionary-case','variable-text','child-order','connection','empty-description','empty-metadata','no-metadata','empty-custom-properties']:
        value=copy.deepcopy(source)
        if label=='dictionary-order':value['customProperties']=dict(reversed(list(value['customProperties'].items())))
        if label=='dictionary-case':value['customProperties']['AData']=value['customProperties'].pop('aData')
        if label=='variable-text':value['variables'][0]['value']='{ "literal":1 }'
        if label=='child-order':value['root']['activities'].reverse()
        if label=='connection':value['root']['connections'][0]['target']['port']='Other'
        if label=='empty-description':value['description']=''
        if label=='empty-metadata':value['root']['metadata']={}
        if label=='no-metadata':value['root']['metadata']=None
        if label=='empty-custom-properties':value['root']['customProperties']={}
        add('elsa3-source/' + label,'Elsa3.Activities.Design.Import','Elsa3.Activities.Design.Import.Services.ReusableActivityCollectionAnalyzer','SourceFingerprint',
            [typed_raw(value,variants[0][1])],source_projection(value,variants[0][1]),'elsa3-source-')
    # Journal request v2 omits only the physical activity delivery from the otherwise complete request.
    contract_material=dict(activityTypeKey='test.activity',contractVersion='1',descriptorKind='test',descriptorPayload=dict(x=1),inputs=[],outcomes=[],
        activation=dict(descriptorKind='test',capability='execute'),result=dict(Type=dict(alias='Int32',collectionKind='Single',schema=None,schemaVersion=None),
            IsRequired=False,Policy=dict(isPersistable=True,isSensitive=False,requiresEncryption=False,redactionMode=None,lifecycle='Instance',storage='Inline',
                storageProfile=None,retentionPolicy=None),projections=[]))
    contract_hash='sha256:' + hashlib.sha256(canonical(contract_material).encode()).hexdigest()
    contract_wire=dict(activityTypeKey='test.activity',contractVersion='1',schemaFingerprint=contract_hash,descriptorKind='test',descriptorPayload=dict(x=1),
        inputs={},outcomes=[],activation=contract_material['activation'],result=dict(type=contract_material['result']['Type'],isRequired=False,
            policy=contract_material['result']['Policy'],projections={}))
    agent_identity=dict(tenantId='tenant-é',persistencePartition='partition-é',workflowExecutionId='workflow-é',activityExecutionId='activity-é',
        operationKey='operation-é',artifactId='artifact-é',artifactHash='artifact-hash',definitionVersionId='version-é',templateHash='template-é')
    descriptor=dict(instructions='Explain the supplied data.',model=dict(providerKey='fixture',connectionName='connection',modelId='model'),settings={},
        inputSchema={},resultSchema={},capabilitiesFingerprint='capability-é',schemaProfile='elsa.agent.structured-object/1',
        limits=dict(maximumModelCalls=8,timeoutMilliseconds=300000,maximumOutputTokens=None),allowContinuity=False,
        definitionId=None,definitionVersionId=None,version=None,templateHash=None,sourceReferenceId=None)
    agent_request=dict(identity=agent_identity,descriptor=descriptor,contract=contract_wire,inputs=marker,bindingFingerprint='binding-é',
        preparedSession=None,sessionTarget=None,continuation=None)
    def agent_projection(value,raw,delivery):
        result=copy.deepcopy(value);result['inputs']=helper.load_json(raw)
        if delivery:result['identity']['activityExecutionId']=''
        return result
    def agent_argument(value,raw):
        # Baseline CLR admission uses numeric ActivityValueLifecycle.Instance/ActivityValueStorage.Inline (both zero).
        # The hash owner's independent fixed profile still emits their names in the expected projection.
        argument=copy.deepcopy(value)
        argument['contract']['result']['policy'].update(lifecycle=0,storage=0)
        return typed_raw(argument,raw)
    for label,raw in variants:
        for mode,delivery in [('legacy',False),('delivery',True)]:
            add('agent-request/' + mode + '-' + label,'Elsa.Agent','Elsa.Agent.Journal.AgentTurnJournal','RequestHash',
                [agent_argument(agent_request,raw),dict(text='elsa.agent.turn/request/' + ('2' if delivery else '1'))],agent_projection(agent_request,raw,delivery))
    for label in ['other-activity','workspace','effective-limits','prepared-session','session-target','continuation','binding','descriptor-settings']:
        value=copy.deepcopy(agent_request)
        if label=='other-activity':value['identity']['activityExecutionId']='other-activity-é'
        if label=='workspace':value['identity']['conversationWorkspace']='workspace-é'
        if label=='effective-limits':value['effectiveLimits']=dict(maximumModelCalls=1,timeoutMilliseconds=20000,maximumOutputTokens=32)
        if label=='prepared-session':value['preparedSession']=dict(conversationId='conversation-é',revision=9007199254740993,protectedValue='{"opaque":1.0}')
        if label=='session-target':value['sessionTarget']=dict(conversationId='conversation-é',revision=9007199254740993)
        if label=='continuation':value['continuation']=dict(conversationId='conversation-é',expectedRevision=9007199254740993)
        if label=='binding':value['bindingFingerprint']='other-binding-é'
        if label=='descriptor-settings':value['descriptor']['settings']=dict(temperature=0.25)
        for mode,delivery in [('legacy',False),('delivery',True)]:
            add('agent-request/' + mode + '-' + label,'Elsa.Agent','Elsa.Agent.Journal.AgentTurnJournal','RequestHash',
                [agent_argument(value,variants[0][1]),dict(text='elsa.agent.turn/request/' + ('2' if delivery else '1'))],agent_projection(value,variants[0][1],delivery))
    # Actual binding constructors/resolvers: file identities are opaque; HTTP and SQL are ordinary owner JSON.
    binding_scope=dict(tenantId='tenant-é',persistencePartition='partition-é')
    scope_identity=hashlib.sha256(etl_frame(['etl-scope-v1','tenant-é','partition-é'])).hexdigest()
    etl_runtime='Elsa.Etl.Runtime'
    for platform,path in [('windows','C:\\etl-fixture\\source.csv'),('unix','/etl-fixture/source.csv')]:
        for label,role,expected_hash in [('source',0,'A'*64),('source-lowercase',0,'a'*64),('output',2,None)]:
            binding=dict(scope=binding_scope,alias='source',role=role,revision='v1',path=path,isCurrent=True,expectedSha256=expected_hash,
                immutableInput=role==0,atomicPublication=role==2)
            projection=opaque(dict(formatVersion=1,scopeIdentity=scope_identity,alias='source',role='Source' if role==0 else 'MainOutput',revision='v1',
                path=path,expectedSha256=expected_hash.lower() if expected_hash else None,immutableInput=role==0,atomicPublication=role==2))
            projection['role']='Source' if role==0 else 'MainOutput'
            add('host-file-binding/' + platform + '-' + label,etl_runtime,'Elsa.Etl.Runtime.Bindings.HostFileBindings','Resolve',
                [dict(json=binding_scope),dict(text='source'),dict(json=role)],projection,platform=platform,returnProperty='Handle',
                instance=dict(constructorArguments=dict(bindings=dict(arrayArguments=[dict(json=binding)]))))
    metadata=dict(tenantId='tenant-é',name='api',typeName='http',authScheme='api-key',owner='Tenant',status='Ready',
        settings=[dict(key='baseurl',value='https://api.fixture.invalid/')],allowedHostsKind='fixed',allowedHosts=['api.fixture.invalid'])
    for label in ['anonymous','connection','endpoint','paths','shard','connection-alias']:
        endpoint='https://api.fixture.invalid/data' if label!='endpoint' else 'https://api.fixture.invalid/other'
        paths=['/data','/other'] if label=='paths' else [endpoint.split('.invalid')[1]]
        shards=[dict(id='north',seedUri=endpoint+'?cursor=first')]
        if label=='shard':shards[0]['id']='south'
        connection=None if label=='anonymous' else metadata
        connection_name=None if connection is None else ('other' if label=='connection-alias' else 'API')
        projection=dict(formatVersion=1,scopeIdentity=scope_identity,alias='source',role='Source',revision='v1',endpoint=endpoint,allowHttp=False,
            allowedPaths=sorted(paths),idempotencyHeader='Idempotency-Key',connectionAlias=connection_name.lower() if connection_name else None,
            shards=shards,connection=connection)
        binding=dict(constructorArguments=dict(Scope=dict(json=binding_scope),Alias=dict(text='source'),Role=dict(text='Source'),Revision=dict(text='v1'),
            EndpointPolicy=dict(constructorArguments=dict(endpoint=dict(text=endpoint),allowedPaths=dict(json=paths),allowHttp=dict(json=False))),
            AllowHttp=dict(json=False),IsCurrent=dict(json=True),ConnectionName=dict(json=connection_name),IdempotencyHeader=dict(text='Idempotency-Key'),Shards=dict(json=shards)))
        add('host-http-binding/' + label,etl_runtime,'Elsa.Etl.Runtime.Bindings.HostHttpBindings','ComputeHandle',[binding,dict(json=connection)],projection)
    for label in ['base','password','tables-order','database','port','ledger']:
        database='orders' if label!='database' else 'other'
        port=5432 if label!='port' else 5433
        password='fixture-password' if label!='password' else 'different-fixture-password'
        tables=['public.orders','public.other'] if label!='tables-order' else ['public.other','public.orders']
        ledger='elsa_etl_effect_ledger' if label!='ledger' else 'custom_ledger'
        options=dict(tenantId='tenant-é',persistencePartition='partition-é',alias='warehouse',role='MainOutput',revision='v1',isCurrent=True,provider='postgresql',
            connectionString=f'Host=db.fixture.invalid;Port={port};Database={database};Username=owner;Password={password}',allowedTables=tables,ledgerTable=ledger,createLedger=True)
        instance=dict(constructorArguments=dict(options=dict(arrayArguments=[dict(json=options)]),engines=dict(arrayArguments=[dict(clrType='Elsa.Etl.Sql.PostgreSql.PostgreSqlEtlSqlEngine',
            clrAssembly='Elsa.Etl.Sql.PostgreSql',constructorArguments={})])))
        projection=dict(formatVersion=1,scopeIdentity=scope_identity,alias='warehouse',role='MainOutput',revision='v1',provider='postgresql',host='db.fixture.invalid',
            port=str(port),database=database,user='owner',allowedTables=sorted(tables),ledgerTable=ledger,createLedger=True)
        add('host-sql-binding/' + label,etl_runtime,'Elsa.Etl.Runtime.Bindings.HostSqlBindings','ResolveCurrent',
            [dict(json=binding_scope),dict(text='warehouse'),dict(text='MainOutput'),dict(text='public.orders')],projection,instance=instance,returnProperty='Handle')
    # Evidence's observable comparand binds canonical inline data even when the value is redacted or truncated.
    evidence='Elsa.Workflows.ExecutionEvidence'
    for label,raw in variants:
        for mode in ['Captured','Sensitive','Truncated']:
            # DurableValueLifecycle.Instance and DurableValueStorage.Inline are one in the baseline CLR argument profile.
            policy=dict(lifecycle=1,storage=1,isSensitive=mode=='Sensitive',requiresEncryption=False,redactionMode=None,retentionPolicy=None,metadata={})
            envelope=dict(constructorArguments=dict(type=dict(json=dict(alias='Any',collectionKind='Single',schema=None,schemaVersion=None)),presence=dict(json=2),
                inlineValue=dict(rawJson=raw),externalReference=dict(json=None),policy=dict(json=policy),withheldValue=dict(json=None)))
            instance=dict(constructorArguments=dict(store=dict(clrType='Elsa.Workflows.ExecutionEvidence.Services.InMemoryExecutionEvidenceStore',clrAssembly=evidence,
                constructorArguments=dict(options=dict(json=None))),options=dict(json=dict(maxInlineValueLength=1 if mode=='Truncated' else 8192,redactSensitiveValues=True)),logger=dict(json=None)))
            extra=dict(returnProperty='Comparand',instance=instance)
            if mode=='Captured':extra['returnRawPaths']={'Value':raw.strip(' \t\r\n')}
            add('evidence-capture/' + mode.lower() + '-' + label,evidence,'Elsa.Workflows.ExecutionEvidence.Services.ExecutionEvidenceCheckpointEnricher','Capture',
                [envelope],helper.load_json(raw),mode+':',**extra)
    # Publication review binds the explicitly selected draft/review facts, with opaque CLR strings.
    draft=dict(id='draft-é',tenantId=None,definitionId='definition-é',revision=9007199254740993)
    readiness=dict(kind='provider',key='fixture',schemaVersion='1',status='Ready',supportedSchemaVersions=['1','2'])
    dependencies=[dict(definitionId='dependency-é',versionId='version-é',version='1.0.0',templateHash='hash-é',occurrenceId='occurrence-é')]
    for label in ['base','revision','head','versions-order','dependency','provider','storage','runtime']:
        value=copy.deepcopy(draft);head=None;versions=['1.0.0','2.0.0'];deps=copy.deepcopy(dependencies);provider=copy.deepcopy(readiness);storage=[];runtime_readiness=[]
        if label=='revision':value['revision']+=1
        if label=='head':head='head-é'
        if label=='versions-order':versions.reverse()
        if label=='dependency':deps[0]['templateHash']='other-hash'
        if label=='provider':provider['status']='Unavailable'
        if label=='storage':storage=[dict(kind='storage',key='inline',schemaVersion=None,status='Ready',supportedSchemaVersions=[])]
        if label=='runtime':runtime_readiness=[dict(kind='runtime',key='execute',schemaVersion='1',status='Ready',supportedSchemaVersions=['1'])]
        projection=opaque(dict(id=value['id'],tenantId=value['tenantId'],definitionId=value['definitionId'],revision=value['revision'],headVersionId=head,
            reviewedVersion='1.0.0',templateHash=None,providerFingerprint=None,diff=None,requiredBump='Patch',validVersions=versions,
            dependencies=deps,provider=provider,storage=storage,runtime=runtime_readiness,diagnostics=[]))
        add('publication-review/' + label,'Elsa.Workflows.Publishing.Api','Elsa.Workflows.Publishing.Api.Services.ActivityPublicationReviewPolicy','ReviewToken',
            [dict(json=value),dict(json=head),dict(text='1.0.0'),dict(json=None),dict(json=None),dict(json=1),dict(json=versions),dict(json=deps),dict(json=provider),
             dict(json=storage),dict(json=runtime_readiness),dict(json=[])],projection,'sha256:')
    # These production helpers receive the real caller's existing process key or per-attempt nonce.
    for label,raw in variants:
        for key_label,key in [('ascending',bytes(range(32))),('descending',bytes(reversed(range(32))))]:
            digest=hmac.new(key,helper.canonical(helper.load_json(raw)).encode(),hashlib.sha256).hexdigest()
            rows.append(dict(name='shell-revision/' + key_label + '-' + label,assembly='Elsa.Studio.Hosting.Nuplane',
                type='Elsa.Studio.Hosting.Nuplane.ShellFeatureStore',method='HashRevision',arguments=[dict(rawJson=raw),dict(keyHex=key.hex())],
                canonical=helper.canonical(helper.load_json(raw)),expected=digest))
    connector_revision=dict(kind=4,revision=dict(alias='source',revision='v1',handle='source-handle',expectedSha256=None))
    connector_bindings=dict(source=connector_revision,lookup=None,main=connector_revision,errors=connector_revision,httpShardIds=[])
    for label,raw in variants:
        invocation_row=next(row for row in rows if row['name']=='etl-invocation-fingerprint/'+label)
        admission=dict(constructorArguments=dict(Invocation=invocation_row['arguments'][0],Bindings=dict(json=connector_bindings)))
        for nonce in ['aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa','bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb']:
            projection=dict(formatVersion=2,kind='connector-input-snapshot',invocation=invocation_row['expected'],role='Source',pinHandle='source-handle',attempt=nonce)
            add('rabbitmq-snapshot-identity/' + label + '-' + nonce[0],etl_runtime,'Elsa.Etl.Runtime.Preparation.EtlConnectorPreparer','RabbitMqSnapshotIdentity',
                [admission,dict(json='-'.join([nonce[:8],nonce[8:12],nonce[12:16],nonce[16:20],nonce[20:]]))],projection)
    # ETL terminal identity hashes the owner's u16-prefixed wire strings and camel-case enum, not CLR serialization.
    etl_ef='Elsa.Etl.Persistence.EntityFrameworkCore'
    terminal_instance=dict(constructorArguments=dict(db=dict(clrType='Elsa.Etl.Persistence.EntityFrameworkCore.EtlRuntimeSqliteDbContext',clrAssembly=etl_ef,
        constructorArguments=dict(options=dict(constructorArguments={}))),clock=dict(systemTimeProvider=True),options=dict(json={}),logger=dict(json=None)))
    terminal_invocation=next(row for row in rows if row['name']=='etl-invocation-fingerprint/base')['arguments'][0]
    for label in ['success','fault','diagnostic','accepted','rejected','filtered','settlement-time']:
        outcome=2 if label=='fault' else 0
        diagnostic=dict(code='failure-é',recordOrdinal=9007199254740993,field='OwnerKey') if label=='diagnostic' else None
        run=dict(invocationFingerprint='invocation-hash',acceptedRecords=9007199254740993 if label=='accepted' else 1,
            rejectedRecords=2 if label=='rejected' else 0,filteredRecords=3 if label=='filtered' else 0)
        projection=dict(version='u16:'+opaque('etl-terminal-v1'),invocationFingerprint='u16:'+opaque(run['invocationFingerprint']),outcome='faulted' if outcome==2 else 'succeeded',
            diagnostic=None if diagnostic is None else dict(code='u16:'+opaque(diagnostic['code']),recordOrdinal=diagnostic['recordOrdinal'],field='u16:'+opaque(diagnostic['field'])),
            acceptedRecords=run['acceptedRecords'],rejectedRecords=run['rejectedRecords'],filteredRecords=run['filteredRecords'])
        add('etl-terminal/' + label,etl_ef,'Elsa.Etl.Persistence.EntityFrameworkCore.Stores.EfEtlRuntimeStore','BuildTerminal',
            [terminal_invocation,dict(json=outcome),dict(json=diagnostic),dict(json=run),dict(json=1 if label=='settlement-time' else 0)],projection,
            instance=terminal_instance,returnProperty='ResultId')
    # Invalid Rules dispatch still binds the complete frozen request and native result address.
    rule_artifact=dict(formatVersion=1,languageId='elsa.rules.finite-positive',languageVersion=1,vocabulary=['fact'],rules=[])
    artifact_bytes=canonical(rule_artifact).encode();artifact_digest=hashlib.sha256(artifact_bytes).hexdigest()
    rule_pin=dict(formatVersion=1,sourceSha256='a'*64,artifact=dict(formatVersion=1,artifactId='rules:'+artifact_digest,sha256=artifact_digest,
        canonicalBytes=base64.b64encode(artifact_bytes).decode()),implementation=dict(pinSchemaVersion=1,packages=[],languageId='elsa.rules.finite-positive',
            languageVersion=1,receiptSchemaVersion=1,stateSchemaVersion=1),vocabularyCount=1)
    rule_request=dict(scope=dict(tenantId=None,persistencePartition='partition-é'),runId='run-é',program=rule_pin,initialFacts=[dict(atom='fact')],maxRounds=0,
        workflowExecutionId='workflow-é',activityExecutionId='activity-é',executableId='executable-é',executableHash='executable-hash',nodeId='node-é',resumeTargetId='resume-é')
    rule_address=dict(formatVersion=1,workflowExecutionId='workflow-é',activityExecutionId='activity-é',executableId='executable-é',executableHash='executable-hash',
        nodeId='node-é',bookmarkId='bookmark-é',resumeTargetId='resume-é',registrationId='registration-é',stimulusType='rules-completed',stimulusHash='b'*64,
        resultContractId='RuleResult',resultSchemaVersion=1,deduplicationPolicy='once',providerId='rules-provider')
    for label in ['base','rounds','fact','run','address']:
        value=copy.deepcopy(rule_request);destination=copy.deepcopy(rule_address)
        if label=='rounds':value['maxRounds']=-1
        if label=='fact':value['initialFacts']=[dict(atom='other')]
        if label=='run':value['runId']='other-run'
        if label=='address':destination['bookmarkId']='other-bookmark'
        add('rules-invalid-delivery/' + label,'Elsa.Rules.Workflows.Runtime','Elsa.Rules.Workflows.Runtime.Services.RulesNativeBoundary','InvalidDeliveryId',
            [dict(json=value),dict(json=destination)],dict(request=value,destination=destination,code='rules.invalid-round-limit'),'rules-invalid-')
    # Publication identities are private production helpers used by the actual snapshot/index publishers.
    source_chunk=dict(key='source.csv',contentHash='a'*64,byteLength=12)
    for label,raw in variants:
        invocation_row=next(row for row in rows if row['name']=='etl-invocation-fingerprint/'+label)
        admission=dict(constructorArguments=dict(Invocation=invocation_row['arguments'][0],Bindings=dict(json=connector_bindings)))
        add('connector-input-identity/' + label,etl_runtime,'Elsa.Etl.Runtime.Preparation.EtlConnectorPreparer','InputSnapshotIdentity',
            [admission,dict(json=0),dict(text='source-handle')],dict(formatVersion=2,kind='connector-input-snapshot',invocation=invocation_row['expected'],role='Source',pinHandle='source-handle'))
        for family,method,kind in [('connector-csv-index','CsvSourceIndexIdentity','connector-csv-source-index'),('connector-jsonl-index','JsonlSourceIndexIdentity','connector-jsonl-source-index')]:
            add(family+'/' + label,etl_runtime,'Elsa.Etl.Runtime.Preparation.EtlConnectorPreparer',method,[admission,dict(text='source-handle'),dict(json=source_chunk)],
                dict(formatVersion=2,kind=kind,invocation=invocation_row['expected'],sourcePinHandle='source-handle',sourceKey=source_chunk['key'],sourceHash=source_chunk['contentHash']))
    base_invocation=next(row for row in rows if row['name']=='etl-invocation-fingerprint/base')
    admission=dict(constructorArguments=dict(Invocation=base_invocation['arguments'][0],Bindings=dict(json=connector_bindings)))
    for label,role,handle in [('lookup',1,'source-handle'),('handle',0,'changed-handle')]:
        add('connector-input-identity/'+label,etl_runtime,'Elsa.Etl.Runtime.Preparation.EtlConnectorPreparer','InputSnapshotIdentity',
            [admission,dict(json=role),dict(text=handle)],dict(formatVersion=2,kind='connector-input-snapshot',invocation=base_invocation['expected'],role='Lookup' if role==1 else 'Source',pinHandle=handle))
    for label in ['key','hash','length']:
        chunk=copy.deepcopy(source_chunk)
        if label=='key':chunk['key']='other.csv'
        if label=='hash':chunk['contentHash']='b'*64
        if label=='length':chunk['byteLength']=13
        for family,method,kind in [('connector-csv-index','CsvSourceIndexIdentity','connector-csv-source-index'),('connector-jsonl-index','JsonlSourceIndexIdentity','connector-jsonl-source-index')]:
            add(family+'/'+label,etl_runtime,'Elsa.Etl.Runtime.Preparation.EtlConnectorPreparer',method,[admission,dict(text='source-handle'),dict(json=chunk)],
                dict(formatVersion=2,kind=kind,invocation=base_invocation['expected'],sourcePinHandle='source-handle',sourceKey=chunk['key'],sourceHash=chunk['contentHash']))
    for label in ['base','lookup','run','handle','ignored-scope-alias-revision']:
        role=1 if label=='lookup' else 0;run='other-run' if label=='run' else 'run-é';handle='other-handle' if label=='handle' else 'source-handle'
        pin=dict(scope=binding_scope,alias='source',role=role,revision='v1',handle=handle,expectedSha256='a'*64)
        if label=='ignored-scope-alias-revision':pin.update(scope=dict(tenantId=None,persistencePartition='other'),alias='other',revision='v2')
        add('v1-input-identity/'+label,etl_runtime,'Elsa.Etl.Runtime.Preparation.EtlFilePreparer','InputSnapshotIdentity',
            [dict(json=pin),dict(text=run)],dict(formatVersion=1,snapshotFormatVersion=1,runId=run,role='Lookup' if role==1 else 'Source',pinHandle=handle))
    # The real native-catalog receipt verifier checks canonical content but returns exact retained payload bytes.
    for label,raw in variants:
        digest='sha256:'+hashlib.sha256(helper.canonical(helper.load_json(raw)).encode()).hexdigest()
        descriptor=dict(kind='workflow-executable',identity='fixture',codec='test-json',codecVersion=1,payloadDigest=digest,byteLength=len(raw.encode()))
        rows.append(dict(name='native-catalog-payload/'+label,assembly='Elsa.Workflows.Publishing',type='Elsa.Workflows.Publishing.SourceCapture.NativeCatalogSourceMemberContributor',
            method='ValidateCapturedPayload',arguments=[dict(utf8=raw),dict(json=descriptor),dict(json=4096)],expected=digest,expectedBytesHex=raw.encode().hex()))
    for label,raw in [('malformed','{'),('wrong-hash',variants[0][1]),('wrong-length',variants[0][1]),('byte-limit',variants[0][1])]:
        digest='sha256:' + ('0'*64 if label=='wrong-hash' else hashlib.sha256(helper.canonical(helper.load_json(variants[0][1])).encode()).hexdigest())
        descriptor=dict(kind='workflow-executable',identity='fixture',codec='test-json',codecVersion=1,payloadDigest=digest,byteLength=len(raw.encode())+(1 if label=='wrong-length' else 0))
        rows.append(dict(name='native-catalog-payload/'+label,assembly='Elsa.Workflows.Publishing',type='Elsa.Workflows.Publishing.SourceCapture.NativeCatalogSourceMemberContributor',
            method='ValidateCapturedPayload',arguments=[dict(utf8=raw),dict(json=descriptor),dict(json=len(raw.encode())-1 if label=='byte-limit' else 4096)],
            expected=None,expectedException='Elsa.Workflows.Publishing.Releases.Capture.Core.ReleaseCaptureException',exceptionCode='native_catalog_captured_member_conflict'))
    return rows


if __name__ == "__main__":
    Path(__file__).with_name("additional-owner-hash-vectors.json").write_text(json.dumps(generate(), ensure_ascii=True, indent=2) + "\n", encoding="utf-8")
