import React, { useState } from 'react'

interface Props {
  onLogin: (credentials: { login: string; password: string }) => Promise<boolean>
  onSuccess: () => void
}

type State = 'idle' | 'loading' | 'error'

export default function AdminLoginForm({ onLogin, onSuccess }: Props) {
  const [login, setLogin] = useState('')
  const [password, setPassword] = useState('')
  const [state, setState] = useState<State>('idle')

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault()
    setState('loading')
    try {
      const ok = await onLogin({ login, password })
      if (ok) {
        setState('idle')
        onSuccess()
      } else {
        setState('error')
      }
    } catch {
      setState('error')
    }
  }

  return (
    <form className="upload-form" onSubmit={handleSubmit}>
      <div className="modal-title">Вход</div>
      <input
        type="text"
        placeholder="Логин"
        value={login}
        onChange={e => setLogin(e.target.value)}
        autoComplete="username"
        required
        disabled={state === 'loading'}
      />
      <input
        type="password"
        placeholder="Пароль"
        value={password}
        onChange={e => setPassword(e.target.value)}
        autoComplete="current-password"
        required
        disabled={state === 'loading'}
      />
      <button type="submit" className="upload-submit" disabled={state === 'loading'}>
        {state === 'loading' ? <span className="play-btn-spinner" /> : 'Войти'}
      </button>
      {state === 'error' && (
        <div className="upload-status error">Неверный логин или пароль</div>
      )}
    </form>
  )
}
