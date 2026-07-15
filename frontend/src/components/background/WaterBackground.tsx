import { useEffect, useRef } from 'react'
import { API_MUTATION_EVENT } from '../../api/apiActivity'

/**
 * Animated water scene rendered on a full-viewport canvas behind all page
 * content (z-index below the app shell, above the CSS base gradient).
 *
 * Every element is event-based: spawned at random intervals with a random
 * lifespan. Elements drift across the surface; when one fully leaves the
 * viewport it re-enters at the antipodal point — computed with the standard
 * 2-D rotation matrix at θ = π (see rotateAbout) — unless its lifespan has
 * already expired, in which case it simply does not respawn.
 *
 * Scene inventory:
 * - reflections (both themes): broad soft light patches drifting slowly
 * - lily pads (both themes): green, floating above the water
 * - fish (both themes): grey, blurred, moving linearly under the water
 * - droplets (both themes): ripple rings — spawned randomly and whenever the
 *   API layer sends data to the server (see api/apiActivity.ts)
 * - fog + fireflies (dark mode only): drifting mist and sporadically darting
 *   points of light that bloom gold over the water
 *
 * All colors come from the --water-* design tokens in index.css so the scene
 * follows the light/dark theme.
 */

type Vec2 = { x: number; y: number }

interface Rgba {
  r: number
  g: number
  b: number
  a: number
}

type EntityKind = 'reflection' | 'lily' | 'fish' | 'firefly' | 'fog' | 'droplet'
type SpawnableKind = Exclude<EntityKind, 'droplet'>

interface SceneEntity {
  kind: EntityKind
  pos: Vec2
  /** px per second */
  vel: Vec2
  /** Base draw size; also the off-screen margin before a wrap/despawn check. */
  radius: number
  bornAt: number
  lifespanMs: number
  /** Random phase used for per-entity variation (color pick, wobble, pulse). */
  seed: number
  /** Set when a theme switch removes this kind; entity fades out then dies. */
  fadeOutStart?: number
  /** Firefly steering state: random-walk heading and dart window. */
  wanderAngle?: number
  dartUntil?: number
}

/**
 * Rotate `point` counterclockwise about `center` by `theta` using the standard
 * two-dimensional rotation matrix
 *
 *   R(θ) = [ cosθ  −sinθ ]
 *          [ sinθ   cosθ ]
 *
 * applied to the offset p − c:  p' = c + R(θ)(p − c).
 * R(θ)ᵀR(θ) = I and det R(θ) = 1, so distance from the pivot is preserved.
 *
 * The antipodal wraparound is the special case θ = π, where R(π) = −I: an
 * element drifting off one edge of the water re-enters at the diametrically
 * opposite point. Velocity is deliberately left unrotated — with the position
 * negated about the center, the unchanged heading carries the element back
 * across the surface instead of straight off the same edge.
 */
function rotateAbout(point: Vec2, center: Vec2, theta: number): Vec2 {
  const cos = Math.cos(theta)
  const sin = Math.sin(theta)
  const dx = point.x - center.x
  const dy = point.y - center.y
  return {
    x: center.x + dx * cos - dy * sin,
    y: center.y + dx * sin + dy * cos,
  }
}

const ANTIPODAL_ANGLE = Math.PI

function rand(min: number, max: number): number {
  return min + Math.random() * (max - min)
}

function parseColor(raw: string): Rgba {
  const value = raw.trim()
  if (value.startsWith('#')) {
    const hex = value.slice(1)
    const full = hex.length === 3 ? hex.split('').map((c) => c + c).join('') : hex
    return {
      r: parseInt(full.slice(0, 2), 16),
      g: parseInt(full.slice(2, 4), 16),
      b: parseInt(full.slice(4, 6), 16),
      a: 1,
    }
  }
  const match = /rgba?\(([^)]+)\)/.exec(value)
  if (match) {
    const parts = match[1].split(',').map((part) => parseFloat(part))
    return { r: parts[0] ?? 255, g: parts[1] ?? 255, b: parts[2] ?? 255, a: parts[3] ?? 1 }
  }
  return { r: 255, g: 255, b: 255, a: 0 }
}

function rgba(color: Rgba, alphaScale = 1): string {
  const a = Math.max(0, Math.min(1, color.a * alphaScale))
  return `rgba(${color.r}, ${color.g}, ${color.b}, ${a})`
}

