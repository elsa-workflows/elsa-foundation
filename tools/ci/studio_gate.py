#!/usr/bin/env python3
"""Fail the existing required aggregate when a selected Studio proof is not successful.

The browser proof follows STUDIO_HEAVY. Full composed UX follows its independent
nightly/manual/opt-in scope. A selected proof must succeed.
"""
import os

PROOFS = ('STUDIO_FRONTEND', 'STUDIO_BROWSER', 'STUDIO_DOTNET', 'STUDIO_UX')


def failures(selected, results, heavy, ux):
    if selected not in {'true', 'false'}:
        return ['Studio scope was missing or invalid']
    if heavy not in {'true', 'false'}:
        return ['Studio heavy scope was missing or invalid']
    if ux not in {'true', 'false'}:
        return ['Studio UX scope was missing or invalid']
    if selected == 'false' and ux == 'false':
        return []
    required = {
        'STUDIO_FRONTEND': selected == 'true',
        'STUDIO_DOTNET': selected == 'true',
        'STUDIO_BROWSER': selected == 'true' and heavy == 'true',
        'STUDIO_UX': ux == 'true',
    }
    return [f'{proof} did not succeed ({results.get(proof, "missing")})'
            for proof in PROOFS
            if results.get(proof) != 'success'
            and not (not required[proof] and results.get(proof) == 'skipped')]


if __name__ == '__main__':
    errors = failures(os.environ.get('STUDIO_SELECTED'), os.environ, os.environ.get('STUDIO_HEAVY'),
                      os.environ.get('STUDIO_UX_SELECTED'))
    for error in errors:
        print(f'::error::{error}')
    raise SystemExit(bool(errors))
