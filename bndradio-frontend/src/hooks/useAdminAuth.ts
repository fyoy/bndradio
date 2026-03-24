import { useState, useCallback } from 'react'

const TOKEN_KEY = 'bndradio_admin_token'

function getTokenExpiry(token: string): number | null {
  try {
    const payload = JSON.parse(atob(token.split('.')[1]))
    return typeof payload.exp === 'number' ? payload.exp : null
  } catch {
    return null
  }
}

function isTokenValid(token: string | null): boolean {
  if (!token) return false
  const exp = getTokenExpiry(token)
  if (exp === null) return false
  return exp > Date.now() / 1000
}

function loadToken(): string | null {
  try {
    const token = localStorage.getItem(TOKEN_KEY)
    return isTokenValid(token) ? token : null
  } catch {
    return null
  }
}

export interface AdminAuthState {
  isAdmin: boolean
  login: (credentials: { login: string; password: string }) => Promise<boolean>
  logout: () => void
  getAuthHeader: () => Record<string, string>
}

export function useAdminAuth(): AdminAuthState {
  const [token, setToken] = useState<string | null>(loadToken)

  const isAdmin = isTokenValid(token)

  const login = useCallback(async (credentials: { login: string; password: string }): Promise<boolean> => {
    const res = await fetch('/auth/login', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(credentials),
    })
    if (!res.ok) return false
    const data = await res.json()
    const newToken: string = data.token
    try { localStorage.setItem(TOKEN_KEY, newToken) } catch { /* ignore */ }
    setToken(newToken)
    return true
  }, [])

  const logout = useCallback(() => {
    try { localStorage.removeItem(TOKEN_KEY) } catch { /* ignore */ }
    setToken(null)
  }, [])

  const getAuthHeader = useCallback((): Record<string, string> => {
    const t = loadToken()
    return t ? { Authorization: `Bearer ${t}` } : {}
  }, [])

  return { isAdmin, login, logout, getAuthHeader }
}