/** Linear blend of two colors — used to mix element colors into the water. */
function mix(from: Rgba, to: Rgba, t: number): Rgba {
  return {
    r: Math.round(from.r + (to.r - from.r) * t),
    g: Math.round(from.g + (to.g - from.g) * t),
    b: Math.round(from.b + (to.b - from.b) * t),
    a: from.a + (to.a - from.a) * t,
  }
}

const WHITE: Rgba = { r: 255, g: 255, b: 255, a: 1 }

interface WaterPalette {
  reflections: [Rgba, Rgba, Rgba]
  lily: Rgba
  lilyRim: Rgba
  lilyShadow: Rgba
  fish: Rgba
  droplet: Rgba
  firefly: Rgba
  fog: Rgba
}

function readPalette(): WaterPalette {
  const styles = getComputedStyle(document.documentElement)
  const token = (name: string) => parseColor(styles.getPropertyValue(name))
  return {
    reflections: [
      token('--water-reflection-a'),
      token('--water-reflection-b'),
      token('--water-reflection-c'),
    ],
    lily: token('--water-lily'),
    lilyRim: token('--water-lily-rim'),
    lilyShadow: token('--water-lily-shadow'),
    fish: token('--water-fish'),
    droplet: token('--water-droplet'),
    firefly: token('--water-firefly'),
    fog: token('--water-fog'),
  }
}

interface SpawnRule {
  cap: number
  minDelayMs: number
  maxDelayMs: number
  minLifespanMs: number
  maxLifespanMs: number
  darkOnly?: boolean
}

const SPAWN_RULES: Record<SpawnableKind, SpawnRule> = {
  reflection: { cap: 5, minDelayMs: 4000, maxDelayMs: 9000, minLifespanMs: 50000, maxLifespanMs: 90000 },
  lily: { cap: 4, minDelayMs: 9000, maxDelayMs: 18000, minLifespanMs: 45000, maxLifespanMs: 85000 },
  fish: { cap: 3, minDelayMs: 10000, maxDelayMs: 22000, minLifespanMs: 30000, maxLifespanMs: 60000 },
  firefly: { cap: 7, minDelayMs: 2500, maxDelayMs: 7000, minLifespanMs: 20000, maxLifespanMs: 45000, darkOnly: true },
  fog: { cap: 3, minDelayMs: 12000, maxDelayMs: 25000, minLifespanMs: 60000, maxLifespanMs: 110000, darkOnly: true },
}

const SPAWNABLE_KINDS = Object.keys(SPAWN_RULES) as SpawnableKind[]

const DROPLET_CAP = 12
const DROPLET_MIN_DELAY_MS = 4000
const DROPLET_MAX_DELAY_MS = 11000
const DROPLET_LIFESPAN_MS = 2600
const FADE_IN_MS = 1200
const FADE_OUT_MS = 700
/** Painter's order, back to front: fish swim under everything else. */
const DRAW_ORDER: EntityKind[] = ['fish', 'reflection', 'droplet', 'lily', 'fog', 'firefly']

class WaterScene {
  private readonly canvas: HTMLCanvasElement
  private readonly ctx: CanvasRenderingContext2D
  private entities: SceneEntity[] = []
  private palette: WaterPalette
  private dark: boolean
  private width = 0
  private height = 0
  private rafId = 0
  private lastFrameAt = 0
  private nextSpawnAt = {} as Record<SpawnableKind, number>
  private nextDropletAt = 0
  private lastApiDropletAt = 0
  private readonly themeObserver: MutationObserver
  private readonly handleResize = () => this.resize()
  private readonly handleApiDroplet = () => this.spawnApiDroplet()
  private readonly renderFrame = (now: number) => {
    const dt = Math.min(0.1, (now - this.lastFrameAt) / 1000)
    this.lastFrameAt = now
    this.step(now, dt)
    this.draw(now)
    this.rafId = requestAnimationFrame(this.renderFrame)
  }

  constructor(canvas: HTMLCanvasElement) {
    this.canvas = canvas
    const ctx = canvas.getContext('2d')
    if (!ctx) throw new Error('2d canvas is unavailable')
    this.ctx = ctx
    this.dark = document.documentElement.getAttribute('data-theme') === 'dark'
    this.palette = readPalette()
    this.themeObserver = new MutationObserver(() => this.onThemeChange())
  }

