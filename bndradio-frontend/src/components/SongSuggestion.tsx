import React, { useState, useEffect, useCallback } from 'react'
import type { Song } from '../types'

interface Props {
  onUpload: () => void
  refreshKey?: number
  onSuggest?: () => void
  sessionId: string
  isAdmin?: boolean
  getAuthHeader?: () => Record<string, string>
}

function queueLabel(pos: number): string {
  if (pos === 0) return 'играет'
  return `#${pos}`
}

type Tab = 'catalog'

export default function SongSuggestion({ onUpload, refreshKey, onSuggest, sessionId, isAdmin = false, getAuthHeader }: Props) {
  const [songs, setSongs] = useState<Song[]>([])
  const [search, setSearch] = useState('')
  const [debouncedSearch, setDebouncedSearch] = useState('')
  const [status, setStatus] = useState<string | null>(null)
  const [deleteMode, setDeleteMode] = useState(false)
  const [activeTab] = useState<Tab>('catalog')
  const [searchFocused, setSearchFocused] = useState(false)

  const fetchList = useCallback(async () => {
    try {
      const res = await fetch('/queue/list', {
        headers: { 'X-Session-Id': sessionId },
      })
      if (!res.ok) return
      const data: Song[] = await res.json()
      const sorted = [...data].sort((a, b) => {
        const aInQueue = a.queuePosition != null
        const bInQueue = b.queuePosition != null
        if (aInQueue && bInQueue) return (a.queuePosition ?? 0) - (b.queuePosition ?? 0)
        if (aInQueue) return -1
        if (bInQueue) return 1
        return (b.playCount ?? 0) - (a.playCount ?? 0)
      })
      setSongs(sorted)
    } catch { /* ignore */ }
  }, [sessionId])

  useEffect(() => {
    fetchList()
    const id = setInterval(fetchList, 5000)
    return () => clearInterval(id)
  }, [fetchList])

  useEffect(() => {
    if (refreshKey !== undefined) fetchList()
  }, [refreshKey, fetchList])

  useEffect(() => {
    const id = setTimeout(() => setDebouncedSearch(search), 150)
    return () => clearTimeout(id)
  }, [search])

  const hasVotedElsewhere = songs.some(s => s.myVote && s.queuePosition !== 0)

  const filtered = songs.filter(s =>
    s.title.toLowerCase().includes(debouncedSearch.toLowerCase())
  )

  const handleSuggest = async (songId: string, isRevote: boolean = false) => {
    try {
      const res = await fetch('/queue/suggest', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json', 'X-Session-Id': sessionId },
        body: JSON.stringify({ songId }),
      })
      if (res.ok) {
        setStatus(isRevote ? '↩ Голос перенесён' : '✓ Повышен приоритет')
        fetchList()
        onSuggest?.()
      } else if (res.status === 429) {
        const data = await res.json()
        const mins = Math.ceil(data.secondsRemaining / 60)
        setStatus(`⏳ Подождите ещё ${mins} мин`)
      } else {
        setStatus('Не найдено.')
      }
    } catch {
      setStatus('Ошибка.')
    }
    setTimeout(() => setStatus(null), 2500)
  }

  const handleUnvote = async (songId: string) => {
    try {
      const res = await fetch('/queue/suggest', {
        method: 'DELETE',
        headers: { 'Content-Type': 'application/json', 'X-Session-Id': sessionId },
        body: JSON.stringify({ songId }),
      })
      if (res.ok) {
        setStatus('✕ Голос снят')
        fetchList()
        onSuggest?.()
      }
    } catch {
      setStatus('Ошибка.')
    }
    setTimeout(() => setStatus(null), 2500)
  }

  const handleDelete = async (e: React.MouseEvent, songId: string) => {
    e.stopPropagation()
    try {
      const res = await fetch(`/songs/${songId}`, {
        method: 'DELETE',
        headers: { ...getAuthHeader?.() },
      })
      if (res.ok) { fetchList(); onSuggest?.() }
      else setStatus('Ошибка удаления.')
    } catch {
      setStatus('Ошибка.')
    }
    setTimeout(() => setStatus(null), 2500)
  }

  return (
    <aside className="sidebar open">
        <div className="sidebar-header">

          {activeTab === 'catalog' && (
            <div className="sidebar-search-inline" style={searchFocused ? { flex: 1 } : {}}>
              <svg className="sidebar-search-icon" viewBox="0 0 24 24" fill="currentColor" width="13" height="13">
                <path d="M15.5 14h-.79l-.28-.27A6.47 6.47 0 0016 9.5 6.5 6.5 0 109.5 16c1.61 0 3.09-.59 4.23-1.57l.27.28v.79l5 4.99L20.49 19l-4.99-5zm-6 0C7.01 14 5 11.99 5 9.5S7.01 5 9.5 5 14 7.01 14 9.5 11.99 14 9.5 14z"/>
              </svg>
              <input
                className="sidebar-search-input"
                type="text"
                placeholder="Поиск…"
                value={search}
                onChange={e => setSearch(e.target.value)}
                onFocus={() => setSearchFocused(true)}
                onBlur={() => setSearchFocused(false)}
              />
            </div>
          )}

          <div
            className="sidebar-header-actions"
            style={searchFocused ? { opacity: 0, pointerEvents: 'none', flex: '0 0 0', overflow: 'hidden', padding: 0, margin: 0, minWidth: 0 } : {}}
          >
            {isAdmin && (
              <button className="upload-pill" onClick={onUpload} title="Загрузить">
                <span className="upload-pill-icon">↑</span>
              </button>
            )}
            {isAdmin && activeTab === 'catalog' && (
              <button
                className={`delete-mode-btn${deleteMode ? ' active' : ''}`}
                onClick={() => setDeleteMode(d => !d)}
                aria-label="Режим удаления"
              >
                <svg viewBox="0 0 24 24" fill="currentColor" width="14" height="14">
                  <path d="M9 3v1H4v2h1v13a2 2 0 002 2h10a2 2 0 002-2V6h1V4h-5V3H9zm0 5h2v9H9V8zm4 0h2v9h-2V8z"/>
                </svg>
              </button>
            )}
          </div>
        </div>

        <ul className="song-list">
          {filtered.length === 0 && (
            <li className="song-list-empty">
              {search ? 'Ничего не найдено' : (
                <>
                  <div>Каталог пуст</div>
                  <div style={{ marginTop: 8, fontSize: 11, opacity: 0.6 }}>перетащи файл сюда</div>
                </>
              )}
            </li>
          )}
          {filtered.map((song) => {
            const onCooldown = (song.voteCooldown ?? 0) > 0
            const minsLeft = onCooldown ? Math.ceil((song.voteCooldown ?? 0) / 60) : 0
            const isPlaying = song.queuePosition === 0
            const clickable = !deleteMode && !isPlaying && !onCooldown
            return (
              <li
                key={song.id}
                className={`song-item${isPlaying ? ' song-item--playing' : ''}${song.myVote ? ' song-item--voted' : ''}${deleteMode ? ' delete-mode' : ''}${onCooldown ? ' song-item--cooldown' : ''}`}
                onClick={() => {
                  if (deleteMode || isPlaying || onCooldown) return
                  if (song.myVote) {
                    handleUnvote(song.id)
                  } else {
                    handleSuggest(song.id, hasVotedElsewhere && !song.myVote)
                  }
                }}
                title={onCooldown ? `Недавно играла — подождите ${minsLeft} мин` : song.myVote ? 'Нажмите чтобы снять голос' : undefined}
              >
                <div className="song-item-row">
                  <div className="song-item-text">
                    <div className="song-item-title">{song.title}</div>
                  </div>
                  {clickable && !deleteMode && (
                    <span className="song-boost-hint">{song.myVote ? '✕ Снять' : '↑ Поднять'}</span>
                  )}
                  <div className="song-item-meta">
                    {onCooldown && (
                      <span className="cooldown-badge" title={`Ещё ${minsLeft} мин`}>⏳{minsLeft}м</span>
                    )}
                    {song.queuePosition != null && (
                      <span className={`queue-badge${song.queuePosition === 0 ? ' queue-badge--now' : ''}`}>
                        {queueLabel(song.queuePosition)}
                      </span>
                    )}
                    {(song.voteCount ?? 0) > 0 && (
                      <span className={`vote-count${song.myVote ? ' vote-count--mine' : ''}`}>
                        ↑{song.voteCount}
                      </span>
                    )}
                    {(song.playCount ?? 0) > 0 && song.queuePosition == null && (song.voteCount ?? 0) === 0 && (
                      <span className="play-count">{song.playCount}×</span>
                    )}
                    {deleteMode && isAdmin && (
                      <button
                        className="song-delete-btn visible"
                        onClick={e => handleDelete(e, song.id)}
                        aria-label="Удалить"
                      >×</button>
                    )}
                  </div>
                </div>
              </li>
            )
          })}
        </ul>

        {status && <div className="suggest-feedback">{status}</div>}
      </aside>
  )
}
