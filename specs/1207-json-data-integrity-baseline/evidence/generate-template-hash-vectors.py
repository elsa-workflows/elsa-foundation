"""Hash retained real-owner projection inputs independently; no CLR production code is loaded.

The inputs and owned contracts freeze at the first stable release. A later change
requires an explicit identity/migration decision, never just regenerated goldens.
"""
from pathlib import Path
import hashlib, importlib.util, json
spec=importlib.util.spec_from_file_location('canonical_fixture',Path(__file__).with_name('generate-variable-hash-vectors.py'))
helper=importlib.util.module_from_spec(spec);spec.loader.exec_module(helper)
def generate():
    rows=json.loads(Path(__file__).with_name('template-hash-inputs.json').read_text())
    result=[]
    for row in rows:
        canonical=helper.canonical(helper.load_json(row['json']))
        result.append(dict(**row,canonical=canonical,sha256=hashlib.sha256(canonical.encode()).hexdigest()))
    return result
if __name__=='__main__':
    Path(__file__).with_name('template-hash-vectors.json').write_text(json.dumps(generate(),ensure_ascii=True,indent=2)+'\n')
