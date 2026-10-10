#!/usr/bin/env python3
"""Measure bounded collector costs without starting API workers or changing quotas.

Run the same harness against each candidate Core project. Compare complete
snapshot hashes as well as warm medians; this is not a whole-game benchmark.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import statistics
import subprocess
import tempfile
from xml.sax.saxutils import escape

ROOT = Path(__file__).resolve().parents[2]
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--core-project', type=Path, default=ROOT / 'backend/SlotMath.Core/SlotMath.Core.csproj')
parser.add_argument('--output', type=Path, required=True)
parser.add_argument('--rounds', type=int, default=200000)
parser.add_argument('--warmup', type=int, default=20000)
parser.add_argument('--repeats', type=int, default=7)
args = parser.parse_args()
if not args.core_project.is_file() or min(args.rounds, args.warmup, args.repeats) <= 0:
    parser.error('A Core project and positive workload parameters are required.')
with tempfile.TemporaryDirectory(prefix='slotmath-cohort-bench-') as directory:
    temp = Path(directory)
    # Core already exposes its collector to this test assembly. Use that same
    # test surface in an isolated executable, without widening production APIs.
    temp.joinpath('Benchmark.csproj').write_text(
        '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType>'
        '<TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable>'
        '<AssemblyName>SlotMath.Core.Tests</AssemblyName></PropertyGroup><ItemGroup>'
        f'<ProjectReference Include="{escape(str(args.core_project.resolve()), {chr(34): "&quot;"})}" />'
        '</ItemGroup></Project>')
    temp.joinpath('Program.cs').write_text(Path(__file__).with_name('MeasurementCohortBenchmark.cs').read_text())
    output = temp / 'report.json'
    result = subprocess.run(['dotnet', 'run', '-c', 'Release', '--project', str(temp / 'Benchmark.csproj'), '--', str(output),
                    str(args.rounds), str(args.warmup), str(args.repeats)], env=os.environ, check=False,
                   stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
    if result.returncode:
        raise SystemExit(result.stdout.decode(errors='replace')[-4000:])
    report = json.loads(output.read_text())
for scenario in report['report']:
    evidence = json.dumps(scenario.pop('evidence'), sort_keys=True, separators=(',', ':')).encode()
    scenario['evidenceSha256'] = hashlib.sha256(evidence).hexdigest()
    scenario['medianElapsedMs'] = statistics.median(point['elapsedMs'] for point in scenario['timing'])
    scenario['medianAllocatedBytes'] = statistics.median(point['allocatedBytes'] for point in scenario['timing'])
    print(f"{scenario['name']}: {scenario['medianElapsedMs']:.1f} ms, {scenario['medianAllocatedBytes']} bytes")
report['harnessSha256'] = hashlib.sha256(Path(__file__).with_name('MeasurementCohortBenchmark.cs').read_bytes()).hexdigest()
args.output.write_text(json.dumps(report, indent=2) + '\n')
