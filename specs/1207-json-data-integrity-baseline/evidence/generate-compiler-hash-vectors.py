"""Independent SHA-256 over literal compiler-owner preimages; no production CLR code is loaded.

These recipes and hashed contracts freeze at the first stable release. A later
change requires an explicit identity/migration decision, never just regenerated pins.
"""
from pathlib import Path
import hashlib,importlib.util,json
spec=importlib.util.spec_from_file_location('canonical_fixture',Path(__file__).with_name('generate-variable-hash-vectors.py'))
helper=importlib.util.module_from_spec(spec);spec.loader.exec_module(helper)
def generate():
    result=[]
    for row in json.loads(Path(__file__).with_name('compiler-hash-inputs.json').read_text()):
        canonical=helper.canonical(helper.load_json(row['json']))
        result.append(dict(**row,canonical=canonical,sha256=hashlib.sha256(canonical.encode()).hexdigest()))
    return result
if __name__=='__main__':
    Path(__file__).with_name('compiler-hash-vectors.json').write_text(json.dumps(generate(),ensure_ascii=True,indent=2)+'\n')