  start(): void {
    this.resize()
    window.addEventListener('resize', this.handleResize)
    document.addEventListener(API_MUTATION_EVENT, this.handleApiDroplet)
    this.themeObserver.observe(document.documentElement, { attributes: true, attributeFilter: ['data-theme'] })

    const now = performance.now()
    for (const kind of SPAWNABLE_KINDS)
      this.nextSpawnAt[kind] = now + rand(SPAWN_RULES[kind].minDelayMs, SPAWN_RULES[kind].maxDelayMs)
    this.nextDropletAt = now + rand(DROPLET_MIN_DELAY_MS, DROPLET_MAX_DELAY_MS)

    // Seed the scene so it never starts empty; backdated ages stagger the TTLs.
    this.seedInitial(now)

    this.lastFrameAt = now
    this.rafId = requestAnimationFrame(this.renderFrame)
  }

  destroy(): void {
    cancelAnimationFrame(this.rafId)
    window.removeEventListener('resize', this.handleResize)
    document.removeEventListener(API_MUTATION_EVENT, this.handleApiDroplet)
    this.themeObserver.disconnect()
  }

  private onThemeChange(): void {
    this.dark = document.documentElement.getAttribute('data-theme') === 'dark'
    this.palette = readPalette()
    if (!this.dark) {
      const now = performance.now()
      for (const entity of this.entities) {
        if (entity.kind === 'droplet') continue
        if (SPAWN_RULES[entity.kind as SpawnableKind].darkOnly && entity.fadeOutStart === undefined)
          entity.fadeOutStart = now
      }
    }
  }

  private resize(): void {
    const dpr = Math.min(2, window.devicePixelRatio || 1)
    this.width = window.innerWidth
    this.height = window.innerHeight
    this.canvas.width = Math.round(this.width * dpr)
    this.canvas.height = Math.round(this.height * dpr)
    this.ctx.setTransform(dpr, 0, 0, dpr, 0, 0)
  }

  private count(kind: EntityKind): number {
    let total = 0
    for (const entity of this.entities) if (entity.kind === kind) total++
    return total
  }

  private randomPoint(margin: number): Vec2 {
    return { x: rand(margin, this.width - margin), y: rand(margin, this.height - margin) }
  }

  private seedInitial(now: number): void {
    const seedKind = (kind: SpawnableKind, amount: number) => {
      for (let i = 0; i < amount; i++) {
        const entity = this.makeEntity(kind, now)
        if (!entity) continue
        entity.bornAt = now - rand(0, entity.lifespanMs * 0.4)
        this.entities.push(entity)
      }
    }
    seedKind('reflection', 4)
    seedKind('lily', 2)
    seedKind('fish', 1)
    if (this.dark) {
      seedKind('fog', 2)
      seedKind('firefly', 4)
    }
  }

  private makeEntity(kind: SpawnableKind, now: number): SceneEntity | null {
    const rule = SPAWN_RULES[kind]
    const lifespanMs = rand(rule.minLifespanMs, rule.maxLifespanMs)
    const seed = rand(0, 1000)
    const base = { kind, bornAt: now, lifespanMs, seed }
    switch (kind) {
      case 'reflection': {
        const angle = rand(0, Math.PI * 2)
        const speed = rand(5, 13)
        return {
          ...base,
          pos: this.randomPoint(60),
          vel: { x: Math.cos(angle) * speed, y: Math.sin(angle) * speed },
          radius: rand(150, 320),
        }
      }
      case 'lily': {
        const angle = rand(0, Math.PI * 2)
        const speed = rand(3, 8)
        return {
          ...base,
          pos: this.randomPoint(40),
          vel: { x: Math.cos(angle) * speed, y: Math.sin(angle) * speed },
          radius: rand(16, 30),
        }
      }
      case 'fish': {
        const angle = rand(0, Math.PI * 2)
        const speed = rand(26, 48)
        return {
          ...base,
          pos: this.randomPoint(40),
          vel: { x: Math.cos(angle) * speed, y: Math.sin(angle) * speed },
          radius: rand(9, 14),
        }
      }
      case 'firefly':
        return {
          ...base,
          pos: this.randomPoint(30),
          vel: { x: rand(-15, 15), y: rand(-15, 15) },
          radius: rand(2.4, 3.4),
          wanderAngle: rand(0, Math.PI * 2),
        }
      case 'fog': {
        const direction = Math.random() < 0.5 ? -1 : 1
        return {
          ...base,
          pos: this.randomPoint(80),
          vel: { x: rand(6, 12) * direction, y: rand(-2, 2) },
          radius: rand(260, 430),
        }
      }
    }
  }

