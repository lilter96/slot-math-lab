#!/usr/bin/env python3
"""Check product paths and text for retired third-party branding."""
from pathlib import Path
import re
import subprocess

root = Path(__file__).resolve().parents[2]
tokens = ['dog' + r'[ _-]?' + 'house', 'prag' + 'matic']
pattern = re.compile('|'.join(tokens), re.IGNORECASE)
files = subprocess.check_output(['git', 'ls-files', '--cached', '--others', '--exclude-standard', '-z'], cwd=root).decode().split('\0')
failures = []
for name in set(files):
    if not name:
        continue
    file = root / name
    if not file.is_file():
        continue
    if pattern.search(name):
        failures.append(name + ': path')
    try:
        text = file.read_text()
    except UnicodeDecodeError:
        continue
    for index, line in enumerate(text.splitlines(), 1):
        if pattern.search(line):
            failures.append(f'{name}:{index}')
if failures:
    raise SystemExit('\n'.join(sorted(failures)))
print('Product branding scan: zero matches.')
