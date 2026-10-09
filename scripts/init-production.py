#!/usr/bin/env python3
"""Generate private deployment credentials without printing secrets."""
import base64
import hashlib
import os
from pathlib import Path
import secrets

root = Path('production-secrets')
root.mkdir(mode=0o700, exist_ok=True)
target = root / 'deployment.env'
if target.exists():
    raise SystemExit('Credentials already exist; refusing to overwrite encryption keys.')
password = secrets.token_urlsafe(32)
salt = secrets.token_bytes(16)
hash_value = hashlib.pbkdf2_hmac('sha256', password.encode(), salt, 210000)
password_hash = 'pbkdf2:210000:' + base64.b64encode(salt).decode() + ':' + base64.b64encode(hash_value).decode()
values = {
    'DOMAIN': 'localhost',
    'JWT_SECRET': secrets.token_urlsafe(48),
    'AUTH_USER': 'operator',
    'AUTH_PASSWORD_HASH': password_hash,
    'STORAGE_KEY': base64.b64encode(secrets.token_bytes(32)).decode(),
    'POSTGRES_PASSWORD': secrets.token_urlsafe(32),
}
target.write_text(''.join(f'{key}={value}\n' for key, value in values.items()))
os.chmod(target, 0o600)
password_path = root / 'operator-password.txt'
password_path.write_text(password + '\n')
os.chmod(password_path, 0o600)
print('Created production-secrets/deployment.env and operator-password.txt. Keep both private and backed up.')
