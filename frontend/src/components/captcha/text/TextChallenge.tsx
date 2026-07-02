import { useMemo } from 'react'

interface TextChallengeProps {
  content: string
  answer: string
  onAnswerChange: (value: string) => void
  onKeydown: () => void
  disabled?: boolean
  inputId?: string
}

// System font stacks only — no external font loading (no new network dependency, no CSP risk).
// Each ends in a different generic CSS family (serif/monospace/cursive/sans-serif/fantasy) so
// they still render visibly distinct from each other even on a system that has none of the named
// fonts installed and falls all the way back to the generic.
const FONT_STACKS = [
  "Georgia, 'Times New Roman', serif",
  "'Courier New', Courier, monospace",
  "'Comic Sans MS', 'Chalkboard SE', cursive",
  "Impact, Haettenschweiler, 'Arial Narrow Bold', sans-serif",
  "Papyrus, Copperplate, fantasy",
  "'Trebuchet MS', 'Segoe UI', system-ui, sans-serif",
]

const FONT_WEIGHTS = [400, 500, 700, 900]
const CHAR_COLORS = ['#1e3a8a', '#1d4ed8', '#2563eb', '#3730a3', '#312e81', '#4c1d95', '#0f172a']
const HOLE_VARIANTS = ['round', 'ragged'] as const

interface StrikeDecoration {
  top: number
  angle: number
}

interface HoleDecoration {
  variant: (typeof HOLE_VARIANTS)[number]
  left: number
  top: number
  size: number
}

interface CharDecoration {
  style: React.CSSProperties
  strike: StrikeDecoration | null
}

/** FNV-1a-style hash of the content string, mixed with the character's own index, so adjacent
 * characters (and the same character appearing twice) don't get correlated distortion. */
function seedFor(content: string, index: number): number {
  let h = 2166136261 ^ index
  for (let i = 0; i < content.length; i++) {
    h ^= content.charCodeAt(i)
    h = Math.imul(h, 16777619)
  }
  h ^= Math.imul(index + 1, 2654435761)
  return h >>> 0
}

/** Tiny deterministic PRNG (mulberry32) — not Math.random. Seeding it from the content string
 * means re-renders that don't change the content (e.g. typing into the answer field) don't make
 * the distortion visibly reshuffle, while still giving each character its own independent-looking
 * sequence of random choices. */
function mulberry32(seed: number): () => number {
  let t = seed >>> 0
  return () => {
    t = (t + 0x6d2b79f5) | 0
    let r = Math.imul(t ^ (t >>> 15), 1 | t)
    r = (r + Math.imul(r ^ (r >>> 7), 61 | r)) ^ r
    return ((r ^ (r >>> 14)) >>> 0) / 4294967296
  }
}

function pick<T>(rand: () => number, options: readonly T[]): T {
  return options[Math.floor(rand() * options.length)]
}

function range(rand: () => number, min: number, max: number): number {
  return min + rand() * (max - min)
}

/**
 * A CSS mask applied directly to the character glyph — as opposed to painting a copy of the
 * background pattern on top of it — so the "hole" is a genuine transparency and whatever is
 * actually behind the glyph (the panel's striped background) shows through with pixel-perfect
 * alignment. A painted-on copy can never line up, since it starts tiling its own pattern from its
 * own box origin rather than the real background's.
 */
function buildHoleMask(hole: HoleDecoration): string {
  const rx = hole.size
  const ry = hole.variant === 'ragged' ? hole.size * 0.55 : hole.size * 0.85
  const stops =
    hole.variant === 'ragged'
      ? 'transparent 0%, transparent 50%, rgba(0, 0, 0, 0.4) 65%, black 88%'
      : 'transparent 0%, transparent 72%, black 100%'
  return `radial-gradient(ellipse ${rx}% ${ry}% at ${hole.left}% ${hole.top}%, ${stops})`
}

/** Builds one character's full decoration independently of every other character: its own font,
 * weight, italic/normal, color, rotate/translate/skew/scale transform, and independently-rolled
 * chances of a strikethrough and/or a torn-looking "hole" — the two can stack on the same
 * character. Whitespace never gets either (there's no stroke to tear). */
function buildCharDecoration(content: string, index: number): CharDecoration {
  const char = content[index]
  const rand = mulberry32(seedFor(content, index))

  const fontFamily = pick(rand, FONT_STACKS)
  const fontWeight = pick(rand, FONT_WEIGHTS)
  const fontStyle = rand() < 0.4 ? 'italic' : 'normal'
  const color = pick(rand, CHAR_COLORS)
  const rotate = range(rand, -14, 14)
  const translateX = range(rand, -2.5, 2.5)
  const translateY = range(rand, -3, 3)
  const skewX = range(rand, -9, 9)
  const scaleY = range(rand, 0.88, 1.12)

  const style: React.CSSProperties = {
    fontFamily,
    fontWeight,
    fontStyle,
    color,
    transform: `rotate(${rotate}deg) translate(${translateX}px, ${translateY}px) skewX(${skewX}deg) scaleY(${scaleY})`,
  }

  const isBlank = char === ' '

  const strike: StrikeDecoration | null =
    !isBlank && rand() < 0.25 ? { top: range(rand, 35, 65), angle: range(rand, -20, 20) } : null

  const hole: HoleDecoration | null =
    !isBlank && rand() < 0.25
      ? { variant: pick(rand, HOLE_VARIANTS), left: range(rand, 20, 65), top: range(rand, 15, 60), size: range(rand, 26, 40) }
      : null

  if (hole) {
    const mask = buildHoleMask(hole)
    style.maskImage = mask
    style.WebkitMaskImage = mask
  }

  return { style, strike }
}

function blockEvent(e: React.SyntheticEvent) {
  e.preventDefault()
}

/**
 * The code or expression is rendered as per-character distorted, non-selectable spans — each
 * character independently gets its own font, weight, italic/normal, color, and rotate/skew/scale
 * transform, and independently rolls a strikethrough and a "hole"/tear (the two can stack) — with
 * copy/cut/right-click/drag blocked on it, and paste/drop blocked on the answer field, so it can't
 * just be lifted with Ctrl+C/Ctrl+V.
 */
export function TextChallenge({ content, answer, onAnswerChange, onKeydown, disabled, inputId }: TextChallengeProps) {
  const decorations = useMemo(() => content.split('').map((_, i) => buildCharDecoration(content, i)), [content])

  return (
    <>
      <div
        className="captcha-content"
        aria-label={content}
        onCopy={blockEvent}
        onCut={blockEvent}
        onContextMenu={blockEvent}
        onDragStart={blockEvent}
      >
        {content.split('').map((char, i) => {
          const { style, strike } = decorations[i]
          return (
            <span key={i} className="captcha-char-wrap" aria-hidden="true">
              <span className="captcha-char" style={style}>
                {char}
              </span>
              {strike && (
                <span
                  className="captcha-char-strike"
                  style={{ top: `${strike.top}%`, transform: `rotate(${strike.angle}deg)` }}
                />
              )}
            </span>
          )
        })}
      </div>

      <input
        id={inputId}
        type="text"
        className="captcha-input"
        value={answer}
        onChange={(e) => onAnswerChange(e.target.value)}
        onKeyDown={onKeydown}
        onPaste={blockEvent}
        onDrop={blockEvent}
        autoComplete="off"
        disabled={disabled}
        placeholder="Your answer"
      />
    </>
  )
}
