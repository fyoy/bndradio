// Persistent browser identity helpers stored in localStorage.
// sessionId — random UUID, stable across page reloads.
// username  — display name shown in the presence panel (default: slave#NNNN).
const KEY      = 'bndradio_session_id'
const NAME_KEY = 'bndradio_username'

export function getSessionId(): string {
  let id = localStorage.getItem(KEY)
  if (!id) {
    id = crypto.randomUUID()
    localStorage.setItem(KEY, id)
  }
  return id
}

export function getUsername(): string {
  let name = localStorage.getItem(NAME_KEY)
  if (!name) {
    const num = Math.floor(Math.random() * 9000) + 1000
    name = `slave#${num}`
    localStorage.setItem(NAME_KEY, name)
  }
  return name
}

export function setUsername(name: string): string {
  const trimmed = name.trim().slice(0, 32) || getUsername()
  localStorage.setItem(NAME_KEY, trimmed)
  return trimmed
}
