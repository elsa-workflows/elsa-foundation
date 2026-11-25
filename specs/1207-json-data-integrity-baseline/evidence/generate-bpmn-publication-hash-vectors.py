"""Independent authored BPMN publication recipes, frozen from the first stable release."""
from pathlib import Path
import hashlib
import importlib.util
import json

spec = importlib.util.spec_from_file_location("canonical_fixture", Path(__file__).with_name("generate-variable-hash-vectors.py"))
helper = importlib.util.module_from_spec(spec)
spec.loader.exec_module(helper)


def generate():
    publication = helper.canonical(["", "author", "operation"])
    rows = []
    for name, action, slot, revision in [
        ("omitted", None, None, None), ("replace", 0, None, None),
        ("named-slot", 0, "Review", None), ("side-by-side", 1, None, None),
        ("revision-zero", None, None, 0),
    ]:
        value = dict(formatVersion=1, definitionId="process-definition", versionId="version-1",
                     actorId="author", tenantId=None, requestId="operation", intentProvided=action is not None,
                     action=action, slotName=slot, expectedSlotRevision=revision, expectedPublicationId=None)
        canonical = helper.canonical(helper.load_json(json.dumps(value)))
        rows.append(dict(name=name, action=action, slot=slot, revision=revision,
                         canonical=canonical, expected=hashlib.sha256(canonical.encode()).hexdigest(),
                         publicationCanonical=publication,
                         publicationId="bpmn-publication:" + hashlib.sha256(publication.encode()).hexdigest()))
    return rows


if __name__ == "__main__":
    Path(__file__).with_name("bpmn-publication-hash-vectors.json").write_text(
        json.dumps(generate(), indent=2) + "\n", encoding="utf-8")
