#!/usr/bin/env python3
"""Fail the existing required aggregate when a selected Studio proof is not successful.

The browser and composed UX proofs are skipped on a draft pull request (STUDIO_HEAVY is 'false'); the ready run
executes them, so a skip is only accepted for them when the heavy scope says so.
"""
import os

PROOFS = ('STUDIO_FRONTEND', 'STUDIO_BROWSER', 'STUDIO_DOTNET', 'STUDIO_UX')
HEAVY_PROOFS = ('STUDIO_BROWSER', 'STUDIO_UX')


def failures(selected, results, heavy='true'):
    if selected not in {'true', 'false'}:
        return ['Studio scope was missing or invalid']
    if heavy not in {'true', 'false'}:
        return ['Studio heavy scope was missing or invalid']
    if selected == 'false':
        return []
    return [f'{proof} did not succeed ({results.get(proof, "missing")})'
            for proof in PROOFS
            if results.get(proof) != 'success'
            and not (heavy == 'false' and proof in HEAVY_PROOFS and results.get(proof) == 'skipped')]


if __name__ == '__main__':
    errors = failures(os.environ.get('STUDIO_SELECTED'), os.environ, os.environ.get('STUDIO_HEAVY'))
    for error in errors:
        print(f'::error::{error}')
    raise SystemExit(bool(errors))