  private makeDroplet(pos: Vec2, now: number): SceneEntity {
    return {
      kind: 'droplet',
      pos,
      vel: { x: 0, y: 0 },
      radius: rand(26, 54),
      bornAt: now,
      lifespanMs: DROPLET_LIFESPAN_MS,
      seed: rand(0, 1000),
    }
  }

  private spawnApiDroplet(): void {
    const now = performance.now()
    if (now - this.lastApiDropletAt < 180) return
    this.lastApiDropletAt = now
    if (this.count('droplet') >= DROPLET_CAP) return
    // Keep API-send ripples in the outer band of the viewport, away from the
    // middle of the page where forms and input boxes live.
    let pos = this.randomPoint(40)
    for (let tries = 0; tries < 10; tries++) {
      const inCenterBand =
        pos.x > this.width * 0.22 &&
        pos.x < this.width * 0.78 &&
        pos.y > this.height * 0.16 &&
        pos.y < this.height * 0.84
      if (!inCenterBand) break
      pos = this.randomPoint(40)
    }
    this.entities.push(this.makeDroplet(pos, now))
  }

  /** Sporadic firefly flight: a random-walk heading with occasional darts. */
  private steerFirefly(entity: SceneEntity, now: number, dt: number): void {
    entity.wanderAngle = (entity.wanderAngle ?? rand(0, Math.PI * 2)) + rand(-2.4, 2.4) * dt
    if (entity.dartUntil === undefined || now > entity.dartUntil) {
      if (Math.random() < dt * 0.4) entity.dartUntil = now + rand(200, 500)
    }
    const darting = entity.dartUntil !== undefined && now < entity.dartUntil
    const speed = darting ? 120 : 26
    const blend = Math.min(1, (darting ? 6 : 2.4) * dt)
    entity.vel.x += (Math.cos(entity.wanderAngle) * speed - entity.vel.x) * blend
    entity.vel.y += (Math.sin(entity.wanderAngle) * speed - entity.vel.y) * blend
  }

  private step(now: number, dt: number): void {
    const center: Vec2 = { x: this.width / 2, y: this.height / 2 }
    const survivors: SceneEntity[] = []

    for (const entity of this.entities) {
      if (entity.fadeOutStart !== undefined && now - entity.fadeOutStart > FADE_OUT_MS) continue

      if (entity.kind === 'droplet') {
        if (now - entity.bornAt <= entity.lifespanMs) survivors.push(entity)
        continue
      }

      if (entity.kind === 'firefly') this.steerFirefly(entity, now, dt)
      entity.pos.x += entity.vel.x * dt
      entity.pos.y += entity.vel.y * dt

      const margin = entity.radius * (entity.kind === 'fish' ? 2.4 : 1.1)
      const outside =
        entity.pos.x < -margin ||
        entity.pos.x > this.width + margin ||
        entity.pos.y < -margin ||
        entity.pos.y > this.height + margin

      if (outside) {
        if (now - entity.bornAt > entity.lifespanMs) continue // lifespan over: no respawn on the far side
        entity.pos = rotateAbout(entity.pos, center, ANTIPODAL_ANGLE)
      }

      survivors.push(entity)
    }

    this.entities = survivors
    this.spawnDue(now)
  }

  private spawnDue(now: number): void {
    for (const kind of SPAWNABLE_KINDS) {
      if (now < this.nextSpawnAt[kind]) continue
      const rule = SPAWN_RULES[kind]
      this.nextSpawnAt[kind] = now + rand(rule.minDelayMs, rule.maxDelayMs)
      if (rule.darkOnly && !this.dark) continue
      if (this.count(kind) >= rule.cap) continue
      const entity = this.makeEntity(kind, now)
      if (entity) this.entities.push(entity)
    }

    if (now >= this.nextDropletAt) {
      this.nextDropletAt = now + rand(DROPLET_MIN_DELAY_MS, DROPLET_MAX_DELAY_MS)
      if (this.count('droplet') < DROPLET_CAP) this.entities.push(this.makeDroplet(this.randomPoint(30), now))
    }
  }

