"""Independent Python SHA-256 pins for the existing VariableValueDigest recipe.

No production serializer/hash code is invoked. Input spelling, canonical preimages
and outputs are retained; the recipe freezes with Elsa 4's first stable release.
"""
from pathlib import Path
import copy
import hashlib
import json


def quote(value):
    result = '"'
    for char in value:
        code = ord(char)
        if 0xd800 <= code <= 0xdfff:
            raise ValueError('Raw JSON fixture strings cannot contain unpaired surrogate code units')
        if char == '\\':
            result += '\\\\'
        elif code in {8, 9, 10, 12, 13}:
            result += '\\' + {8: 'b', 9: 't', 10: 'n', 12: 'f', 13: 'r'}[code]
        elif code < 32 or code >= 127 or code in {34, 38, 39, 43, 60, 62, 96}:
            if code > 0xffff:
                code -= 0x10000
                result += '\\u%04X\\u%04X' % (0xd800 + (code >> 10), 0xdc00 + (code & 1023))
            else:
                result += '\\u%04X' % code
        else:
            result += char
    return result + '"'


class Number(str):
    pass


def load_json(raw):
    def unique_members(pairs):
        result = {}
        for key, value in pairs:
            if key in result:
                raise ValueError('Duplicate JSON names require a token-preserving fixture generator')
            result[key] = value
        return result
    value = json.loads(raw, parse_int=Number, parse_float=Number, object_pairs_hook=unique_members)
    canonical(value)  # Validate raw string/name code units before any output is written.
    return value


def canonical(value):
    if isinstance(value, Number):
        mantissa, *exponent = value.lower().split('e')
        power = int(exponent[0]) if exponent else 0
        negative = mantissa.startswith('-')
        mantissa = mantissa.lstrip('-')
        fraction = len(mantissa.split('.')[1]) if '.' in mantissa else 0
        digits = mantissa.replace('.', '').lstrip('0')
        if not digits:
            return '0'
        coefficient = digits.rstrip('0')
        return ('-' if negative else '') + coefficient + 'e' + str(power + len(digits) - len(coefficient) - fraction)
    if value is None:
        return 'null'
    if isinstance(value, bool):
        return str(value).lower()
    if isinstance(value, str):
        return quote(value)
    if isinstance(value, list):
        return '[' + ','.join(canonical(item) for item in value) + ']'
    return '{' + ','.join(quote(key) + ':' + canonical(value[key]) for key in sorted(value, key=lambda key: key.encode('utf-8'))) + '}'


def generate():
    cases = [
        ('absent', 0, None), ('explicit-null', 1, None), ('inline-null', 2, 'null'),
        ('spelling-a', 2, '{ "text":"\\u0061", "n":1.0 }'),
        ('spelling-b', 2, '{"n":1e0,"text":"a"}'),
        ('empty', 2, '{}'), ('present-null', 2, '{"x":null}'),
        ('array-forward', 2, '[1,2]'), ('array-reverse', 2, '[2,1]'),
        ('large-a', 2, '9007199254740993'), ('large-b', 2, '9007199254740992'),
        ('fraction', 2, '0.10000000000000000000000000001'),
        ('negative-zero', 2, '-0.000e999999999999999999999'),
        ('zero', 2, '0'), ('unbounded-exponent', 2, '1.2300e999999999999999999999'),
        ('unicode', 2, '{"text":"René <雪>","items":[1,null]}'),
    ]
    result = []
    for name, presence, raw in cases:
        payload = {'presence': Number(str(presence))}
        if raw is not None:
            payload['inline'] = load_json(raw)
        preimage = canonical(payload)
        result.append(dict(name=name, presence=presence, json=raw, canonical=preimage,
                           sha256=hashlib.sha256(preimage.encode('utf-8')).hexdigest()))
    descriptor = dict(alias='String', collectionKind='Single', schema=None, schemaVersion=None)
    plan = dict(schemaVersion=Number('1'), sourceRepresentation='TextValue', sourceType=descriptor,
                targetType=descriptor, mode='Auto', operation='Identity', profile=None,
                limits=dict(maxDepth=Number('64'), maxCollectionItems=Number('10000'), maxPayloadBytes=Number('1048576')), options={})
    plan['fingerprint'] = 'sha256:' + hashlib.sha256(canonical(plan).encode()).hexdigest()
    # The existing pin describes explicit JSON-null schemas. An absent CLR Schema is omitted
    # by ValueTypeDescriptor's member contract, while the plan's own WriteType preimage is unchanged.
    absent_schema_plan = copy.deepcopy(plan)
    for name in ['sourceType', 'targetType']:
        absent_schema_plan[name].pop('schema', None)
    markers = [
        ('external', Number('2'), dict(external=dict(storageProfile='blob', locator='object-1', metadata={'OwnerKey': 'René'}))),
        ('withheld-policy', Number('3'), dict(withheld=dict(kind='PolicyRequiresEncryption'))),
        ('withheld-secret-plan', Number('3'), dict(withheld=dict(kind='SecretReference',
             secret=dict(name='api-key', typeName='String', scope='tenant-a'), conversionPlan=plan))),
        ('withheld-secret-plan-absent-schema', Number('3'), dict(withheld=dict(kind='SecretReference',
             secret=dict(name='api-key', typeName='String', scope='tenant-a'), conversionPlan=absent_schema_plan))),
    ]
    for name, presence, marker in markers:
        preimage = canonical(dict(presence=presence, **marker))
        result.append(dict(name=name, presence=int(presence), json=None, canonical=preimage,
                           sha256=hashlib.sha256(preimage.encode()).hexdigest()))
    destination = Path(__file__).with_name('variable-value-hash-vectors.json')
    destination.write_text(json.dumps(result, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    print(f'Wrote {len(result)} independent variable digest vectors')


if __name__ == '__main__':
    generate()
