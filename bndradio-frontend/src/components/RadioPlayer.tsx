import React, { useRef, useState, useEffect, useCallback } from 'react'
import NowPlaying from './NowPlaying'
import VolumeControl from './VolumeControl'
import SongSuggestion from './SongSuggestion'
import SongUpload from './SongUpload'
import OceanBackground from './OceanBackground'
import { getSessionId, getUsername, getUserColor, setUsername, setUserColor, COLORS } from '../session'
import DropZone from './DropZone'
import { useRadioEvents } from '../hooks/useRadioEvents'
import type { RadioState, PresenceState } from '../hooks/useRadioEvents'
import { useAdminAuth } from '../hooks/useAdminAuth'
import AdminLoginForm from './AdminLoginForm'

const STREAM_URL = '/stream'

interface QueueState {
  currentId: string
  currentTitle: string
  nextId: string | null
  nextTitle: string | null
  elapsedMs: number
  durationMs: number
}

export default function RadioPlayer() {
  const audioRef = useRef<HTMLAudioElement>(null)
  const [playing, setPlaying] = useState(false)
  const [buffering, setBuffering] = useState(false)
  const [uploadOpen, setUploadOpen] = useState(false)
  const [loginOpen, setLoginOpen] = useState(false)
  const [progress, setProgress] = useState(0)
  const [catalogKey, setCatalogKey] = useState(0)
  const [queueState, setQueueState] = useState<QueueState | null>(null)
  const [onlineCount, setOnlineCount] = useState<number | null>(null)
  const [onlineUsers, setOnlineUsers] = useState<{ username: string; color: string }[]>([])
  const [showUsersTooltip, setShowUsersTooltip] = useState(false)
  const [skipVotes, setSkipVotes] = useState(0)
  const [mySkipVote, setMySkipVote] = useState(false)
  const [skipCooldown, setSkipCooldown] = useState(0)
  const [showColorPicker, setShowColorPicker] = useState(false)
  const [userColor, setUserColorState] = useState(getUserColor)
  const [trackFlash, setTrackFlash] = useState(false)
  const [skipShake, setSkipShake] = useState(false)
  const [timeOverride, setTimeOverride] = useState<'day' | 'night' | undefined>(undefined)
  const intentionalStopRef = useRef(false)
  const prevTrackIdRef = useRef<string>('')

  const sessionId = getSessionId()
  const [username, setUsernameState] = useState(getUsername)
  const { isAdmin, login, logout, getAuthHeader } = useAdminAuth()
  const handleLogout = () => {
    logout()
    setLoginOpen(false)
  }
  const [editingName, setEditingName] = useState(false)
  const [nameInput, setNameInput] = useState('')
  const nameInputRef = useRef<HTMLInputElement>(null)
const startEditName = () => {
    setNameInput(username)
    setEditingName(true)
    setTimeout(() => nameInputRef.current?.select(), 0)
  }

  const commitName = () => {
    if (!editingName) return
    const saved = setUsername(nameInput)
    setUsernameState(saved)
    setEditingName(false)
    setTimeout(() => {
      fetch('/presence/announce', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ text: saved }),
      }).catch(() => {})
    }, 0)
  }

  const lastSyncRef = useRef<{ elapsedMs: number; at: number }>({ elapsedMs: 0, at: Date.now() })
  const lastStateHashRef = useRef<string>('')

  useEffect(() => {
    if (!uploadOpen) return
    const onKey = (e: KeyboardEvent) => { if (e.key === 'Escape') setUploadOpen(false) }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [uploadOpen])

  useEffect(() => {
    fetch('/queue/state', { headers: { 'X-Session-Id': sessionId } })
      .then(r => r.ok ? r.json() : null)
      .then(data => {
        if (!data) return
        lastSyncRef.current = { elapsedMs: data.elapsedMs, at: Date.now() }
        setQueueState({
          currentId:    data.current?.id ?? '',
          currentTitle: data.current?.title ?? '',
          nextId:       null,
          nextTitle:    null,
          elapsedMs:    data.elapsedMs,
          durationMs:   data.current?.durationMs ?? 0,
        })
        setSkipVotes(data.skipVotes ?? 0)
        setMySkipVote(data.mySkipVote ?? false)
      })
      .catch(() => {})
  }, [])

  const pingPresence = useCallback(async (name: string, color: string) => {
    try {
      const res = await fetch('/presence/ping', {
        method: 'POST',
        headers: { 'X-Session-Id': sessionId, 'Content-Type': 'application/json' },
        body: JSON.stringify({ username: name, color }),
      })
      if (res.ok) {
        const data = await res.json()
        setOnlineCount(data.count)
        setOnlineUsers(data.users ?? [])
      }
    } catch { /* ignore */ }
  }, [sessionId])

  useEffect(() => {
    pingPresence(username, userColor)
    const id = setInterval(() => pingPresence(username, userColor), 20_000)
    return () => clearInterval(id)
  }, [sessionId, username, userColor, pingPresence])

  useEffect(() => {
    if (skipCooldown <= 0) return
    const id = setInterval(() => setSkipCooldown(s => Math.max(0, s - 1)), 1000)
    return () => clearInterval(id)
  }, [skipCooldown])

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      const tag = (e.target as HTMLElement).tagName
      if (tag === 'INPUT' || tag === 'TEXTAREA') return
      if (e.code === 'Space') {
        e.preventDefault()
        if (playing) stopPlayback()
        else startPlayback()
      }
      if (e.code === 'ArrowUp') {
        e.preventDefault()
        const audio = audioRef.current
        if (audio) audio.volume = Math.min(1, audio.volume + 0.1)
      }
      if (e.code === 'ArrowDown') {
        e.preventDefault()
        const audio = audioRef.current
        if (audio) audio.volume = Math.max(0, audio.volume - 0.1)
      }
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [playing])

  useEffect(() => {
    if (!('mediaSession' in navigator)) return
    if (queueState?.currentTitle) {
      navigator.mediaSession.metadata = new MediaMetadata({
        title: queueState.currentTitle,
        artist: 'bndradio',
        album: 'Live Stream',
      })
    }
    navigator.mediaSession.setActionHandler('play', () => startPlayback())
    navigator.mediaSession.setActionHandler('pause', () => stopPlayback())
    navigator.mediaSession.setActionHandler('stop', () => stopPlayback())
  }, [queueState?.currentTitle, playing])

  const prevNotifTitleRef = useRef<string>('')
  useEffect(() => {
    if (!queueState?.currentTitle) return
    if (queueState.currentTitle === prevNotifTitleRef.current) return
    prevNotifTitleRef.current = queueState.currentTitle
    if (!playing) return
    if (Notification.permission === 'granted') {
      new Notification('bndradio', { body: queueState.currentTitle, silent: true })
    }
  }, [queueState?.currentTitle, playing])

  useEffect(() => {
    if (Notification.permission === 'default') {
      Notification.requestPermission().catch(() => {})
    }
  }, [])

  useEffect(() => {
    const favicon = document.querySelector<HTMLLinkElement>('link[rel="icon"]')
    if (!favicon) return
    const icon = playing ? '🍑' : '💤'
    favicon.href = `data:image/svg+xml,<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 32 32'><text y='28' font-size='28'>${icon}</text></svg>`
  }, [playing])

