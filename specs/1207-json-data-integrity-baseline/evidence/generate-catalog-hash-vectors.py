"""Independently hash the retained literal catalog-owner projection inputs.

Inputs were captured from the real CLR catalog fixture with the production descriptor
baseline. Capture is input evidence; this generator loads no CLR serializer or hasher.
Both these recipes and their complete owned contracts freeze at first stable release.
"""
from pathlib import Path
import hashlib
import importlib.util
import json

spec = importlib.util.spec_from_file_location('canonical_fixture', Path(__file__).with_name('generate-variable-hash-vectors.py'))
helper = importlib.util.module_from_spec(spec)
spec.loader.exec_module(helper)


def generate():
    inputs = json.loads(Path(__file__).with_name('catalog-hash-inputs.json').read_text(encoding='utf-8'))
    vectors = []
    for item in inputs:
        canonical = helper.canonical(helper.load_json(item['json']))
        vectors.append(dict(type=item['type'], json=item['json'], canonical=canonical,
                            sha256=hashlib.sha256(canonical.encode()).hexdigest().upper()))
    return vectors


if __name__ == '__main__':
    Path(__file__).with_name('catalog-hash-vectors.json').write_text(json.dumps(generate(), ensure_ascii=True, indent=2) + '\n', encoding='utf-8')
