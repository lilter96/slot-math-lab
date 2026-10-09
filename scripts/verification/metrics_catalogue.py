#!/usr/bin/env python3
"""Publish the researched definitions to the native catalogue, with a drift check."""
import argparse
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SOURCE = ROOT / 'docs/slot-math-metrics.catalog.json'
TARGET = ROOT / 'frontend/src/lib/measurements/catalog.generated.json'

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--check', action='store_true')
    args = parser.parse_args()
    definitions = json.loads(SOURCE.read_text())
    ids = [m[0] for family in definitions['families'] for m in family['metrics']]
    if len(ids) != 159 or len(ids) != len(set(ids)):
        raise SystemExit('The reviewed catalogue must contain 159 unique definitions.')
    if args.check:
        if not TARGET.exists() or TARGET.read_bytes() != SOURCE.read_bytes():
            raise SystemExit('Native metric definitions have drifted; run scripts/verification/metrics_catalogue.py.')
    else:
        TARGET.write_bytes(SOURCE.read_bytes())
    print(f'{len(ids)} unique metric definitions; native catalogue verified.')

if __name__ == '__main__':
    main()