const skipVotesRef = useRef(skipVotes)
  skipVotesRef.current = skipVotes

  useRadioEvents({
    sessionId,
    onState: useCallback((s: RadioState) => {
      const prevSkipVotes = skipVotesRef.current
      setSkipVotes(s.skipVotes)
      setMySkipVote(s.mySkipVote)

      if (s.skipVotes > prevSkipVotes) {
      }

      const hash = `${s.currentId}|${s.nextId}`
      if (hash !== lastStateHashRef.current) {
        lastStateHashRef.current = hash
        setCatalogKey(k => k + 1)
      }

      if (s.currentId && s.currentId !== prevTrackIdRef.current) {
        if (prevTrackIdRef.current !== '') {
          setTrackFlash(true)
          setTimeout(() => setTrackFlash(false), 100)
        }
        prevTrackIdRef.current = s.currentId
      }

      lastSyncRef.current = { elapsedMs: s.elapsedMs, at: Date.now() }
      setQueueState({
        currentId:    s.currentId,
        currentTitle: s.currentTitle,
        nextId:       s.nextId,
        nextTitle:    s.nextTitle,
        elapsedMs:    s.elapsedMs,
        durationMs:   s.durationMs,
      })
    }, []),
    onPresence: useCallback((p: PresenceState) => {
      setOnlineCount(p.count)
      setOnlineUsers(p.users)
    }, []),
    onReaction: useCallback((_emoji: string) => {}, []),
  })

  // Local progress tick
  useEffect(() => {
    const id = setInterval(() => {
      if (!queueState || queueState.durationMs <= 0) return
      const { elapsedMs, at } = lastSyncRef.current
      const elapsed = Math.min(elapsedMs + (Date.now() - at), queueState.durationMs)
      setProgress((elapsed / queueState.durationMs) * 100)
    }, 500)
    return () => clearInterval(id)
  }, [queueState])

  const startPlayback = useCallback(async () => {
    const audio = audioRef.current
    if (!audio) return
    intentionalStopRef.current = false
    setBuffering(true)
    audio.src = `${STREAM_URL}?t=${Date.now()}`
    try {
      await audio.play()
      setPlaying(true)
      
    } catch (e) {
      console.error('Playback failed:', e)
      setPlaying(false)
      
      setBuffering(false)
    }
  }, [])

  const stopPlayback = useCallback(() => {
    const audio = audioRef.current
    if (!audio) return
    intentionalStopRef.current = true
    audio.pause()
    audio.src = ''
    setPlaying(false)
    
    setBuffering(false)
  }, [])

  useEffect(() => {
    const audio = audioRef.current
    if (!audio) return
    const onError = () => {
      
      setBuffering(false)
      if (intentionalStopRef.current) {
        intentionalStopRef.current = false
        return
      }
      if (audioRef.current && audioRef.current.src) {
        setTimeout(() => {
          if (!audioRef.current || intentionalStopRef.current) return
          setBuffering(true)
          audioRef.current.src = `${STREAM_URL}?t=${Date.now()}`
          audioRef.current.play().catch(() => { setPlaying(false); setBuffering(false) })
        }, 500)
      } else {
        setPlaying(false)
      }
    }
    const onPlaying = () => { setPlaying(true); setBuffering(false) }
    const onWaiting = () => { if (playing) setBuffering(true) }
    const onCanPlay = () => 
    audio.addEventListener('error',   onError)
    audio.addEventListener('playing', onPlaying)
    audio.addEventListener('waiting', onWaiting)
    audio.addEventListener('canplay', onCanPlay)
    return () => {
      audio.removeEventListener('error',   onError)
      audio.removeEventListener('playing', onPlaying)
      audio.removeEventListener('waiting', onWaiting)
      audio.removeEventListener('canplay', onCanPlay)
    }
  }, [playing])

  const valid = queueState && queueState.currentId !== '00000000-0000-0000-0000-000000000000'

  return (
    <div
      className="app-layout"
      style={{ '--accent': userColor } as React.CSSProperties}
    >
      <OceanBackground timeOverride={timeOverride} />
      <DropZone onUploaded={() => setCatalogKey(k => k + 1)} disabled={uploadOpen} isAdmin={isAdmin} getAuthHeader={getAuthHeader} />
      <audio ref={audioRef} preload="none" style={{ display: 'none' }} />

      <div className="emoji-float-layer" aria-hidden="true">
      </div>

      <main className="player-stage">
        <div className={`stage-row${playing ? ' stage-row--playing' : ''}${trackFlash ? ' stage-row--flash' : ''}${skipShake ? ' stage-row--shake' : ''}`}>

          <div className="player-wrap">
            <div
              className="player-card"
              style={{ '--progress': `${progress}%` } as React.CSSProperties}
            >
              <div className="card-topbar">
                <span className="user-badge">
                  <span className="user-badge-icon-wrap">
                    {isAdmin && <span className="admin-crown">👑</span>}
                    <span
                      className="user-badge-icon"
                      style={{ background: userColor }}
                      onClick={() => setShowColorPicker(p => !p)}
                      title="Выбрать цвет"
                    >
                      {username[0].toUpperCase()}
                    </span>
                  </span>
                  {showColorPicker && (
                    <div className="color-picker">
                      {COLORS.map(c => (
                        <button
                          key={c}
                          className={`color-swatch${c === userColor ? ' active' : ''}`}
                          style={{ background: c }}
                          onClick={() => {
                            setUserColor(c)
                            setUserColorState(c)
                            setShowColorPicker(false)
                            pingPresence(username, c)
                          }}
                        />
                      ))}
                    </div>
                  )}
                  {editingName ? (
                    <input
                      ref={nameInputRef}
                      className="user-badge-input"
                      value={nameInput}
                      onChange={e => setNameInput(e.target.value)}
                      onBlur={commitName}
                      onKeyDown={e => {
                        if (e.key === 'Enter') { e.preventDefault(); commitName() }
                        if (e.key === 'Escape') setEditingName(false)
                      }}
                      maxLength={32}
                      autoFocus
                    />
                  ) : (
                    <span className="user-badge-name" onClick={startEditName} title="Нажмите чтобы изменить имя">
                      {username}
                    </span>
                  )}
                </span>

                {onlineCount !== null && (
                  <div
                    className="online-count"
                    onMouseEnter={() => setShowUsersTooltip(true)}
                    onMouseLeave={() => setShowUsersTooltip(false)}
                  >
                    <span className="online-dot" />
                    {onlineCount}
                    {showUsersTooltip && onlineUsers.length > 0 && (
                      <div className="users-tooltip">
                        {onlineUsers.map(u => (
                          <span key={u.username} className={`users-tooltip-item${u.username === username ? ' users-tooltip-item--me' : ''}`}>
                            <span className="users-panel-dot" style={{ background: u.color }} />
                            {u.username}
                          </span>
                        ))}
                      </div>
                    )}
                  </div>
                )}
                <button
                  className="admin-login-btn"
                  onClick={() => setTimeOverride(t => t === undefined ? 'day' : t === 'day' ? 'night' : undefined)}
                  title={timeOverride === 'day' ? 'День → нажми для ночи' : timeOverride === 'night' ? 'Ночь → нажми для авто' : 'Авто (реальное время) → нажми для дня'}
                  aria-label="Переключить время суток"
                >
                  {timeOverride === 'night' ? '🌙' : timeOverride === 'day' ? '☀️' : '🕐'}
                </button>
                <button
                  className={`admin-login-btn${isAdmin ? ' admin-login-btn--active' : ''}`}
                  onClick={isAdmin ? handleLogout : () => setLoginOpen(l => !l)}
                  title={isAdmin ? 'Выйти из режима администратора' : 'Войти как администратор'}
                  aria-label={isAdmin ? 'Выйти' : 'Войти как администратор'}
                >
                  {isAdmin ? (
                    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" width="15" height="15">
                      <rect x="3" y="11" width="18" height="11" rx="2" ry="2"/>
                      <path d="M7 11V7a5 5 0 0 1 9.9-1M17 11V7"/>
                    </svg>
                  ) : (
                    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" width="15" height="15">
                      <rect x="3" y="11" width="18" height="11" rx="2" ry="2"/>
                      <path d="M7 11V7a5 5 0 0 1 10 0v4"/>
                    </svg>
                  )}
                </button>
              </div>

              <NowPlaying title={valid ? queueState.currentTitle : null} onDoubleClick={undefined} />

              <div className="player-controls">
                <div className="play-btn-wrap">
                  <svg className="play-ring" viewBox="0 0 68 68" aria-hidden="true" style={{ opacity: playing ? 1 : 0, transition: 'opacity 0.3s ease' }}>
                    <circle className="play-ring-track" cx="34" cy="34" r="29" />
                    <circle
                      className="play-ring-fill"
                      cx="34" cy="34" r="29"
                      style={{ strokeDashoffset: 182.2 - (182.2 * progress) / 100 }}
                    />
                  </svg>
                  <button
                    className={`play-btn${playing ? ' stop' : ''}${buffering ? ' buffering' : ''}`}
                    onClick={playing ? stopPlayback : startPlayback}
                    aria-label={playing ? 'Остановить эфир' : 'Слушать эфир'}
                  >
                    <span className="play-btn-icon" aria-hidden="true">
                      {buffering
                        ? <span className="play-btn-spinner" />
                        : playing
                          ? <svg viewBox="0 0 24 24" fill="currentColor"><rect x="6" y="5" width="4" height="14" rx="1.5"/><rect x="14" y="5" width="4" height="14" rx="1.5"/></svg>
                          : <svg viewBox="0 0 24 24" fill="currentColor"><path d="M8 5.14v14l11-7-11-7z"/></svg>
                      }
                    </span>
                  </button>
                </div>
                <button
                  className={`skip-btn${mySkipVote ? ' skip-btn--voted skip-btn--cooldown' : ''}${skipCooldown > 0 ? ' skip-btn--cooldown' : ''}`}
                  disabled={mySkipVote || skipCooldown > 0}
                  onClick={async () => {
                    if (skipCooldown > 0) return
                    const res = await fetch('/queue/skip', {
                      method: 'POST',
                      headers: { 'X-Session-Id': sessionId },
                    })
                    if (res.ok) {
                      const data = await res.json()
                      setSkipVotes(data.votes)
                      setMySkipVote(true)
                      setSkipCooldown(15)
                      setSkipShake(true)
                      setTimeout(() => setSkipShake(false), 400)
                      if (data.skipped && playing) {
                        const audio = audioRef.current
                        if (!audio) return
                        setTimeout(() => {
                          audio.src = `${STREAM_URL}?t=${Date.now()}`
                          audio.play().catch(() => {})
                        }, 300)
                      }
                    }
                  }}
                  aria-label="Пропустить"
                  title={mySkipVote ? 'Вы уже проголосовали' : skipCooldown > 0 ? `Подождите ${skipCooldown}с` : `Пропустить (${skipVotes}/3)`}
                >
                  <svg viewBox="0 0 24 24" fill="currentColor">
                    <path d="M6 18l8.5-6L6 6v12zm2-8.14L11.03 12 8 14.14V9.86zM16 6h2v12h-2z"/>
                  </svg>
                  <span className="skip-count">
                    {skipCooldown > 0 ? `${skipCooldown}` : skipVotes > 0 ? `${skipVotes}/3` : ''}
                  </span>
                </button>
              </div>

              <VolumeControl audioRef={audioRef} />
            </div>
          </div>

          <div className="catalog-wrap">
            <SongSuggestion
              onUpload={() => setUploadOpen(true)}
              refreshKey={catalogKey}
              onSuggest={() => setCatalogKey(k => k + 1)}
              sessionId={sessionId}
              isAdmin={isAdmin}
              getAuthHeader={getAuthHeader}
            />
          </div>
        </div>
      </main>

      {uploadOpen && (
        <div
          className="modal-overlay"
          onClick={() => setUploadOpen(false)}
          onDragEnter={e => e.stopPropagation()}
          onDragOver={e => e.stopPropagation()}
          onDrop={e => e.stopPropagation()}
        >
          <div className="modal" onClick={e => e.stopPropagation()}>
            <SongUpload onClose={() => setUploadOpen(false)} getAuthHeader={getAuthHeader} />
          </div>
        </div>
      )}

      {loginOpen && !isAdmin && (
        <div className="modal-overlay" onClick={() => setLoginOpen(false)}>
          <div className="modal" onClick={e => e.stopPropagation()}>
            <AdminLoginForm
              onLogin={login}
              onSuccess={() => setLoginOpen(false)}
            />
          </div>
        </div>
      )}

    </div>
  )
}
