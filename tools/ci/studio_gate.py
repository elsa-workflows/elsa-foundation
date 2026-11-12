#!/usr/bin/env python3
"""Fail the existing required aggregate when a selected Studio proof is not successful."""
import os

PROOFS = ('STUDIO_FRONTEND', 'STUDIO_BROWSER', 'STUDIO_DOTNET', 'STUDIO_UX')


def failures(selected, results):
    if selected not in {'true', 'false'}:
        return ['Studio scope was missing or invalid']
    if selected == 'false':
        return []
    return [f'{proof} did not succeed ({results.get(proof, "missing")})'
            for proof in PROOFS if results.get(proof) != 'success']


if __name__ == '__main__':
    errors = failures(os.environ.get('STUDIO_SELECTED'), os.environ)
    for error in errors:
        print(f'::error::{error}')
    raise SystemExit(bool(errors))
