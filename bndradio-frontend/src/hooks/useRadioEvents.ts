import { useEffect, useRef, useCallback } from 'react'

export interface RadioState {
  currentId: string
  currentTitle: string
  nextId: string | null
  nextTitle: string | null
  elapsedMs: number
  durationMs: number
  skipVotes: number
  mySkipVote: boolean
}

export interface PresenceState {
  count: number
  users: { username: string; color: string }[]
}

export interface SkipRequest {
  sessionId: string
  username: string
}

interface Handlers {
  onState: (s: RadioState) => void
  onPresence: (p: PresenceState) => void
  onReaction?: (emoji: string) => void
  onSkipRequests?: (requests: SkipRequest[]) => void
  sessionId: string
}

const RECONNECT_DELAY_MS = 2000
const MAX_RECONNECT_DELAY_MS = 30_000

export function useRadioEvents({ onState, onPresence, onReaction, onSkipRequests, sessionId }: Handlers) {
  const esRef = useRef<EventSource | null>(null)
  const reconnectDelay = useRef(RECONNECT_DELAY_MS)
  const reconnectTimer = useRef<ReturnType<typeof setTimeout> | null>(null)
  const mountedRef = useRef(true)

  const onStateRef = useRef(onState)
  const onPresenceRef = useRef(onPresence)
  const onReactionRef = useRef(onReaction)
  const onSkipRequestsRef = useRef(onSkipRequests)
  onStateRef.current = onState
  onPresenceRef.current = onPresence
  onReactionRef.current = onReaction
  onSkipRequestsRef.current = onSkipRequests

  const connect = useCallback(() => {
    if (!mountedRef.current) return

    const url = `/events?sid=${encodeURIComponent(sessionId)}`
    const es = new EventSource(url)
    esRef.current = es

    es.addEventListener('state', (e: MessageEvent) => {
      try {
        const raw = JSON.parse(e.data)
        const d = raw.cached ?? raw
        const state: RadioState = {
          currentId:    d.current?.id ?? '',
          currentTitle: d.current?.title ?? '',
          nextId:       d.next?.id ?? null,
          nextTitle:    d.next?.title ?? null,
          elapsedMs:    d.elapsedMs ?? 0,
          durationMs:   d.current?.durationMs ?? 0,
          skipVotes:    d.skipVotes ?? 0,
          mySkipVote:   raw.mySkipVote ?? false,
        }
        onStateRef.current(state)
        reconnectDelay.current = RECONNECT_DELAY_MS
      } catch { }
    })

    es.addEventListener('presence', (e: MessageEvent) => {
      try {
        const d = JSON.parse(e.data)
        onPresenceRef.current({ count: d.count ?? 0, users: d.users ?? [] })
      } catch { }
    })

    es.addEventListener('reaction', (e: MessageEvent) => {
      try {
        const d = JSON.parse(e.data)
        onReactionRef.current?.(d.emoji)
      } catch { }
    })

    es.addEventListener('skip_requests', (e: MessageEvent) => {
      try {
        const d = JSON.parse(e.data)
        onSkipRequestsRef.current?.(d.requests ?? [])
      } catch { }
    })

    es.onerror = () => {
      es.close()
      esRef.current = null
      if (!mountedRef.current) return
      reconnectTimer.current = setTimeout(() => {
        reconnectDelay.current = Math.min(reconnectDelay.current * 1.5, MAX_RECONNECT_DELAY_MS)
        connect()
      }, reconnectDelay.current)
    }
  }, [sessionId])

  useEffect(() => {
    mountedRef.current = true
    connect()
    return () => {
      mountedRef.current = false
      esRef.current?.close()
      if (reconnectTimer.current) clearTimeout(reconnectTimer.current)
    }
  }, [connect])
}
