import { useEffect, useRef } from 'react'

interface Props {
  progress: number
  flash?: boolean
  pulse?: boolean
  playing?: boolean
  accent?: string
}

export default function WaterFill({ progress, flash, pulse, playing, accent }: Props) {
  const canvasRef   = useRef<HTMLCanvasElement>(null)
  const frameRef    = useRef<number>(0)
  const t1Ref       = useRef(0)
  const t2Ref       = useRef(0)
  const displayRef  = useRef(progress)
  const progressRef = useRef(progress)
  const flashRef    = useRef(false)
  const pulseRef    = useRef(0)
  const playingRef  = useRef(playing ?? true)
  const accentRef   = useRef(accent ?? null)
  // offscreen canvas — resize doesn't clear it
  const offscreenRef = useRef<HTMLCanvasElement | null>(null)
  const sizeRef      = useRef({ w: 0, h: 0 })

  useEffect(() => { progressRef.current = progress }, [progress])
  useEffect(() => { playingRef.current = playing ?? true }, [playing])
  useEffect(() => { accentRef.current = accent ?? null }, [accent])
  useEffect(() => { if (flash) { flashRef.current = true; displayRef.current = 100 } }, [flash])
  useEffect(() => { if (pulse) pulseRef.current = 18 }, [pulse])

  useEffect(() => {
    const canvas = canvasRef.current
    if (!canvas) return

    const offscreen = document.createElement('canvas')
    offscreenRef.current = offscreen

    const draw = () => {
      const { w, h } = sizeRef.current
      if (w === 0 || h === 0) { frameRef.current = requestAnimationFrame(draw); return }

      // sync offscreen size only when needed
      if (offscreen.width !== w) offscreen.width = w
      if (offscreen.height !== h) offscreen.height = h

      const ctx = offscreen.getContext('2d')!

      if (flashRef.current) { displayRef.current = 100; flashRef.current = false }
      displayRef.current += (progressRef.current - displayRef.current) * 0.04
      if (pulseRef.current > 0) pulseRef.current *= 0.88

      const fillH = (displayRef.current / 100) * h
      const y0    = h - fillH

      t1Ref.current += playingRef.current ? 0.012 : 0.003
      t2Ref.current -= playingRef.current ? 0.007 : 0.002

      const extra = pulseRef.current
      const amp1 = Math.max(4, fillH * 0.04) + extra
      const amp2 = Math.max(2, fillH * 0.025) + extra * 0.5

      ctx.clearRect(0, 0, w, h)
      ctx.beginPath()
      ctx.moveTo(0, h)
      for (let x = 0; x <= w; x += 2) {
        const wave1 = Math.sin(x / (w * 0.28) * Math.PI * 2 + t1Ref.current) * amp1
        const wave2 = Math.sin(x / (w * 0.17) * Math.PI * 2 + t2Ref.current) * amp2
        ctx.lineTo(x, y0 + wave1 + wave2)
      }
      ctx.lineTo(w, h)
      ctx.closePath()

      const grad = ctx.createLinearGradient(0, y0 - amp1 - amp2, 0, h)
      const hue1 = (t1Ref.current * 20) % 360
      const hue2 = (hue1 + 60) % 360
      const hue3 = (hue1 + 140) % 360
      if (accentRef.current) {
        const a = accentRef.current
        grad.addColorStop(0,   `color-mix(in srgb, ${a} 30%, hsla(${hue1},90%,60%,0.07))`)
        grad.addColorStop(0.5, `color-mix(in srgb, ${a} 40%, hsla(${hue2},90%,55%,0.14))`)
        grad.addColorStop(1,   `color-mix(in srgb, ${a} 55%, hsla(${hue3},90%,50%,0.22))`)
      } else {
        grad.addColorStop(0,   `hsla(${hue1},90%,60%,0.07)`)
        grad.addColorStop(0.5, `hsla(${hue2},90%,55%,0.14)`)
        grad.addColorStop(1,   `hsla(${hue3},90%,50%,0.22)`)
      }
      ctx.fillStyle = grad
      ctx.fill()

      // blit offscreen → visible canvas (no size change on visible canvas)
      const visCtx = canvas.getContext('2d')!
      visCtx.clearRect(0, 0, canvas.width, canvas.height)
      visCtx.drawImage(offscreen, 0, 0, w, h, 0, 0, canvas.width, canvas.height)

      frameRef.current = requestAnimationFrame(draw)
    }

    draw()
    return () => cancelAnimationFrame(frameRef.current)
  }, [])

  useEffect(() => {
    const canvas = canvasRef.current
    if (!canvas) return
    const ro = new ResizeObserver(entries => {
      for (const e of entries) {
        const w = Math.round(e.contentRect.width)
        const h = Math.round(e.contentRect.height)
        if (w > 0 && h > 0) {
          // update size ref — draw loop picks it up next frame
          sizeRef.current = { w, h }
          // resize visible canvas without clearing offscreen
          canvas.width  = w
          canvas.height = h
        }
      }
    })
    ro.observe(canvas)
    return () => ro.disconnect()
  }, [])

  return (
    <div style={{ position: 'absolute', inset: 0, borderRadius: 'inherit', pointerEvents: 'none' }}>
      <canvas
        ref={canvasRef}
        style={{ position: 'absolute', inset: 0, width: '100%', height: '100%', zIndex: 0, borderRadius: 'inherit', pointerEvents: 'none' }}
      />
    </div>
  )
}
