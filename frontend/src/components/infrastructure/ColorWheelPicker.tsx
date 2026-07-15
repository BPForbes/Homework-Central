import { useEffect, useRef, useState } from 'react'
import type { PointerEvent as ReactPointerEvent } from 'react'
import { FontAwesomeIcon } from '@fortawesome/react-fontawesome'
import { faEyeDropper } from '@fortawesome/free-solid-svg-icons'

interface Hsv {
  h: number
  s: number
  v: number
}

const HEX_PATTERN = /^#[0-9a-f]{6}$/i

function hexToRgb(hex: string): { r: number; g: number; b: number } {
  const int = parseInt(hex.slice(1), 16)
  return { r: (int >> 16) & 255, g: (int >> 8) & 255, b: int & 255 }
}

function rgbToHex(r: number, g: number, b: number): string {
  const clamp = (n: number) => Math.max(0, Math.min(255, Math.round(n)))
  return `#${[r, g, b].map((c) => clamp(c).toString(16).padStart(2, '0')).join('')}`
}

function rgbToHsv(r: number, g: number, b: number): Hsv {
  const rf = r / 255
  const gf = g / 255
  const bf = b / 255
  const max = Math.max(rf, gf, bf)
  const min = Math.min(rf, gf, bf)
  const d = max - min
  let h = 0
  if (d !== 0) {
    if (max === rf) h = 60 * (((gf - bf) / d) % 6)
    else if (max === gf) h = 60 * ((bf - rf) / d + 2)
    else h = 60 * ((rf - gf) / d + 4)
  }
  if (h < 0) h += 360
  const s = max === 0 ? 0 : d / max
  return { h, s, v: max }
}

function hsvToRgb(h: number, s: number, v: number): { r: number; g: number; b: number } {
  const c = v * s
  const x = c * (1 - Math.abs(((h / 60) % 2) - 1))
  const m = v - c
  let r = 0
  let g = 0
  let b = 0
  if (h < 60) [r, g, b] = [c, x, 0]
  else if (h < 120) [r, g, b] = [x, c, 0]
  else if (h < 180) [r, g, b] = [0, c, x]
  else if (h < 240) [r, g, b] = [0, x, c]
  else if (h < 300) [r, g, b] = [x, 0, c]
  else [r, g, b] = [c, 0, x]
  return { r: (r + m) * 255, g: (g + m) * 255, b: (b + m) * 255 }
}

function hexToHsv(hex: string): Hsv {
  const { r, g, b } = hexToRgb(hex)
  return rgbToHsv(r, g, b)
}

function hsvToHex(hsv: Hsv): string {
  const { r, g, b } = hsvToRgb(hsv.h, hsv.s, hsv.v)
  return rgbToHex(r, g, b)
}

interface EyeDropperResult {
  sRGBHex: string
}

interface EyeDropperApi {
  open: () => Promise<EyeDropperResult>
}

declare global {
  interface Window {
    EyeDropper: new () => EyeDropperApi
  }
}

interface ColorWheelPickerProps {
  value: string
  onChange: (hex: string) => void
}

/**
 * A self-contained HSV color wheel (saturation/value square + hue slider) plus, where supported,
 * the browser's EyeDropper API for screen color picking. Deliberately doesn't use the native
 * `<input type="color">` control — its OS-level color dialog is known to hang the render process
 * in some environments — so all interaction here stays inside the page.
 */
export function ColorWheelPicker({ value, onChange }: ColorWheelPickerProps) {
  const [hsv, setHsv] = useState<Hsv>(() => (HEX_PATTERN.test(value) ? hexToHsv(value) : { h: 0, s: 0, v: 0 }))
  const squareRef = useRef<HTMLDivElement>(null)
  const hueRef = useRef<HTMLDivElement>(null)
  const draggingRef = useRef<'square' | 'hue' | null>(null)

  useEffect(() => {
    if (HEX_PATTERN.test(value) && value.toLowerCase() !== hsvToHex(hsv).toLowerCase())
      setHsv(hexToHsv(value))
    // Only re-sync when the externally-controlled value actually changes; re-running this on
    // every local hsv change would fight the user mid-drag.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [value])

  function updateFromSquare(clientX: number, clientY: number) {
    const el = squareRef.current
    if (!el) return
    const rect = el.getBoundingClientRect()
    const s = Math.min(1, Math.max(0, (clientX - rect.left) / rect.width))
    const v = 1 - Math.min(1, Math.max(0, (clientY - rect.top) / rect.height))
    const next = { ...hsv, s, v }
    setHsv(next)
    onChange(hsvToHex(next))
  }

  function updateFromHue(clientX: number) {
    const el = hueRef.current
    if (!el) return
    const rect = el.getBoundingClientRect()
    const h = Math.min(1, Math.max(0, (clientX - rect.left) / rect.width)) * 360
    const next = { ...hsv, h }
    setHsv(next)
    onChange(hsvToHex(next))
  }

  function handlePointerDown(target: 'square' | 'hue') {
    return (e: ReactPointerEvent<HTMLDivElement>) => {
      draggingRef.current = target
      e.currentTarget.setPointerCapture(e.pointerId)
      if (target === 'square') updateFromSquare(e.clientX, e.clientY)
      else updateFromHue(e.clientX)
    }
  }

  function handlePointerMove(e: ReactPointerEvent<HTMLDivElement>) {
    if (draggingRef.current === 'square') updateFromSquare(e.clientX, e.clientY)
    else if (draggingRef.current === 'hue') updateFromHue(e.clientX)
  }

  function handlePointerUp() {
    draggingRef.current = null
  }

  async function handleEyedropper() {
    const EyeDropperCtor = window.EyeDropper
    if (!EyeDropperCtor) return
    try {
      const result = await new EyeDropperCtor().open()
      if (HEX_PATTERN.test(result.sRGBHex)) {
        setHsv(hexToHsv(result.sRGBHex))
        onChange(result.sRGBHex)
      }
    } catch {
      // User cancelled the eyedropper (e.g. pressed Escape) — nothing to do.
    }
  }

  const pureHueHex = hsvToHex({ h: hsv.h, s: 1, v: 1 })
  const supportsEyedropper = typeof window !== 'undefined' && 'EyeDropper' in window

  return (
    <div className="color-wheel-picker">
      <div
        ref={squareRef}
        className="color-wheel-square"
        style={{ backgroundColor: pureHueHex }}
        onPointerDown={handlePointerDown('square')}
        onPointerMove={handlePointerMove}
        onPointerUp={handlePointerUp}
        role="slider"
        aria-label="Saturation and brightness"
        aria-valuenow={Math.round(hsv.v * 100)}
      >
        <div className="color-wheel-square-thumb" style={{ left: `${hsv.s * 100}%`, top: `${(1 - hsv.v) * 100}%` }} />
      </div>
      <div className="color-wheel-controls">
        <div
          ref={hueRef}
          className="color-wheel-hue"
          onPointerDown={handlePointerDown('hue')}
          onPointerMove={handlePointerMove}
          onPointerUp={handlePointerUp}
          role="slider"
          aria-label="Hue"
          aria-valuenow={Math.round(hsv.h)}
        >
          <div className="color-wheel-hue-thumb" style={{ left: `${(hsv.h / 360) * 100}%` }} />
        </div>
        {supportsEyedropper && (
          <button
            type="button"
            className="color-wheel-eyedropper"
            onClick={() => void handleEyedropper()}
            title="Pick a color from the screen"
            aria-label="Pick a color from the screen"
          >
            <FontAwesomeIcon icon={faEyeDropper} />
          </button>
        )}
      </div>
    </div>
  )
}
