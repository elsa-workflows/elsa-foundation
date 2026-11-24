"""Independent additional owner recipes. No production serializer or hasher is invoked.

Every pinned recipe freezes at the first stable release; subsequent changes need
an explicit identity/migration decision.
"""
from pathlib import Path
import base64
import hashlib
import importlib.util
import json
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
        [{"objectArray": scalars}], [dict(representation="clr", value=base64.b64encode(value.encode("utf-16-le")).decode()) for value in scalars])
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
        if isinstance(value, str): return base64.b64encode(value.encode("utf-16-le")).decode()
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
    return rows


if __name__ == "__main__":
    Path(__file__).with_name("additional-owner-hash-vectors.json").write_text(json.dumps(generate(), ensure_ascii=True, indent=2) + "\n", encoding="utf-8")
