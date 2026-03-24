import React, { useState, useRef, useCallback } from 'react'

interface Props {
  onClose: () => void
  getAuthHeader?: () => Record<string, string>
}

function mapStatusToMessage(status: number): string {
  switch (status) {
    case 400: return 'Неверный запрос'
    case 413: return 'Файл слишком большой'
    case 415: return 'Неподдерживаемый формат аудио'
    case 429: return 'Слишком много запросов, подождите'
    case 500: return 'Ошибка сервера'
    default:  return `Ошибка ${status}`
  }
}

type Mode = 'single' | 'bulk'

interface BulkResult {
  name: string
  status: 'ok' | 'skip' | 'error'
  msg?: string
}

export default function SongUpload({ onClose, getAuthHeader }: Props) {
  const [mode, setMode] = useState<Mode>('single')

  const [title, setTitle] = useState('')
  const [file, setFile] = useState<File | null>(null)
  const [progress, setProgress] = useState(0)
  const [status, setStatus] = useState<{ msg: string; error?: boolean } | null>(null)
  const [uploading, setUploading] = useState(false)
  const [fileDragging, setFileDragging] = useState(false)

  const [bulkFiles, setBulkFiles] = useState<File[]>([])
  const [bulkResults, setBulkResults] = useState<BulkResult[]>([])
  const [bulkProgress, setBulkProgress] = useState<{ done: number; total: number } | null>(null)
  const [bulkRunning, setBulkRunning] = useState(false)
  const folderInputRef = useRef<HTMLInputElement>(null)

  const handleFile = (f: File | null) => {
    setFile(f)
    if (f && !title) setTitle(f.name.replace(/\.[^.]+$/, ''))
  }

  const handleFileDrop = useCallback((e: React.DragEvent) => {
    e.preventDefault()
    e.stopPropagation()
    setFileDragging(false)
    const f = e.dataTransfer.files[0]
    if (f) handleFile(f)
  }, [title])

  const handleBulkDrop = useCallback((e: React.DragEvent) => {
    e.preventDefault()
    e.stopPropagation()
    const audioExts = /\.(mp3|flac|ogg|opus|wav|aac|m4a|wma|aiff|alac)$/i
    const files = Array.from(e.dataTransfer.files).filter(f =>
      f.type.startsWith('audio/') || audioExts.test(f.name)
    )
    if (files.length) { setBulkFiles(files); setBulkResults([]); setBulkProgress(null) }
  }, [])
  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault()
    if (!file) return
    setUploading(true)
    setStatus(null)

    const formData = new FormData()
    formData.append('file', file)
    formData.append('title', title)

    const xhr = new XMLHttpRequest()
    xhr.upload.onprogress = ev => {
      if (ev.lengthComputable) setProgress(Math.round((ev.loaded / ev.total) * 100))
    }
    xhr.onload = () => {
      setUploading(false)
      if (xhr.status === 201) {
        setStatus({ msg: '✓ Загружено!' })
        setTitle(''); setFile(null); setProgress(0)
        setTimeout(onClose, 1200)
      } else if (xhr.status === 409) {
        setStatus({ msg: xhr.responseText, error: true })
      } else {
        setStatus({ msg: mapStatusToMessage(xhr.status), error: true })
      }
    }
    xhr.onerror = () => { setUploading(false); setStatus({ msg: 'Ошибка сети.', error: true }) }
    xhr.open('POST', '/songs/upload')
    const authHeader = getAuthHeader?.() ?? {}
    Object.entries(authHeader).forEach(([k, v]) => xhr.setRequestHeader(k, v))
    xhr.send(formData)
  }

  const handleFolderChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    const files = Array.from(e.target.files ?? []).filter(f => f.type.startsWith('audio/'))
    setBulkFiles(files)
    setBulkResults([])
    setBulkProgress(null)
  }

  const uploadOne = (f: File): Promise<BulkResult> => {
    return new Promise(resolve => {
      const t = f.name.replace(/\.[^.]+$/, '')
      const fd = new FormData()
      fd.append('file', f)
      fd.append('title', t)
      const xhr = new XMLHttpRequest()
      xhr.onload = () => {
        if (xhr.status === 201) resolve({ name: t, status: 'ok' })
        else if (xhr.status === 409) resolve({ name: t, status: 'skip', msg: 'уже есть' })
        else resolve({ name: t, status: 'error', msg: mapStatusToMessage(xhr.status) })
      }
      xhr.onerror = () => resolve({ name: t, status: 'error', msg: 'сеть' })
      xhr.open('POST', '/songs/upload')
      const authHeader = getAuthHeader?.() ?? {}
      Object.entries(authHeader).forEach(([k, v]) => xhr.setRequestHeader(k, v))
      xhr.send(fd)
    })
  }

  const handleBulkUpload = async () => {
    if (!bulkFiles.length) return
    setBulkRunning(true)
    setBulkResults([])
    const results: BulkResult[] = []
    for (let i = 0; i < bulkFiles.length; i++) {
      setBulkProgress({ done: i, total: bulkFiles.length })
      const r = await uploadOne(bulkFiles[i])
      results.push(r)
      setBulkResults([...results])
    }
    setBulkProgress({ done: bulkFiles.length, total: bulkFiles.length })
    setBulkRunning(false)
  }

  const okCount   = bulkResults.filter(r => r.status === 'ok').length
  const skipCount = bulkResults.filter(r => r.status === 'skip').length
  const errCount  = bulkResults.filter(r => r.status === 'error').length

  return (
    <>
      <div className="modal-title">
        Загрузить треки
        <button className="modal-close" onClick={onClose}>✕</button>
      </div>

      <div className="upload-tabs">
        <button className={`upload-tab${mode === 'single' ? ' active' : ''}`} onClick={() => setMode('single')}>Один трек</button>
        <button className={`upload-tab${mode === 'bulk' ? ' active' : ''}`} onClick={() => setMode('bulk')}>Папка</button>
      </div>

      {mode === 'single' && (
        <form className="upload-form" onSubmit={handleSubmit}>
          <label
            className={`file-label ${file ? 'has-file' : ''}${fileDragging ? ' dragging' : ''}`}
            onDragOver={e => { e.preventDefault(); setFileDragging(true) }}
            onDragLeave={() => setFileDragging(false)}
            onDrop={handleFileDrop}
          >
            <input type="file" accept="audio/*" onChange={e => handleFile(e.target.files?.[0] ?? null)} />
            {file ? `🎵 ${file.name}` : '+ Выбрать или перетащить файл'}
          </label>
          <input
            type="text"
            placeholder="Название"
            value={title}
            onChange={e => setTitle(e.target.value)}
            required
          />
          {progress > 0 && progress < 100 && (
            <progress className="upload-progress" value={progress} max={100} />
          )}
          <button className="upload-submit" type="submit" disabled={!file || !title || uploading}>
            {uploading ? `Загрузка… ${progress}%` : 'Загрузить'}
          </button>
          {status && <div className={`upload-status ${status.error ? 'error' : ''}`}>{status.msg}</div>}
        </form>
      )}

      {mode === 'bulk' && (
        <div className="upload-form">
          <label
            className={`file-label ${bulkFiles.length ? 'has-file' : ''}`}
            onDragOver={e => e.preventDefault()}
            onDrop={handleBulkDrop}
          >
            <input
              ref={folderInputRef}
              type="file"
              accept="audio/*"
              multiple
              {...{ webkitdirectory: '' } as React.InputHTMLAttributes<HTMLInputElement>}
              onChange={handleFolderChange}
            />
            {bulkFiles.length ? `📁 ${bulkFiles.length} аудиофайлов` : '+ Выбрать папку или перетащить файлы'}
          </label>

          {bulkProgress && (
            <progress className="upload-progress" value={bulkProgress.done} max={bulkProgress.total} />
          )}

          {bulkProgress && (
            <div className="upload-status">
              {bulkProgress.done}/{bulkProgress.total}
              {bulkResults.length > 0 && ` · ✓ ${okCount} пропущено ${skipCount} ошибок ${errCount}`}
            </div>
          )}

          {bulkResults.length > 0 && !bulkRunning && (
            <div className="upload-status">
              Готово: загружено {okCount}, пропущено {skipCount}{errCount > 0 ? `, ошибок ${errCount}` : ''}
            </div>
          )}

          <button
            className="upload-submit"
            onClick={handleBulkUpload}
            disabled={!bulkFiles.length || bulkRunning}
          >
            {bulkRunning ? 'Загрузка…' : `Загрузить ${bulkFiles.length ? `(${bulkFiles.length})` : ''}`}
          </button>
        </div>
      )}
    </>
  )
}
