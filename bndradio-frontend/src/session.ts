const KEY = 'bndradio_session_id'
const NAME_KEY = 'bndradio_username'
const COLOR_KEY = 'bndradio_usercolor'

export function getSessionId(): string {
  let id = localStorage.getItem(KEY)
  if (!id) {
    id = crypto.randomUUID()
    localStorage.setItem(KEY, id)
  }
  return id
}

export const COLORS = ['#30d158', '#ff453a', '#0a84ff', '#ffd60a', '#bf5af2', '#ff9f0a', '#5ac8fa', '#ff375f']

export function getUsername(): string {
  let name = localStorage.getItem(NAME_KEY)
  if (!name) {
    const num = Math.floor(Math.random() * 9000) + 1000
    name = `slave#${num}`
    localStorage.setItem(NAME_KEY, name)
  }
  return name
}

export function getUserColor(): string {
  let color = localStorage.getItem(COLOR_KEY)
  if (!color) {
    color = COLORS[Math.floor(Math.random() * COLORS.length)]
    localStorage.setItem(COLOR_KEY, color)
  }
  return color
}

export function setUserColor(color: string): void {
  localStorage.setItem(COLOR_KEY, color)
}

export function setUsername(name: string): string {
  const trimmed = name.trim().slice(0, 32) || getUsername()
  localStorage.setItem(NAME_KEY, trimmed)
  return trimmed
}
