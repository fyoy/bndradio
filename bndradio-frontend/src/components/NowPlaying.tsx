// Displays the currently playing track title with a fade transition on change
// and a CSS marquee animation when the title overflows its container.
import { useState, useEffect, useRef } from 'react'

interface Props {
  title: string | null
  onDoubleClick?: () => void
}

export default function NowPlaying({ title, onDoubleClick }: Props) {
  const [displayed, setDisplayed] = useState(title)
  const [visible, setVisible] = useState(true)
  const spanRef = useRef<HTMLSpanElement>(null)
  const [overflow, setOverflow] = useState(false)

  useEffect(() => {
    if (title === displayed) return
    setVisible(false)
    const t = setTimeout(() => { setDisplayed(title); setVisible(true) }, 220)
    return () => clearTimeout(t)
  }, [title])

  useEffect(() => {
    const el = spanRef.current
    if (!el) return
    setOverflow(el.scrollWidth > el.clientWidth + 2)
  }, [displayed])

  return (
    <div className="now-playing" onDoubleClick={onDoubleClick} style={onDoubleClick ? { cursor: 'pointer' } : undefined}>
      <div
        className={`title-wrap${!overflow ? ' title-wrap--short' : ''}`}
        style={{ opacity: visible ? 1 : 0, transition: 'opacity 0.22s ease' }}
      >
        <span
          ref={spanRef}
          className={`title${overflow ? ' title--marquee' : ''}`}
        >
          {displayed ?? '—'}
        </span>
      </div>
    </div>
  )
}
