import { useEffect, useRef } from 'react'
import { API_MUTATION_EVENT } from '../../api/apiActivity'
import {
  AmbientSpawner,
  SCENE_FPS,
  SCENE_FRAME_MS,
  sampleFrameRange,
  scheduleBurst,
  secondsToFrames,
  type ScheduledSpawn,
} from './waterScheduling'

/**
 * Animated water scene rendered on a full-viewport canvas behind all page
 * content (z-index below the app shell, above the CSS base gradient).
 *
 * Every element is event-based: spawned at random intervals with a random
 * lifespan. Elements drift across the surface; when one fully leaves the
 * viewport it re-enters at the antipodal point. Once its frame-based lifespan
 * expires it plays a kind-specific retirement animation and no longer wraps.
 *
 * Scene inventory:
 * - reflections (both themes): broad soft light patches drifting slowly
 * - lily pads (both themes): green, floating above the water
 * - fish (both themes): top-down, multi-colored with shaded flanks, pectoral
 *   fins and a swishing tail blade; each tail stroke thrusts and jerks them
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
  bornFrame: number
  lifespanFrames: number
  /** Random phase used for per-entity variation (color pick, wobble, pulse). */
  seed: number
  /** Once set, the entity continues moving while its kind-specific exit animation plays. */
  despawnFrame?: number
  /** Stable fish mottling generated once rather than on every rendered frame. */
  fishSpots?: Array<{ x: number; y: number; radius: number }>
  /** Stable body outline generated once rather than tracing Béziers every frame. */
  fishBodyPath?: Path2D
  /** Firefly steering state: random-walk heading and dart window. */
  wanderAngle?: number
  dartUntil?: number
  /** Fish swimming state: heading, cruise speed, tail oscillator, turn drift. */
  heading?: number
  cruiseSpeed?: number
  tailPhase?: number
  tailRate?: number
  headingDrift?: number
  nextTurnAt?: number
}

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

/** Mix only the RGB channels, preserving the base alpha — used for shading. */
function shade(color: Rgba, toward: Rgba, t: number): Rgba {
  return { ...mix(color, toward, t), a: color.a }
}

/** Small deterministic PRNG used once at spawn time for stable entity patterning. */
function seededRandom(seed: number): () => number {
  let state = (Math.floor(seed * 1000) % 4294967296) + 1
  return () => {
    state = (state * 1664525 + 1013904223) % 4294967296
    return state / 4294967296
  }
}

const WHITE: Rgba = { r: 255, g: 255, b: 255, a: 1 }
const BLACK: Rgba = { r: 0, g: 0, b: 0, a: 1 }

interface WaterPalette {
  reflections: [Rgba, Rgba, Rgba]
  lily: Rgba
  lilyHighlight: Rgba
  lilyRim: Rgba
  lilyShadow: Rgba
  fish: Array<{ spine: Rgba; flank: Rgba; fin: Rgba; spot: Rgba }>
  droplet: Rgba
  firefly: Rgba
  fireflyCore: Rgba
  fog: Rgba
}

function readPalette(): WaterPalette {
  const styles = getComputedStyle(document.documentElement)
  const token = (name: string) => parseColor(styles.getPropertyValue(name))
  const lily = token('--water-lily')
  const firefly = token('--water-firefly')
  const fishColors = [
    token('--water-fish-a'),
    token('--water-fish-b'),
    token('--water-fish-c'),
    token('--water-fish-d'),
  ]
  return {
    reflections: [
      token('--water-reflection-a'),
      token('--water-reflection-b'),
      token('--water-reflection-c'),
    ],
    lily,
    lilyHighlight: mix(lily, WHITE, 0.22),
    lilyRim: token('--water-lily-rim'),
    lilyShadow: token('--water-lily-shadow'),
    fish: fishColors.map((base) => ({
      spine: shade(base, WHITE, 0.35),
      flank: shade(base, BLACK, 0.18),
      fin: shade(base, BLACK, 0.28),
      spot: shade(base, BLACK, 0.32),
    })),
    droplet: token('--water-droplet'),
    firefly,
    fireflyCore: mix(firefly, WHITE, 0.5),
    fog: token('--water-fog'),
  }
}

interface SpawnRule {
  cap: number
  minimumIntervalSeconds: number
  baseRatePerSecond: number
  minLifespanSeconds: number
  maxLifespanSeconds: number
  maximumBurst: number
  darkOnly?: boolean
}

