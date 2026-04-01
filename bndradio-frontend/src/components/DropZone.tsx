// Full-page drag-and-drop overlay for batch audio uploads (admin only).
// Listens to document-level drag events and shows an overlay when files are dragged over the window.
// Uploads files sequentially and shows a toast with the result summary.
import { useState, useEffect, useCallback, useRef } from 'react'

interface UploadResult {
  name: string
  status: 'ok' | 'skip' | 'error'
}

interface Props {
  onUploaded?: () => void
  disabled?: boolean
  isAdmin?: boolean
  getAuthHeader?: () => Record<string, string>
}

const AUDIO_EXTS = /\.(mp3|flac|ogg|opus|wav|aac|m4a|wma|aiff|alac)$/i

function isAudio(f: File) {
  return f.type.startsWith('audio/') || AUDIO_EXTS.test(f.name)
}

function uploadFile(file: File, authHeader: Record<string, string>): Promise<UploadResult> {
  return new Promise(resolve => {
    const title = file.name.replace(/\.[^.]+$/, '')
    const fd = new FormData()
    fd.append('file', file)
    fd.append('title', title)
    const xhr = new XMLHttpRequest()
    xhr.onload = () => {
      if (xhr.status === 201) resolve({ name: title, status: 'ok' })
      else if (xhr.status === 409) resolve({ name: title, status: 'skip' })
      else resolve({ name: title, status: 'error' })
    }
    xhr.onerror = () => resolve({ name: title, status: 'error' })
    xhr.open('POST', '/songs/upload')
    Object.entries(authHeader).forEach(([k, v]) => xhr.setRequestHeader(k, v))
    xhr.send(fd)
  })
}

export default function DropZone({ onUploaded, disabled, isAdmin = false, getAuthHeader }: Props) {
  const [dragging, setDragging] = useState(false)
  const [toast, setToast] = useState<string | null>(null)
  const [running, setRunning] = useState(false)
  const [progress, setProgress] = useState<{ done: number; total: number } | null>(null)
  const dragCounter = useRef(0)
  const toastTimer = useRef<ReturnType<typeof setTimeout> | null>(null)

  const isAdminRef = useRef(isAdmin)
  const getAuthHeaderRef = useRef(getAuthHeader)
  isAdminRef.current = isAdmin
  getAuthHeaderRef.current = getAuthHeader
  const onUploadedRef = useRef(onUploaded)
  const disabledRef   = useRef(disabled)
  onUploadedRef.current = onUploaded
  disabledRef.current   = disabled

  const showToast = useCallback((msg: string, duration = 3500) => {
    setToast(msg)
    if (toastTimer.current) clearTimeout(toastTimer.current)
    toastTimer.current = setTimeout(() => setToast(null), duration)
  }, [])

  const showToastRef = useRef(showToast)
  showToastRef.current = showToast

  const handleFiles = useCallback(async (files: File[]) => {
    const audio = files.filter(isAudio)
    if (!audio.length) { showToastRef.current('Нет аудиофайлов'); return }

    setRunning(true)
    let ok = 0, skip = 0, err = 0
    const authHeader = getAuthHeaderRef.current?.() ?? {}
    for (let i = 0; i < audio.length; i++) {
      setProgress({ done: i, total: audio.length })
      const r = await uploadFile(audio[i], authHeader)
      if (r.status === 'ok') ok++
      else if (r.status === 'skip') skip++
      else err++
    }

    setProgress(null)
    setRunning(false)

    const parts: string[] = []
    if (ok)   parts.push(`✓ ${ok} загружено`)
    if (skip) parts.push(`${skip} пропущено`)
    if (err)  parts.push(`${err} ошибок`)
    showToastRef.current(parts.join(' · '), 4000)

    if (ok > 0) onUploadedRef.current?.()
  }, [])

  useEffect(() => {
    const onDragEnter = (e: DragEvent) => {
      if (disabledRef.current) return
      if (!isAdminRef.current) return
      if (!e.dataTransfer?.types.includes('Files')) return
      e.preventDefault()
      dragCounter.current++
      setDragging(true)
    }
    const onDragLeave = (e: DragEvent) => {
      if (!e.dataTransfer?.types.includes('Files')) return
      e.preventDefault()
      dragCounter.current--
      if (dragCounter.current <= 0) {
        dragCounter.current = 0
        setDragging(false)
      }
    }
    const onDragOver = (e: DragEvent) => {
      if (disabledRef.current) return
      if (!isAdminRef.current) return
      if (!e.dataTransfer?.types.includes('Files')) return
      e.preventDefault()
      e.dataTransfer.dropEffect = 'copy'
    }
    const onDrop = (e: DragEvent) => {
      dragCounter.current = 0
      setDragging(false)
      if (disabledRef.current) return
      if (!isAdminRef.current) return
      e.preventDefault()
      const files = Array.from(e.dataTransfer?.files ?? [])
      if (files.length) handleFiles(files)
    }

    document.addEventListener('dragenter', onDragEnter)
    document.addEventListener('dragleave', onDragLeave)
    document.addEventListener('dragover',  onDragOver)
    document.addEventListener('drop',      onDrop)
    return () => {
      document.removeEventListener('dragenter', onDragEnter)
      document.removeEventListener('dragleave', onDragLeave)
      document.removeEventListener('dragover',  onDragOver)
      document.removeEventListener('drop',      onDrop)
    }
  }, [handleFiles])

  return (
    <>
      {dragging && (
        <div className="drop-overlay">
          <div className="drop-overlay-inner">
            <span className="drop-overlay-icon">↑</span>
            <span className="drop-overlay-label">Отпустите для загрузки</span>
          </div>
        </div>
      )}

      {(running || toast) && (
        <div className="drop-toast">
          {running && progress
            ? `Загрузка… ${progress.done + 1}/${progress.total}`
            : toast}
        </div>
      )}
    </>
  )
}