  /** 0→1 fade-in after spawn, 1→0 fade-out when a theme switch retires the entity. */
  private envelope(entity: SceneEntity, now: number): number {
    const fadeIn = Math.min(1, (now - entity.bornAt) / FADE_IN_MS)
    const fadeOut =
      entity.fadeOutStart === undefined ? 1 : Math.max(0, 1 - (now - entity.fadeOutStart) / FADE_OUT_MS)
    return fadeIn * fadeOut
  }

  private draw(now: number): void {
    this.ctx.clearRect(0, 0, this.width, this.height)
    for (const kind of DRAW_ORDER)
      for (const entity of this.entities)
        if (entity.kind === kind) this.drawEntity(entity, now)
  }

  private drawEntity(entity: SceneEntity, now: number): void {
    const alpha = this.envelope(entity, now)
    if (alpha <= 0) return
    switch (entity.kind) {
      case 'reflection':
        this.drawReflection(entity, alpha)
        break
      case 'lily':
        this.drawLily(entity, now, alpha)
        break
      case 'fish':
        this.drawFish(entity, alpha)
        break
      case 'droplet':
        this.drawDroplet(entity, now, alpha)
        break
      case 'fog':
        this.drawFog(entity, alpha)
        break
      case 'firefly':
        this.drawFirefly(entity, now, alpha)
        break
    }
  }

  private drawReflection(entity: SceneEntity, alpha: number): void {
    const { ctx } = this
    const color = this.palette.reflections[Math.floor(entity.seed) % 3]
    ctx.save()
    // Additive blending in dark mode makes reflections read as light gathering
    // on the water instead of grey patches.
    if (this.dark) ctx.globalCompositeOperation = 'lighter'
    const gradient = ctx.createRadialGradient(
      entity.pos.x, entity.pos.y, 0,
      entity.pos.x, entity.pos.y, entity.radius,
    )
    gradient.addColorStop(0, rgba(color, alpha))
    gradient.addColorStop(1, rgba(color, 0))
    ctx.fillStyle = gradient
    ctx.beginPath()
    ctx.arc(entity.pos.x, entity.pos.y, entity.radius, 0, Math.PI * 2)
    ctx.fill()
    ctx.restore()
  }

  private drawLily(entity: SceneEntity, now: number, alpha: number): void {
    const { ctx } = this
    const r = entity.radius
    ctx.save()
    ctx.translate(entity.pos.x, entity.pos.y)

    // Soft shadow cast onto the water, mixing the pad into the blue beneath it.
    ctx.fillStyle = rgba(this.palette.lilyShadow, alpha)
    ctx.beginPath()
    ctx.ellipse(2.5, 3.5, r * 1.02, r * 0.92, 0, 0, Math.PI * 2)
    ctx.fill()

    // Slow floating wobble (ctx.rotate applies the same R(θ) rotation matrix).
    ctx.rotate(entity.seed + Math.sin(now / 3000 + entity.seed) * 0.08)

    const notchHalf = 0.30
    const gradient = ctx.createRadialGradient(0, 0, r * 0.15, 0, 0, r)
    gradient.addColorStop(0, rgba(mix(this.palette.lily, WHITE, 0.22), alpha))
    gradient.addColorStop(1, rgba(this.palette.lily, alpha))
    ctx.fillStyle = gradient
    ctx.beginPath()
    ctx.moveTo(0, 0)
    ctx.arc(0, 0, r, notchHalf, Math.PI * 2 - notchHalf)
    ctx.closePath()
    ctx.fill()
    ctx.strokeStyle = rgba(this.palette.lilyRim, alpha * 0.85)
    ctx.lineWidth = 1.2
    ctx.stroke()
    ctx.restore()
  }