const SPAWN_RULES: Record<SpawnableKind, SpawnRule> = {
  reflection: { cap: 5, minimumIntervalSeconds: 3, baseRatePerSecond: 0.11, minLifespanSeconds: 50, maxLifespanSeconds: 90, maximumBurst: 1 },
  lily: { cap: 4, minimumIntervalSeconds: 6, baseRatePerSecond: 0.07, minLifespanSeconds: 45, maxLifespanSeconds: 85, maximumBurst: 1 },
  fish: { cap: 8, minimumIntervalSeconds: 2.5, baseRatePerSecond: 0.13, minLifespanSeconds: 30, maxLifespanSeconds: 60, maximumBurst: 4 },
  firefly: { cap: 9, minimumIntervalSeconds: 1.5, baseRatePerSecond: 0.24, minLifespanSeconds: 20, maxLifespanSeconds: 45, maximumBurst: 3, darkOnly: true },
  fog: { cap: 3, minimumIntervalSeconds: 8, baseRatePerSecond: 0.045, minLifespanSeconds: 60, maxLifespanSeconds: 110, maximumBurst: 1, darkOnly: true },
}

const SPAWNABLE_KINDS = Object.keys(SPAWN_RULES) as SpawnableKind[]

const DROPLET_CAP = 12
const DROPLET_LIFESPAN_FRAMES = secondsToFrames(2.6)
const FADE_IN_FRAMES = secondsToFrames(1.2)
const DESPAWN_FRAMES: Record<EntityKind, number> = {
  reflection: secondsToFrames(1),
  lily: secondsToFrames(1.2),
  fish: secondsToFrames(1),
  firefly: secondsToFrames(0.8),
  fog: secondsToFrames(2),
  droplet: secondsToFrames(0.4),
}
const MAX_CANVAS_PIXELS = 8_000_000
const RADIAL_SPRITE_SIZE = 128
const INTERACTION_SURFACE_SELECTOR = [
  '.auth-card',
  '.verify-card',
  '.dashboard-card',
  '.app-header',
  '.chat-sidebar',
  '.chat-room-panel',
  '.chat-composer-wrap',
  '.chat-preview-panel',
  '.get-roles-panel',
  '.server-maintenance-nav',
  '.server-page-card',
  '.sm-panel',
  '.user-role-detail',
  '.modal-panel',
  '.confirm-modal',
  'form',
  'input',
  'textarea',
  'select',
  'button',
].join(',')
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
  private dpr = 1
  private rafId = 0
  private resizeRafId = 0
  private lastFrameAt = 0
  private accumulatedMs = 0
  private frame = 0
  private running = false
  private lastApiDropletAt = 0
  private readonly pendingSpawns: ScheduledSpawn<SpawnableKind>[] = []
  private readonly spawners = Object.fromEntries(
    SPAWNABLE_KINDS.map((kind) => {
      const rule = SPAWN_RULES[kind]
      return [kind, new AmbientSpawner({
        minimumIntervalSeconds: rule.minimumIntervalSeconds,
        baseRatePerSecond: rule.baseRatePerSecond,
        maximumBurst: rule.maximumBurst,
      })]
    }),
  ) as Record<SpawnableKind, AmbientSpawner>
  private readonly dropletSpawner = new AmbientSpawner({
    minimumIntervalSeconds: 4,
    baseRatePerSecond: 0.12,
    maximumBurst: 1,
  })
  private readonly radialSprites = new Map<string, HTMLCanvasElement>()
  private readonly themeObserver: MutationObserver
  private readonly handleResize = () => {
    if (this.resizeRafId !== 0) return
    this.resizeRafId = requestAnimationFrame(() => {
      this.resizeRafId = 0
      this.resize()
    })
  }
  private readonly handleVisibilityChange = () => {
    if (document.hidden) {
      cancelAnimationFrame(this.rafId)
      this.rafId = 0
      return
    }
    if (this.running && this.rafId === 0) {
      this.lastFrameAt = performance.now()
      this.accumulatedMs = 0
      this.rafId = requestAnimationFrame(this.renderFrame)
    }
  }
  private readonly handleApiDroplet = () => this.spawnApiDroplet()
  private readonly renderFrame = (now: number) => {
    if (!this.running || document.hidden) {
      this.rafId = 0
      return
    }
    const elapsedMs = Math.min(250, now - this.lastFrameAt)
    this.lastFrameAt = now
    this.accumulatedMs += elapsedMs

    const stepCount = Math.min(3, Math.floor(this.accumulatedMs / SCENE_FRAME_MS))
    for (let index = 0; index < stepCount; index++) {
      this.frame++
      this.step(this.frame, 1 / SCENE_FPS)
    }
    if (stepCount > 0) {
      this.accumulatedMs -= stepCount * SCENE_FRAME_MS
      this.draw(this.frame)
    }
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
    document.addEventListener('visibilitychange', this.handleVisibilityChange)
    document.addEventListener(API_MUTATION_EVENT, this.handleApiDroplet)
    this.themeObserver.observe(document.documentElement, { attributes: true, attributeFilter: ['data-theme'] })

    // Seed the scene so it never starts empty; backdated frame ages stagger the lifetimes.
    this.seedInitial(this.frame)
    for (const kind of SPAWNABLE_KINDS)
      this.spawners[kind].reset(this.count(kind), SPAWN_RULES[kind].cap)
    this.dropletSpawner.reset(0, DROPLET_CAP)

    this.running = true
    this.lastFrameAt = performance.now()
    this.rafId = requestAnimationFrame(this.renderFrame)
  }

  destroy(): void {
    this.running = false
    cancelAnimationFrame(this.rafId)
    cancelAnimationFrame(this.resizeRafId)
    window.removeEventListener('resize', this.handleResize)
    document.removeEventListener('visibilitychange', this.handleVisibilityChange)
    document.removeEventListener(API_MUTATION_EVENT, this.handleApiDroplet)
    this.themeObserver.disconnect()
  }

  private onThemeChange(): void {
    this.dark = document.documentElement.getAttribute('data-theme') === 'dark'
    this.palette = readPalette()
    this.radialSprites.clear()
    if (!this.dark) {
      for (const entity of this.entities) {
        if (entity.kind === 'droplet') continue
        if (SPAWN_RULES[entity.kind as SpawnableKind].darkOnly)
          this.beginDespawn(entity)
      }
    } else {
      this.spawners.firefly.reset(this.count('firefly'), SPAWN_RULES.firefly.cap)
      this.spawners.fog.reset(this.count('fog'), SPAWN_RULES.fog.cap)
    }
  }

  private resize(): void {
    const width = window.innerWidth
    const height = window.innerHeight
    const deviceDpr = window.devicePixelRatio || 1
    const pixelBudgetDpr = Math.sqrt(MAX_CANVAS_PIXELS / Math.max(1, width * height))
    const dpr = Math.max(1, Math.min(1.75, deviceDpr, pixelBudgetDpr))
    if (width === this.width && height === this.height && Math.abs(dpr - this.dpr) < 0.01) return

    this.width = width
    this.height = height
    this.dpr = dpr
    this.canvas.width = Math.round(width * dpr)
    this.canvas.height = Math.round(height * dpr)
    this.ctx.setTransform(dpr, 0, 0, dpr, 0, 0)
  }

  private count(kind: EntityKind): number {
    let total = 0
    for (const entity of this.entities) if (entity.kind === kind) total++
    return total
  }

  private randomPoint(margin: number): Vec2 {
    const horizontalMargin = Math.min(margin, this.width / 2)
    const verticalMargin = Math.min(margin, this.height / 2)
    return {
      x: rand(horizontalMargin, Math.max(horizontalMargin, this.width - horizontalMargin)),
      y: rand(verticalMargin, Math.max(verticalMargin, this.height - verticalMargin)),
    }
  }

  private spatialPoint(margin: number): Vec2 {
    let fallback = this.randomPoint(margin)
    for (let attempt = 0; attempt < 12; attempt++) {
      const candidate = this.randomPoint(margin)
      fallback = candidate
      const normalizedX = candidate.x / Math.max(1, this.width)
      const normalizedY = candidate.y / Math.max(1, this.height)
      const depth = 0.2 + 0.8 * normalizedY * normalizedY
      const centerAvoidance = 0.65 + 0.35 * Math.abs(normalizedX - 0.5) * 2
      if (Math.random() <= depth * centerAvoidance && !this.isInteractionSurfaceAt(candidate.x, candidate.y))
        return candidate
    }
    return fallback
  }

  private scheduledPoint(spawn: ScheduledSpawn<SpawnableKind> | undefined, margin: number, weighted = false): Vec2 {
    if (spawn?.anchorX === undefined || spawn.anchorY === undefined)
      return weighted ? this.spatialPoint(margin) : this.randomPoint(margin)
    const horizontalMargin = Math.min(margin, this.width / 2)
    const verticalMargin = Math.min(margin, this.height / 2)
    const candidate = {
      x: Math.max(horizontalMargin, Math.min(this.width - horizontalMargin, spawn.anchorX + rand(-24, 24))),
      y: Math.max(verticalMargin, Math.min(this.height - verticalMargin, spawn.anchorY + rand(-18, 18))),
    }
    return this.isInteractionSurfaceAt(candidate.x, candidate.y)
      ? this.spatialPoint(margin)
      : candidate
  }

  private radialSprite(
    key: string,
    stops: ReadonlyArray<readonly [offset: number, color: Rgba, alpha: number]>,
  ): HTMLCanvasElement {
    const cached = this.radialSprites.get(key)
    if (cached) return cached

    const sprite = document.createElement('canvas')
    sprite.width = RADIAL_SPRITE_SIZE
    sprite.height = RADIAL_SPRITE_SIZE
    const spriteContext = sprite.getContext('2d')
    if (!spriteContext) return sprite

    const center = RADIAL_SPRITE_SIZE / 2
    const gradient = spriteContext.createRadialGradient(center, center, 0, center, center, center)
    for (const [offset, color, alpha] of stops)
      gradient.addColorStop(offset, rgba(color, alpha))
    spriteContext.fillStyle = gradient
    spriteContext.fillRect(0, 0, RADIAL_SPRITE_SIZE, RADIAL_SPRITE_SIZE)
    this.radialSprites.set(key, sprite)
    return sprite
  }

  private createFishBodyPath(radius: number): Path2D {
    const length = radius * 2.2
    const width = radius * 0.75
    const path = new Path2D()
    path.moveTo(length, 0)
    path.quadraticCurveTo(length * 0.55, -width, -length * 0.1, -width * 0.72)
    path.quadraticCurveTo(-length * 0.75, -width * 0.3, -length * 0.95, 0)
    path.quadraticCurveTo(-length * 0.75, width * 0.3, -length * 0.1, width * 0.72)
    path.quadraticCurveTo(length * 0.55, width, length, 0)
    path.closePath()
    return path
  }

  private seedInitial(frame: number): void {
    const seedKind = (kind: SpawnableKind, amount: number) => {
      for (let index = 0; index < amount; index++) {
        const entity = this.makeEntity(kind, frame)
        entity.bornFrame = frame - Math.round(rand(0, entity.lifespanFrames * 0.4))
        this.entities.push(entity)
      }
    }
    seedKind('reflection', 4)
    seedKind('lily', 2)
    seedKind('fish', 2)
    if (this.dark) {
      seedKind('fog', 2)
      seedKind('firefly', 4)
    }
  }

  private makeEntity(
    kind: SpawnableKind,
    frame: number,
    scheduled?: ScheduledSpawn<SpawnableKind>,
  ): SceneEntity {
    const rule = SPAWN_RULES[kind]
    const lifespanFrames = sampleFrameRange(rule.minLifespanSeconds, rule.maxLifespanSeconds)
    const seed = rand(0, 1000)
    const base = { kind, bornFrame: frame, lifespanFrames, seed }
    switch (kind) {
      case 'reflection': {
        const angle = rand(0, Math.PI * 2)
        const speed = rand(5, 13)
        return { ...base, pos: this.scheduledPoint(scheduled, 60, true), vel: { x: Math.cos(angle) * speed, y: Math.sin(angle) * speed }, radius: rand(150, 320) }
      }
      case 'lily': {
        const angle = rand(0, Math.PI * 2)
        const speed = rand(3, 8)
        return { ...base, pos: this.scheduledPoint(scheduled, 40, true), vel: { x: Math.cos(angle) * speed, y: Math.sin(angle) * speed }, radius: rand(16, 30) }
      }
      case 'fish': {
        const heading = scheduled?.heading ?? rand(0, Math.PI * 2)
        const cruiseSpeed = rand(26, 46)
        const radius = rand(9, 14)
        const length = radius * 2.2
        const width = radius * 0.75
        const spotRandom = seededRandom(seed)
        const spotCount = 2 + Math.floor(spotRandom() * 2)
        const fishSpots = Array.from({ length: spotCount }, () => ({
          x: (spotRandom() * 1.3 - 0.75) * length,
          y: (spotRandom() * 0.9 - 0.45) * width,
          radius: (0.2 + spotRandom() * 0.18) * radius,
        }))
        return {
          ...base,
          pos: this.scheduledPoint(scheduled, 40, true),
          vel: { x: Math.cos(heading) * cruiseSpeed, y: Math.sin(heading) * cruiseSpeed },
          radius,
          heading,
          cruiseSpeed,
          tailPhase: rand(0, Math.PI * 2),
          tailRate: rand(4.5, 7),
          headingDrift: 0,
          nextTurnAt: frame + sampleFrameRange(1, 4),
          fishSpots,
          fishBodyPath: this.createFishBodyPath(radius),
        }
      }
      case 'firefly':
        return { ...base, pos: this.scheduledPoint(scheduled, 30, true), vel: { x: rand(-15, 15), y: rand(-15, 15) }, radius: rand(2.4, 3.4), wanderAngle: rand(0, Math.PI * 2) }
      case 'fog': {
        const direction = Math.random() < 0.5 ? -1 : 1
        return { ...base, pos: this.scheduledPoint(scheduled, 80, true), vel: { x: rand(6, 12) * direction, y: rand(-2, 2) }, radius: rand(260, 430) }
      }
    }
  }

  private makeDroplet(pos: Vec2, frame: number): SceneEntity {
    return {
      kind: 'droplet',
      pos,
      vel: { x: 0, y: 0 },
      radius: rand(26, 54),
      bornFrame: frame,
      lifespanFrames: DROPLET_LIFESPAN_FRAMES,
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
    this.entities.push(this.makeDroplet(pos, this.frame))
  }

  /**
   * Top-down swimming: the tail is the motor. Each tail stroke both thrusts
   * the fish forward (burst-glide speed pulsing at twice the swish frequency)
   * and jerks the nose to the side opposite the swish, so the path is a
   * natural wiggle instead of a straight glide. A slow random turn intent
   * steers the fish over longer distances.
   */
  private steerFish(entity: SceneEntity, frame: number, dt: number): void {
    const tailRate = entity.tailRate ?? 5.5
    entity.tailPhase = (entity.tailPhase ?? 0) + tailRate * dt
    if (entity.nextTurnAt === undefined || frame >= entity.nextTurnAt) {
      entity.headingDrift = rand(-0.35, 0.35)
      entity.nextTurnAt = frame + sampleFrameRange(2, 6)
    }
    // Tail angle is sin(phase); its rate of change, cos(phase), is the stroke.
    const stroke = Math.cos(entity.tailPhase)
    entity.heading =
      (entity.heading ?? 0) + (entity.headingDrift ?? 0) * dt - stroke * 0.1 * tailRate * dt
    const speed = (entity.cruiseSpeed ?? 32) * (0.65 + 0.6 * Math.abs(stroke))
    entity.vel.x = Math.cos(entity.heading ?? 0) * speed
    entity.vel.y = Math.sin(entity.heading ?? 0) * speed
  }

  /** Sporadic firefly flight: a random-walk heading with occasional darts. */
  private steerFirefly(entity: SceneEntity, frame: number, dt: number): void {
    entity.wanderAngle = (entity.wanderAngle ?? rand(0, Math.PI * 2)) + rand(-2.4, 2.4) * dt
    if (entity.dartUntil === undefined || frame > entity.dartUntil) {
      if (Math.random() < dt * 0.4) entity.dartUntil = frame + sampleFrameRange(0.2, 0.5)
    }
    const darting = entity.dartUntil !== undefined && frame < entity.dartUntil
    const speed = darting ? 120 : 26
    const blend = Math.min(1, (darting ? 6 : 2.4) * dt)
    entity.vel.x += (Math.cos(entity.wanderAngle) * speed - entity.vel.x) * blend
    entity.vel.y += (Math.sin(entity.wanderAngle) * speed - entity.vel.y) * blend
  }

  private beginDespawn(entity: SceneEntity): void {
    if (entity.despawnFrame !== undefined) return
    entity.despawnFrame = this.frame
  }

  private despawnProgress(entity: SceneEntity, frame: number): number {
    if (entity.despawnFrame === undefined) return 0
    return Math.min(1, Math.max(0, (frame - entity.despawnFrame) / DESPAWN_FRAMES[entity.kind]))
  }

  private isInteractionSurfaceAt(x: number, y: number): boolean {
    const target = document.elementFromPoint(x, y)
    return target instanceof Element && target.closest(INTERACTION_SURFACE_SELECTOR) !== null
  }

  private step(frame: number, dt: number): void {
    let writeIndex = 0

    for (let readIndex = 0; readIndex < this.entities.length; readIndex++) {
      const entity = this.entities[readIndex]
      if (entity.despawnFrame === undefined && frame - entity.bornFrame >= entity.lifespanFrames)
        this.beginDespawn(entity)

      if (entity.kind === 'droplet') {
        if (
          this.despawnProgress(entity, frame) < 1 &&
          !(entity.despawnFrame !== undefined && this.isInteractionSurfaceAt(entity.pos.x, entity.pos.y))
        )
          this.entities[writeIndex++] = entity
        continue
      }

      if (entity.kind === 'firefly') this.steerFirefly(entity, frame, dt)
      if (entity.kind === 'fish') this.steerFish(entity, frame, dt)
      entity.pos.x += entity.vel.x * dt
      entity.pos.y += entity.vel.y * dt

      const margin = entity.radius * (entity.kind === 'fish' ? 2.4 : 1.1)
      const outside =
        entity.pos.x < -margin ||
        entity.pos.x > this.width + margin ||
        entity.pos.y < -margin ||
        entity.pos.y > this.height + margin

      const despawning = entity.despawnFrame !== undefined

      // Retiring entities may finish naturally in open water, but disappear as
      // soon as their center reaches a foreground surface or viewport edge.
      if (
        despawning &&
        (outside ||
          this.despawnProgress(entity, frame) >= 1 ||
          this.isInteractionSurfaceAt(entity.pos.x, entity.pos.y))
      ) {
        continue
      }

      if (outside) {
        // R(π) = -I about the viewport center. Clamp to the margin so a point
        // that stepped slightly past the boundary cannot wrap back and forth.
        entity.pos.x = Math.max(-margin, Math.min(this.width + margin, this.width - entity.pos.x))
        entity.pos.y = Math.max(-margin, Math.min(this.height + margin, this.height - entity.pos.y))
      }

      this.entities[writeIndex++] = entity
    }

    this.entities.length = writeIndex
    this.spawnDue(frame)
  }

  private spawnDue(frame: number): void {
    for (const kind of SPAWNABLE_KINDS) {
      const rule = SPAWN_RULES[kind]
      if (rule.darkOnly && !this.dark) continue

      let pendingCount = 0
      for (const pending of this.pendingSpawns)
        if (pending.kind === kind) pendingCount++
      const burstSize = this.spawners[kind].update(this.count(kind) + pendingCount, rule.cap)
      if (burstSize === 0) continue

      const anchor =
        kind === 'fish'
          ? { ...this.spatialPoint(40), heading: rand(0, Math.PI * 2) }
          : kind === 'firefly'
            ? this.spatialPoint(30)
            : undefined
      scheduleBurst(this.pendingSpawns, kind, burstSize, anchor)
    }

    for (let index = this.pendingSpawns.length - 1; index >= 0; index--) {
      const pending = this.pendingSpawns[index]
      pending.framesRemaining--
      if (pending.framesRemaining > 0) continue

      const rule = SPAWN_RULES[pending.kind]
      if ((!rule.darkOnly || this.dark) && this.count(pending.kind) < rule.cap) {
        this.entities.push(this.makeEntity(pending.kind, frame, pending))
      }
      this.pendingSpawns.splice(index, 1)
    }

    if (this.dropletSpawner.update(this.count('droplet'), DROPLET_CAP) > 0)
      this.entities.push(this.makeDroplet(this.spatialPoint(30), frame))
  }

  /** 0→1 fade-in after spawn, then a smooth kind-specific fade on retirement. */
  private envelope(entity: SceneEntity, frame: number): number {
    const fadeIn = Math.min(1, Math.max(0, (frame - entity.bornFrame) / FADE_IN_FRAMES))
    const progress = this.despawnProgress(entity, frame)
    const easedProgress = progress * progress * (3 - 2 * progress)
    const fadeOut = 1 - easedProgress
    return fadeIn * fadeOut
  }

  private draw(frame: number): void {
    this.ctx.clearRect(0, 0, this.width, this.height)
    for (const kind of DRAW_ORDER)
      for (const entity of this.entities)
        if (entity.kind === kind) this.drawEntity(entity, frame)
  }

  private drawEntity(entity: SceneEntity, frame: number): void {
    const alpha = this.envelope(entity, frame)
    if (alpha <= 0) return
    const progress = this.despawnProgress(entity, frame)
    const easedProgress = progress * progress * (3 - 2 * progress)
    const scale =
      entity.kind === 'fish'
        ? 1 - easedProgress * 0.48
        : entity.kind === 'lily'
          ? 1 - easedProgress * 0.28
          : 1

    this.ctx.save()
    if (scale !== 1) {
      this.ctx.translate(entity.pos.x, entity.pos.y)
      this.ctx.scale(scale, scale)
      this.ctx.translate(-entity.pos.x, -entity.pos.y)
    }
    switch (entity.kind) {
      case 'reflection':
        this.drawReflection(entity, alpha)
        break
      case 'lily':
        this.drawLily(entity, frame, alpha)
        break
      case 'fish':
        this.drawFish(entity, alpha)
        break
      case 'droplet':
        this.drawDroplet(entity, frame, alpha)
        break
      case 'fog':
        this.drawFog(entity, alpha)
        break
      case 'firefly':
        this.drawFirefly(entity, frame, alpha)
        break
    }
    this.ctx.restore()
  }

  private drawReflection(entity: SceneEntity, alpha: number): void {
    const { ctx } = this
    const colorIndex = Math.floor(entity.seed) % 3
    const color = this.palette.reflections[colorIndex]
    const sprite = this.radialSprite(`reflection-${colorIndex}`, [
      [0, color, 1],
      [1, color, 0],
    ])
    ctx.save()
    // Additive blending in dark mode makes reflections read as light gathering
    // on the water instead of grey patches.
    if (this.dark) ctx.globalCompositeOperation = 'lighter'
    ctx.globalAlpha = alpha
    ctx.drawImage(
      sprite,
      entity.pos.x - entity.radius,
      entity.pos.y - entity.radius,
      entity.radius * 2,
      entity.radius * 2,
    )
    ctx.restore()
  }

  /** The pad silhouette: a circle with a notch wedge. */
  private traceLilyPad(radius: number, offsetX = 0, offsetY = 0): void {
    const notchHalf = 0.30
    this.ctx.beginPath()
    this.ctx.moveTo(offsetX, offsetY)
    this.ctx.arc(offsetX, offsetY, radius, notchHalf, Math.PI * 2 - notchHalf)
    this.ctx.closePath()
  }

  private drawLily(entity: SceneEntity, frame: number, alpha: number): void {
    const { ctx } = this
    const r = entity.radius
    ctx.save()
    ctx.translate(entity.pos.x, entity.pos.y)

    // Slow floating wobble (ctx.rotate applies the same R(θ) rotation matrix).
    const rotation = entity.seed + Math.sin(frame / (SCENE_FPS * 3) + entity.seed) * 0.08
    ctx.rotate(rotation)

    // Soft shadow with the same notched silhouette as the pad, cast toward the
    // lower right. The offset is fixed in screen space (the light does not
    // rotate with the pad), so counter-rotate it into pad space with R(−θ).
    const shadowX = 2.5 * Math.cos(rotation) + 3.5 * Math.sin(rotation)
    const shadowY = -2.5 * Math.sin(rotation) + 3.5 * Math.cos(rotation)
    ctx.fillStyle = rgba(this.palette.lilyShadow, alpha)
    this.traceLilyPad(r * 1.03, shadowX, shadowY)
    ctx.fill()

    const sprite = this.radialSprite('lily', [
      [0, this.palette.lilyHighlight, 1],
      [1, this.palette.lily, 1],
    ])
    this.traceLilyPad(r)
    ctx.save()
    ctx.clip()
    ctx.globalAlpha = alpha
    ctx.drawImage(sprite, -r, -r, r * 2, r * 2)
    ctx.restore()
    this.traceLilyPad(r)
    ctx.strokeStyle = rgba(this.palette.lilyRim, alpha * 0.85)
    ctx.lineWidth = 1.2
    ctx.stroke()
    ctx.restore()
  }

  /**
   * Top-down fish: a teardrop body shaded lighter along the spine (where the
   * light from above catches it) and darker on the flanks, paired swept-back
   * pectoral fins, seeded mottling so each fish is patterned differently, and
   * a narrow caudal blade swinging about the peduncle — from above the tail
   * reads as a slim strip, never a "V".
   */
  private drawFish(entity: SceneEntity, alpha: number): void {
    const { ctx } = this
    const fishStyle = this.palette.fish[Math.floor(entity.seed) % this.palette.fish.length]
    const { spine, flank, fin, spot: spotColor } = fishStyle
    const tailAngle = Math.sin(entity.tailPhase ?? 0) * 0.55

    const r = entity.radius
    const length = r * 2.2 // nose-to-peduncle half length
    const width = r * 0.75 // half width at the widest point
    const bodyPath = entity.fishBodyPath ?? this.createFishBodyPath(r)

    ctx.save()
    ctx.translate(entity.pos.x, entity.pos.y)
    // Heading — ctx.rotate is the same R(θ) rotation matrix.
    ctx.rotate(entity.heading ?? Math.atan2(entity.vel.y, entity.vel.x))

    // Pectoral fins: paired, swept back, flexing gently against the tail beat.
    ctx.fillStyle = rgba(fin, alpha * 0.85)
    for (const side of [-1, 1] as const) {
      ctx.save()
      ctx.translate(length * 0.25, side * width * 0.8)
      ctx.rotate(side * (0.9 - tailAngle * side * 0.15))
      ctx.beginPath()
      ctx.ellipse(0, 0, r * 0.7, r * 0.28, 0, 0, Math.PI * 2)
      ctx.fill()
      ctx.restore()
    }

    // Caudal blade, swinging about the peduncle: slim, slightly flared tip.
    ctx.save()
    ctx.translate(-length * 0.95, 0)
    ctx.rotate(tailAngle)
    ctx.fillStyle = rgba(fin, alpha * 0.9)
    ctx.beginPath()
    ctx.moveTo(0, -r * 0.12)
    ctx.quadraticCurveTo(-length * 0.35, -r * 0.3, -length * 0.55, -r * 0.22)
    ctx.lineTo(-length * 0.5, 0)
    ctx.lineTo(-length * 0.55, r * 0.22)
    ctx.quadraticCurveTo(-length * 0.35, r * 0.3, 0, r * 0.12)
    ctx.closePath()
    ctx.fill()
    ctx.restore()

    // Body with cross-body shading: flank → spine highlight → flank.
    const gradient = ctx.createLinearGradient(0, -width, 0, width)
    gradient.addColorStop(0, rgba(flank, alpha))
    gradient.addColorStop(0.5, rgba(spine, alpha))
    gradient.addColorStop(1, rgba(flank, alpha))
    ctx.fillStyle = gradient
    ctx.fill(bodyPath)

    // Spawn-cached mottling, clipped to the spawn-cached body path.
    ctx.clip(bodyPath)
    ctx.fillStyle = rgba(spotColor, alpha * 0.5)
    for (const spot of entity.fishSpots ?? []) {
      ctx.beginPath()
      ctx.ellipse(spot.x, spot.y, spot.radius * 1.6, spot.radius, 0, 0, Math.PI * 2)
      ctx.fill()
    }
    ctx.restore()
  }

  private drawDroplet(entity: SceneEntity, frame: number, alpha: number): void {
    const { ctx } = this
    const t = Math.min(1, Math.max(0, (frame - entity.bornFrame) / entity.lifespanFrames))
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
    const sprite = this.radialSprite('fog', [
      [0, this.palette.fog, 1],
      [1, this.palette.fog, 0],
    ])
    ctx.save()
    ctx.translate(entity.pos.x, entity.pos.y)
    ctx.scale(1.6, 1) // mist banks stretch along the surface
    ctx.globalAlpha = alpha
    ctx.drawImage(sprite, -entity.radius, -entity.radius, entity.radius * 2, entity.radius * 2)
    ctx.restore()
  }

  private drawFirefly(entity: SceneEntity, frame: number, alpha: number): void {
    const { ctx } = this
    const pulse = 0.5 + 0.5 * Math.sin((frame / SCENE_FPS) * 4 + entity.seed)
    const speed = Math.hypot(entity.vel.x, entity.vel.y)
    // Bloom grows with movement over the water: darting fireflies flare gold.
    const intensity = Math.min(1, 0.3 + pulse * 0.35 + speed / 160)
    const glowRadius = entity.radius * (3.5 + 4.5 * intensity)
    const sprite = this.radialSprite('firefly', [
      [0, this.palette.firefly, 1],
      [0.4, this.palette.firefly, 0.35],
      [1, this.palette.firefly, 0],
    ])
    ctx.save()
    ctx.globalCompositeOperation = 'lighter' // additive: gold mixes into the water blue
    ctx.globalAlpha = alpha * intensity
    ctx.drawImage(
      sprite,
      entity.pos.x - glowRadius,
      entity.pos.y - glowRadius,
      glowRadius * 2,
      glowRadius * 2,
    )
    ctx.globalAlpha = 1
    ctx.fillStyle = rgba(this.palette.fireflyCore, alpha * (0.5 + 0.5 * intensity))
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
