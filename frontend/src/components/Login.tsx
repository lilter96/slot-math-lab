import { useEffect, useState } from 'react';
export default function Login() {
  const [required, setRequired] = useState(false);
  const [userId, setUserId] = useState('');
  const [password, setPassword] = useState('');
  const [error, setError] = useState('');
  useEffect(() => {
    const expired = () => { setRequired(true); setError('Session expired. Sign in to recover your existing run.'); };
    window.addEventListener('slotmath:auth-required', expired);
    void fetch('/api/auth/status').then(r => r.json()).then(s => setRequired(s.required && !s.authenticated)).catch(() => {});
    return () => window.removeEventListener('slotmath:auth-required', expired);
  }, []);
  if (!required) return null;
  return <div style={{ position: 'fixed', inset: 0, zIndex: 10000, background: '#111', display: 'grid', placeItems: 'center' }}>
    <form style={{ width: 320 }} onSubmit={async e => {
      e.preventDefault(); setError('');
      try {
        const response = await fetch('/api/auth/token', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ userId, password }) });
        if (!response.ok) throw new Error('Sign-in failed');
        setPassword(''); setRequired(false); location.reload();
      } catch { setError('Unable to sign in. Check your credentials.'); }
    }}>
      <h1>Slot Math Lab</h1>
      <label htmlFor="login-user">Username</label><input className="inp" id="login-user" autoComplete="username" required value={userId} onChange={e => setUserId(e.target.value)} />
      <label htmlFor="login-password">Password</label><input className="inp" id="login-password" type="password" autoComplete="current-password" required value={password} onChange={e => setPassword(e.target.value)} />
      {error && <p role="alert">{error}</p>}
      <button className="btn" type="submit">Sign in</button>
    </form>
  </div>;
}
