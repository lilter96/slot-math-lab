#!/usr/bin/env python3
"""Publish the researched definitions to the native catalogue, with a drift check."""
import argparse
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SOURCE = ROOT / 'docs/slot-math-metrics.catalog.json'
TARGET = ROOT / 'frontend/src/lib/measurements/catalog.generated.json'
COVERAGE = ROOT / 'docs/verification/metrics-coverage.json'
NATIVE_COVERAGE = ROOT / 'frontend/src/lib/measurements/implementation.generated.json'

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--check', action='store_true')
    args = parser.parse_args()
    definitions = json.loads(SOURCE.read_text())
    ids = [m[0] for family in definitions['families'] for m in family['metrics']]
    if len(ids) != 159 or len(ids) != len(set(ids)):
        raise SystemExit('The reviewed catalogue must contain 159 unique definitions.')
    coverage = json.loads(COVERAGE.read_text())
    rows = coverage['metrics']
    if len(rows) != len(ids) or {r['id'] for r in rows} != set(ids):
        raise SystemExit('Every catalogue definition requires exactly one reviewed implementation scope.')
    for row in rows:
        if row['status'] not in coverage['meaning'] or len(row['scope']) < 30:
            raise SystemExit(f"Missing implementation status/scope: {row['id']}")
        if not row['implementation'] or not row['evidence']:
            raise SystemExit(f"Missing implementation/evidence traceability: {row['id']}")
        for path in row['implementation'] + row['evidence']:
            if not (ROOT / path).is_file():
                raise SystemExit(f"Missing reviewed source/fixture: {path}")
    native = json.dumps({r['id']: {'status': r['status'], 'scope': r['scope']} for r in rows}, indent=2) + '\n'
    if args.check:
        if not TARGET.exists() or TARGET.read_bytes() != SOURCE.read_bytes():
            raise SystemExit('Native metric definitions have drifted; run scripts/verification/metrics_catalogue.py.')
        if not NATIVE_COVERAGE.exists() or NATIVE_COVERAGE.read_text() != native:
            raise SystemExit('Native implementation scopes have drifted; run scripts/verification/metrics_catalogue.py.')
    else:
        TARGET.write_bytes(SOURCE.read_bytes())
        NATIVE_COVERAGE.write_text(native)
    print(f'{len(ids)} unique metric definitions; native catalogue and implementation scopes verified.')

if __name__ == '__main__':
    main()
