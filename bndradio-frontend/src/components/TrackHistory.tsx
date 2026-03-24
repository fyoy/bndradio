import { useEffect, useState } from 'react'

interface HistoryItem {
  id: string
  title: string
  playedAt: string
}

interface Props {
  refreshKey?: number
}

export default function TrackHistory({ refreshKey }: Props) {
  const [history, setHistory] = useState<HistoryItem[]>([])

  const load = async () => {
    try {
      const res = await fetch('/queue/history')
      if (res.ok) setHistory(await res.json())
    } catch { /* ignore */ }
  }

  useEffect(() => { load() }, [refreshKey])

  if (history.length === 0) return (
    <div className="history-empty">Ещё ничего не играло</div>
  )

  return (
    <ul className="history-list">
      {history.map((item, i) => (
        <li key={item.id + i} className="history-item">
          <span className="history-num">{i + 1}</span>
          <span className="history-title">{item.title}</span>
        </li>
      ))}
    </ul>
  )
}
