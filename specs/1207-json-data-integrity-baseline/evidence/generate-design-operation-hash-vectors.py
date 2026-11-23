"""Independent pins for the UTF-8 framed workflow-design operation fingerprint.

State is a JSON component of the hash-only projection. Stored state text is not rewritten.
"""
from pathlib import Path
import copy
import hashlib
import importlib.util
import json

spec = importlib.util.spec_from_file_location('canonical_fixture', Path(__file__).with_name('generate-variable-hash-vectors.py'))
helper = importlib.util.module_from_spec(spec)
spec.loader.exec_module(helper)


def framed(kind, canonical):
    return ''.join(str(len(value.encode('utf-8'))) + ':' + value
                   for value in ['elsa-design-material:v1', kind, '1', canonical])


def projections(state):
    create = dict(name='Workflow', description='René <+>', deletedAt=None, deletedReason=None,
                  isSourceOwned=False, sourceVersionId=None, stateJson=state, layout=[], activityPresentation=[])
    return [
        ('workflow.definition.create.v1', 'CreateWorkflowDefinitionRequestMaterial', create),
        ('workflow.definition.create-in-folder.v1', 'CreateWorkflowDefinitionRequestMaterial',
            dict(requestMaterial=create, folderId='folder', actorId='actor')),
        ('workflow.draft.create.v1', 'CreateDraftRequestMaterial',
            dict(workflowDefinitionId='definition', stateJson=state, layout=[], sourceVersionId=None)),
        ('workflow.version.add.v1', 'AddWorkflowDefinitionVersionRequestMaterial', dict(definitionId='definition', stateJson=state)),
        ('workflow.version.materialize.v1', 'MaterializeWorkflowDefinitionVersionRequestMaterial',
            dict(definitionId='definition', versionId='version', version='1.0.0', sourceDraftId=None, sourceCreatedAt=None, stateJson=state)),
        ('workflow.draft.replace.v1', 'UpdateDraftRequestMaterial', dict(draftId='draft', stateJson=state, layout=[], activityPresentation=[])),
        ('workflow.definition.submit.v1', 'SubmitWorkflowDefinitionRequestMaterial', dict(name='Workflow', description=None, stateJson=state)),
    ]


def generate():
    vectors = []

    def add(name, kind, material_type, payload, raw_state=None):
        canonical = helper.canonical(payload)
        frame = framed(kind, canonical)
        material = canonical
        if raw_state is not None:
            raw_projection = copy.deepcopy(payload)
            state_owner = raw_projection['requestMaterial'] if 'requestMaterial' in raw_projection else raw_projection
            state_owner['stateJson'] = '__S9_STATE_JSON_COMPONENT__'
            material = helper.canonical(raw_projection).replace(helper.quote('__S9_STATE_JSON_COMPONENT__'), raw_state)
        vectors.append(dict(name=name, operationKind=kind, materialType=material_type,
            material=material, canonical=canonical, frame=frame,
            sha256='sha256:' + hashlib.sha256(frame.encode()).hexdigest()))

    for spelling, raw in [('a', '{ "total":1.0, "text":"\\u0061" }'), ('b', '{"text":"a","total":1e0}')]:
        for kind, material_type, payload in projections(helper.load_json(raw)):
            add(kind + '-spelling-' + spelling, kind, material_type, payload, raw)
    for name, raw in [('missing', '{}'), ('null', '{"x":null}'), ('forward', '[1,2]'), ('reverse', '[2,1]'),
                      ('large-a', '9007199254740993'), ('large-b', '9007199254740992')]:
        kind, material_type, payload = projections(helper.load_json(raw))[3]
        add(name, kind, material_type, payload)
    for name, text in [('text-a', '{}'), ('text-b', ' { }')]:
        kind, material_type, payload = copy.deepcopy(projections(helper.load_json('{}'))[0])
        payload['description'] = text
        add(name, kind, material_type, payload)
    add('boolean-result', 'workflow.draft.discard.v1.result', None, True)
    return dict(version=1, owner='EfDesignSupport.FingerprintJson',
        freeze='FR-021: frozen from Elsa 4 first stable; later recipe changes require the agreed compatibility process.', vectors=vectors)


if __name__ == '__main__':
    output = Path(__file__).with_name('design-operation-hash-vectors.json')
    output.write_text(json.dumps(generate(), indent=2, ensure_ascii=True) + '\n', encoding='utf-8')
