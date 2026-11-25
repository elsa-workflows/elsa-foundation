"""Independent framed UTF-8 pins for StructuredLogAppendFingerprint.Compute."""
from pathlib import Path
import hashlib
import importlib.util
import json

spec = importlib.util.spec_from_file_location('canonical_fixture', Path(__file__).with_name('generate-variable-hash-vectors.py'))
helper = importlib.util.module_from_spec(spec)
spec.loader.exec_module(helper)


def generate():
    vectors = []
    for name, raw in [
        ('spelling-a', '{ "text":"\\u0061", "n":1.0 }'),
        ('spelling-b', '{"n":1e0,"text":"a"}'),
        ('empty', '{}'), ('present-null', '{"x":null}'),
        ('array-forward', '[1,2]'), ('array-reverse', '[2,1]'),
        ('large-a', '9007199254740993'), ('large-b', '9007199254740992'),
    ]:
        canonical = helper.canonical(helper.load_json(raw))
        parts = ['tenant-𐀀', 'scope-a', 'stream-a', 'token-1', canonical]
        frame = ''.join(str(len(value.encode('utf-16-le')) // 2) + ':' + value for value in parts)
        vectors.append(dict(name=name, payload=raw, canonical=canonical, frame=frame,
                            sha256=hashlib.sha256(frame.encode('utf-8')).hexdigest().upper()))
    items = [dict(token='token-1', payload='{"x":1.0}'), dict(token='token-2', payload='{"x":2,"nil":null}')]
    parts = ['tenant-𐀀', 'scope-a', 'stream-a']
    for item in items:
        parts.extend([item['token'], helper.canonical(helper.load_json(item['payload']))])
    frame = ''.join(str(len(value.encode('utf-16-le')) // 2) + ':' + value for value in parts)
    vectors.append(dict(name='two-items', items=items, frame=frame, sha256=hashlib.sha256(frame.encode()).hexdigest().upper()))
    Path(__file__).with_name('structured-log-hash-vectors.json').write_text(json.dumps(vectors, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    print(f'Wrote {len(vectors)} independent framed log vectors')


if __name__ == '__main__':
    generate()