  private drawFish(entity: SceneEntity, alpha: number): void {
    const { ctx } = this
    ctx.save()
    ctx.translate(entity.pos.x, entity.pos.y)
    // Heading from velocity — ctx.rotate is the same R(θ) rotation matrix.
    ctx.rotate(Math.atan2(entity.vel.y, entity.vel.x))
    // A slight blur reads as "under the surface"; the low alpha lets the water
    // blue mix into the grey body.
    ctx.filter = 'blur(1.2px)'
    ctx.fillStyle = rgba(this.palette.fish, alpha)
    const bodyLength = entity.radius * 2.1
    const bodyHeight = entity.radius * 0.8
    ctx.beginPath()
    ctx.ellipse(0, 0, bodyLength, bodyHeight, 0, 0, Math.PI * 2)
    ctx.fill()
    ctx.beginPath()
    ctx.moveTo(-bodyLength * 0.85, 0)
    ctx.lineTo(-bodyLength * 1.45, -bodyHeight)
    ctx.lineTo(-bodyLength * 1.45, bodyHeight)
    ctx.closePath()
    ctx.fill()
    ctx.restore()
  }

  private drawDroplet(entity: SceneEntity, now: number, alpha: number): void {
    const { ctx } = this
    const t = Math.min(1, (now - entity.bornAt) / entity.lifespanMs)
    for (let ring = 0; ring < 3; ring++) {
      const offset = ring * 0.18
      if (t <= offset) continue
      const progress = (t - offset) / (1 - offset)
      ctx.strokeStyle = rgba(this.palette.droplet, alpha * (1 - progress))
      ctx.lineWidth = 1.8 * (1 - progress) + 0.4
      ctx.beginPath()
      ctx.arc(entity.pos.x, entity.pos.y, entity.radius * progress, 0, Math.PI * 2)
      ctx.stroke()
    }
  }

  private drawFog(entity: SceneEntity, alpha: number): void {
    const { ctx } = this
    ctx.save()
    ctx.translate(entity.pos.x, entity.pos.y)
    ctx.scale(1.6, 1) // mist banks stretch along the surface
    const gradient = ctx.createRadialGradient(0, 0, 0, 0, 0, entity.radius)
    gradient.addColorStop(0, rgba(this.palette.fog, alpha))
    gradient.addColorStop(1, rgba(this.palette.fog, 0))
    ctx.fillStyle = gradient
    ctx.beginPath()
    ctx.arc(0, 0, entity.radius, 0, Math.PI * 2)
    ctx.fill()
    ctx.restore()
  }

  private drawFirefly(entity: SceneEntity, now: number, alpha: number): void {
    const { ctx } = this
    const pulse = 0.5 + 0.5 * Math.sin(now * 0.004 + entity.seed)
    const speed = Math.hypot(entity.vel.x, entity.vel.y)
    // Bloom grows with movement over the water: darting fireflies flare gold.
    const intensity = Math.min(1, 0.3 + pulse * 0.35 + speed / 160)
    const glowRadius = entity.radius * (3.5 + 4.5 * intensity)
    ctx.save()
    ctx.globalCompositeOperation = 'lighter' // additive: gold mixes into the water blue
    const gradient = ctx.createRadialGradient(
      entity.pos.x, entity.pos.y, 0,
      entity.pos.x, entity.pos.y, glowRadius,
    )
    gradient.addColorStop(0, rgba(this.palette.firefly, alpha * intensity))
    gradient.addColorStop(0.4, rgba(this.palette.firefly, alpha * intensity * 0.35))
    gradient.addColorStop(1, rgba(this.palette.firefly, 0))
    ctx.fillStyle = gradient
    ctx.beginPath()
    ctx.arc(entity.pos.x, entity.pos.y, glowRadius, 0, Math.PI * 2)
    ctx.fill()
    ctx.fillStyle = rgba(mix(this.palette.firefly, WHITE, 0.5), alpha * (0.5 + 0.5 * intensity))
    ctx.beginPath()
    ctx.arc(entity.pos.x, entity.pos.y, entity.radius, 0, Math.PI * 2)
    ctx.fill()
    ctx.restore()
  }
}

export function WaterBackground() {
  const canvasRef = useRef<HTMLCanvasElement>(null)

  useEffect(() => {
    const canvas = canvasRef.current
    if (!canvas) return
    // The base gradient (body::before) already freezes under reduced motion;
    // skip the moving scene entirely rather than animating it slower.
    if (window.matchMedia('(prefers-reduced-motion: reduce)').matches) return
    const scene = new WaterScene(canvas)
    scene.start()
    return () => scene.destroy()
  }, [])

  return <canvas ref={canvasRef} className="water-scene" aria-hidden="true" />
}
