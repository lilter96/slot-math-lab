#!/usr/bin/env python3
"""Verify the benchmark CLI reports cap evidence in original stake units."""
import argparse
import json
from pathlib import Path
import subprocess
import tempfile

ROOT = Path(__file__).resolve().parents[2]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--check', action='store_true', required=True)
    parser.add_argument('--assembly', type=Path, default=ROOT / 'backend/SlotMath.Benchmarks/bin/Release/net10.0/SlotMath.Benchmarks.dll')
    args = parser.parse_args()
    with tempfile.TemporaryDirectory(prefix='slotmath-benchmark-cap-') as directory:
        report = Path(directory) / 'report.json'
        subprocess.run([
            'dotnet', str(args.assembly), '--graph',
            str(ROOT / 'backend/SlotMath.Core.Tests/TestData/Benchmarks/scaled-cap-control.json'),
            '--rounds', '10000', '--warmup', '1000', '--workers', '1', '--repeats', '1',
            '--report', str(report),
        ], check=True)
        data = json.loads(report.read_text())
    stats = data['measurements'][0]
    # Independent two-point law: half of the draws pay 1/2, half pay 1.
    # The published fixed PRNG stream pins 5,038 full-stake payouts here.
    expected_mean = (5038 + (10000 - 5038) / 2) / 10000
    checks = {
        'fractional graph exercises scaling': int(data['winScale']) > 1,
        'all requested rounds completed': stats['count'] == 10000,
        'cap remains in stake units': stats['maxWinCap'] == 1,
        'full-stake payouts count as cap hits': stats['capHits'] == 5038,
        'scaled payouts retain their pinned mean': abs(stats['rtp'] - expected_mean) < 1e-12,
        'maximum remains in stake units': stats['maxWin'] == 1,
    }
    failures = [name for name, passed in checks.items() if not passed]
    if failures:
        raise SystemExit('Benchmark cap regression: ' + '; '.join(failures))
    print('Scaled benchmark cap: 10,000 rounds, 5,038 cap hits, cap 1×; verified.')


if __name__ == '__main__':
    main()
