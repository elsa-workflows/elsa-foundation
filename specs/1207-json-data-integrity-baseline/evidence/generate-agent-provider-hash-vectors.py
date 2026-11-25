"""Independent fixed capability-array preimages for the actual connection model providers."""
import hashlib
import importlib.util
import json
from pathlib import Path

spec = importlib.util.spec_from_file_location('canonical_fixture', Path(__file__).with_name('generate-variable-hash-vectors.py'))
helper = importlib.util.module_from_spec(spec)
spec.loader.exec_module(helper)

profiles = {
    'anthropic': 'anthropic/12.32.0;messages/2023-06-01;meai/10.10.1;http1.1;api-key;retries0;redirects0;stream1',
    'azure-openai': 'azure-openai/2.1.0;api/2024-10-21;openai/2.14.0;meai/10.10.1;http1.1;api-key;retries0;redirects0;stream1',
    'openai': 'openai/2.14.0;chat-completions/v1;meai/10.10.1;http1.1;bearer;retries0;redirects0;stream1',
}
rows = []
for vendor, profile in profiles.items():
    for label, tokens, temperature, hosts in [
        ('base', 8192, True, ['provider.fixture.invalid']),
        ('token-limit', 8191, True, ['provider.fixture.invalid']),
        ('host-order-a', 8192, True, ['secondary.fixture.invalid', 'provider.fixture.invalid']),
        ('host-order-b', 8192, True, ['provider.fixture.invalid', 'secondary.fixture.invalid']),
        ('temperature', 8192, False, ['provider.fixture.invalid']),
    ]:
        preimage = [vendor, profile, 'elsa.agent.structured-object/1', 'https://provider.fixture.invalid/',
                    'fixture-model', tokens, True, temperature, 'fixed', sorted(hosts)]
        canonical = helper.canonical(helper.load_json(json.dumps(preimage)))
        rows.append(dict(vendor=vendor, name=label, maximumOutputTokens=tokens, temperatureSupported=temperature,
                         hosts=hosts, canonical=canonical, expected=hashlib.sha256(canonical.encode()).hexdigest().upper()))
Path(__file__).with_name('agent-provider-hash-vectors.json').write_text(json.dumps(rows, indent=2) + '\n', encoding='utf-8')
