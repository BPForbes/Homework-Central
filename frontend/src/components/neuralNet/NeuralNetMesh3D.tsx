import { useEffect, useRef, useState } from 'react'

export type MeshPathTone = 'forward' | 'reeval' | 'backprop' | 'accepted' | 'revision' | 'idle'

export type NeuralMeshFrame = {
  pathTone: MeshPathTone
  /** Global node indexes (concatenated layers) that should light up this frame. */
  activeNodeIndexes: number[]
  /**
   * Dense edge keys `layer:source:target` that should light up this frame.
   * When empty, every edge inherits a muted tone while nodes still color.
   */
  activeEdgeKeys: string[]
}

type Point3 = { x: number; y: number; z: number }
type Node3 = Point3 & { index: number; layer: number; local: number }
type Edge3 = { key: string; layer: number; source: number; target: number; a: number; b: number }

/** World units — shared by training + visualizer so architecture grows the mesh, not packing. */
export const MESH_NODE_GAP = 36
export const MESH_LAYER_GAP = 110

type Props = {
  layerWidths: number[]
  layerLabels?: string[]
  frame: NeuralMeshFrame
  title?: string
  className?: string
}

function toneStroke(tone: MeshPathTone, active: boolean): { stroke: string; alpha: number; width: number } {
  if (!active || tone === 'idle') return { stroke: 'var(--color-primary)', alpha: 0.08, width: 0.8 }
  if (tone === 'forward' || tone === 'accepted') return { stroke: 'var(--color-success)', alpha: 0.95, width: 2.2 }
  if (tone === 'reeval') return { stroke: 'var(--color-warning)', alpha: 0.95, width: 2.2 }
  if (tone === 'backprop' || tone === 'revision') return { stroke: 'var(--color-danger)', alpha: 0.92, width: 2.1 }
  return { stroke: 'var(--color-primary)', alpha: 0.2, width: 1 }
}

function toneFill(tone: MeshPathTone, active: boolean): { fill: string; alpha: number } {
  if (!active || tone === 'idle') return { fill: 'var(--color-surface-alt)', alpha: 0.55 }
  if (tone === 'forward' || tone === 'accepted') return { fill: 'var(--color-success)', alpha: 0.95 }
  if (tone === 'reeval') return { fill: 'var(--color-warning)', alpha: 0.95 }
  if (tone === 'backprop' || tone === 'revision') return { fill: 'var(--color-danger)', alpha: 0.95 }
  return { fill: 'var(--color-primary)', alpha: 0.7 }
}

function cssColor(variable: string, fallback: string): string {
  if (typeof window === 'undefined') return fallback
  const value = getComputedStyle(document.documentElement).getPropertyValue(variable).trim()
  return value || fallback
}

function buildMesh(layerWidths: number[]): { nodes: Node3[]; edges: Edge3[] } {
  const nodes: Node3[] = []
  let global = 0
  for (let layer = 0; layer < layerWidths.length; layer++) {
    const count = Math.max(0, layerWidths[layer] ?? 0)
    // 3D lattice in the YZ plane so deep layers become an explorable volume, not a flat bar.
    const columns = Math.max(1, Math.ceil(Math.sqrt(count)))
    const rows = Math.max(1, Math.ceil(count / columns))
    const x = layer * MESH_LAYER_GAP
    for (let local = 0; local < count; local++) {
      const col = local % columns
      const row = Math.floor(local / columns)
      const y = (col - (columns - 1) / 2) * MESH_NODE_GAP
      const z = (row - (rows - 1) / 2) * MESH_NODE_GAP
      nodes.push({ index: global, layer, local, x, y, z })
      global += 1
    }
  }

  const edges: Edge3[] = []
  let offset = 0
  for (let layer = 0; layer < layerWidths.length - 1; layer++) {
    const sourceCount = layerWidths[layer]
    const targetCount = layerWidths[layer + 1]
    const sourceOffset = offset
    const targetOffset = offset + sourceCount
    for (let source = 0; source < sourceCount; source++) {
      for (let target = 0; target < targetCount; target++) {
        edges.push({
          key: `${layer}:${source}:${target}`,
          layer,
          source,
          target,
          a: sourceOffset + source,
          b: targetOffset + target,
        })
      }
    }
    offset += sourceCount
  }

  return { nodes, edges }
}

function project(
  point: Point3,
  yaw: number,
  pitch: number,
  zoom: number,
  width: number,
  height: number,
): { x: number; y: number; depth: number } {
  const cosY = Math.cos(yaw)
  const sinY = Math.sin(yaw)
  const cosP = Math.cos(pitch)
  const sinP = Math.sin(pitch)
  // Center mesh roughly around origin before rotating.
  const x1 = point.x * cosY - point.z * sinY
  const z1 = point.x * sinY + point.z * cosY
  const y1 = point.y * cosP - z1 * sinP
  const z2 = point.y * sinP + z1 * cosP
  const perspective = 700
  const depth = z2
  const scale = (perspective / (perspective + depth + 220)) * zoom
  return {
    x: width / 2 + x1 * scale,
    y: height / 2 + y1 * scale,
    depth,
  }
}

export function NeuralNetMesh3D({ layerWidths, layerLabels, frame, title, className }: Props) {
  const shellRef = useRef<HTMLDivElement | null>(null)
  const canvasRef = useRef<HTMLCanvasElement | null>(null)
  const [size, setSize] = useState({ width: 720, height: 420 })
  const orbitRef = useRef({ yaw: 0.55, pitch: 0.35, zoom: 1 })
  const dragRef = useRef<{ x: number; y: number; yaw: number; pitch: number } | null>(null)
  const mesh = useRef(buildMesh(layerWidths))

  useEffect(() => {
    mesh.current = buildMesh(layerWidths)
  }, [layerWidths])

  useEffect(() => {
    const shell = shellRef.current
    if (!shell) return
    const observe = new ResizeObserver((entries) => {
      const entry = entries[0]
      if (!entry) return
      const width = Math.max(320, Math.floor(entry.contentRect.width))
      // Window scales with architecture volume while staying usable on Chromebooks.
      const nodeCount = layerWidths.reduce((sum, widthValue) => sum + widthValue, 0)
      const depthHint = Math.max(...layerWidths.map((widthValue) => Math.ceil(Math.sqrt(widthValue))), 1)
      const height = Math.min(720, Math.max(360, 280 + depthHint * 18 + Math.min(180, nodeCount * 0.15)))
      setSize({ width, height })
    })
    observe.observe(shell)
    return () => observe.disconnect()
  }, [layerWidths])

  useEffect(() => {
    const canvas = canvasRef.current
    if (!canvas) return
    const context = canvas.getContext('2d')
    if (!context) return

    const dpr = Math.min(window.devicePixelRatio || 1, 2)
    canvas.width = Math.floor(size.width * dpr)
    canvas.height = Math.floor(size.height * dpr)
    canvas.style.width = `${size.width}px`
    canvas.style.height = `${size.height}px`
    context.setTransform(dpr, 0, 0, dpr, 0, 0)

    const { nodes, edges } = mesh.current
    const activeNodes = new Set(frame.activeNodeIndexes)
    const activeEdges = new Set(frame.activeEdgeKeys)
    const hasEdgeSelection = activeEdges.size > 0
    const { yaw, pitch, zoom } = orbitRef.current

    // Center architecture in world space before projecting.
    const maxX = Math.max(0, (layerWidths.length - 1) * MESH_LAYER_GAP)
    const centered = nodes.map((node) => ({
      ...node,
      x: node.x - maxX / 2,
    }))
    const projected = centered.map((node) => ({
      node,
      screen: project(node, yaw, pitch, zoom, size.width, size.height),
    }))

    context.clearRect(0, 0, size.width, size.height)
    const surface = cssColor('--color-surface-sunken', '#0d2130')
    context.fillStyle = surface
    context.fillRect(0, 0, size.width, size.height)

    // Soft ground grid so the 3D volume reads clearly.
    context.strokeStyle = cssColor('--color-border', '#2a4a5c')
    context.globalAlpha = 0.25
    context.lineWidth = 1
    for (let grid = -4; grid <= 4; grid++) {
      const left = project({ x: -maxX / 2 - 40, y: 140, z: grid * MESH_NODE_GAP }, yaw, pitch, zoom, size.width, size.height)
      const right = project({ x: maxX / 2 + 40, y: 140, z: grid * MESH_NODE_GAP }, yaw, pitch, zoom, size.width, size.height)
      context.beginPath()
      context.moveTo(left.x, left.y)
      context.lineTo(right.x, right.y)
      context.stroke()
    }
    context.globalAlpha = 1

    const idleStride = edges.length > 8000 ? 7 : edges.length > 3000 ? 3 : 1
    const success = cssColor('--color-success', '#2f8f5b')
    const warning = cssColor('--color-warning', '#b8860b')
    const danger = cssColor('--color-danger', '#c1392b')
    const primary = cssColor('--color-primary', '#176f91')

    const resolveStroke = (tone: MeshPathTone, active: boolean) => {
      const style = toneStroke(tone, active)
      const color =
        style.stroke.includes('success') ? success
          : style.stroke.includes('warning') ? warning
            : style.stroke.includes('danger') ? danger
              : primary
      return { ...style, color }
    }

    for (let index = 0; index < edges.length; index++) {
      const edge = edges[index]
      const onPath = !hasEdgeSelection || activeEdges.has(edge.key)
      if (!onPath && index % idleStride !== 0) continue
      const from = projected[edge.a]?.screen
      const to = projected[edge.b]?.screen
      if (!from || !to) continue
      const style = resolveStroke(frame.pathTone, onPath && frame.pathTone !== 'idle')
      context.beginPath()
      context.strokeStyle = style.color
      context.globalAlpha = style.alpha
      context.lineWidth = style.width
      if (frame.pathTone === 'backprop' && onPath) context.setLineDash([5, 4])
      else context.setLineDash([])
      // Light curve in screen space so dense fans separate.
      const midX = (from.x + to.x) / 2
      const bend = ((edge.source + edge.target) % 7 - 3) * 2.2
      context.moveTo(from.x, from.y)
      context.quadraticCurveTo(midX, (from.y + to.y) / 2 + bend, to.x, to.y)
      context.stroke()
    }
    context.setLineDash([])
    context.globalAlpha = 1

    const sortedNodes = [...projected].sort((left, right) => right.screen.depth - left.screen.depth)
    for (const item of sortedNodes) {
      const active = activeNodes.size === 0 ? frame.pathTone !== 'idle' : activeNodes.has(item.node.index)
      const style = toneFill(frame.pathTone, active)
      const color =
        style.fill.includes('success') ? success
          : style.fill.includes('warning') ? warning
            : style.fill.includes('danger') ? danger
              : style.fill.includes('surface') ? cssColor('--color-surface-alt', '#123044')
                : primary
      const radius = item.node.layer === 0 ? 3.4 : item.node.layer === layerWidths.length - 1 ? 4.4 : 3.9
      context.beginPath()
      context.fillStyle = color
      context.globalAlpha = style.alpha
      context.arc(item.screen.x, item.screen.y, radius, 0, Math.PI * 2)
      context.fill()
      context.globalAlpha = 1
      context.strokeStyle = cssColor('--color-border-strong', '#3d6a7e')
      context.lineWidth = 1
      context.stroke()
    }

    if (layerLabels?.length) {
      context.fillStyle = cssColor('--color-ink-secondary', '#afd5e5')
      context.font = '12px var(--font-sans), sans-serif'
      context.textAlign = 'center'
      for (let layer = 0; layer < layerWidths.length; layer++) {
        const label = layerLabels[layer] ?? `layer ${layer}`
        const anchor = project({ x: layer * MESH_LAYER_GAP - maxX / 2, y: -160, z: 0 }, yaw, pitch, zoom, size.width, size.height)
        context.fillText(label.replace(/-/g, ' '), anchor.x, anchor.y)
      }
    }
  }, [size, frame, layerWidths, layerLabels])

  useEffect(() => {
    const canvas = canvasRef.current
    if (!canvas) return

    const onPointerDown = (event: PointerEvent) => {
      dragRef.current = {
        x: event.clientX,
        y: event.clientY,
        yaw: orbitRef.current.yaw,
        pitch: orbitRef.current.pitch,
      }
      canvas.setPointerCapture(event.pointerId)
    }
    const onPointerMove = (event: PointerEvent) => {
      const drag = dragRef.current
      if (!drag) return
      orbitRef.current.yaw = drag.yaw + (event.clientX - drag.x) * 0.008
      orbitRef.current.pitch = Math.max(-1.2, Math.min(1.2, drag.pitch + (event.clientY - drag.y) * 0.006))
      // Force redraw via size noop state bump is avoided — mutate + redraw through rAF.
      requestAnimationFrame(() => {
        setSize((current) => ({ ...current }))
      })
    }
    const onPointerUp = (event: PointerEvent) => {
      dragRef.current = null
      if (canvas.hasPointerCapture(event.pointerId)) canvas.releasePointerCapture(event.pointerId)
    }
    const onWheel = (event: WheelEvent) => {
      event.preventDefault()
      orbitRef.current.zoom = Math.max(0.45, Math.min(2.8, orbitRef.current.zoom * (event.deltaY > 0 ? 0.92 : 1.08)))
      setSize((current) => ({ ...current }))
    }

    canvas.addEventListener('pointerdown', onPointerDown)
    canvas.addEventListener('pointermove', onPointerMove)
    canvas.addEventListener('pointerup', onPointerUp)
    canvas.addEventListener('pointercancel', onPointerUp)
    canvas.addEventListener('wheel', onWheel, { passive: false })
    return () => {
      canvas.removeEventListener('pointerdown', onPointerDown)
      canvas.removeEventListener('pointermove', onPointerMove)
      canvas.removeEventListener('pointerup', onPointerUp)
      canvas.removeEventListener('pointercancel', onPointerUp)
      canvas.removeEventListener('wheel', onWheel)
    }
  }, [])

  const nodeCount = layerWidths.reduce((sum, width) => sum + width, 0)
  const edgeCount = layerWidths.slice(0, -1).reduce(
    (sum, width, index) => sum + width * (layerWidths[index + 1] ?? 0),
    0,
  )

  return (
    <div ref={shellRef} className={`neural-mesh3d ${className ?? ''}`.trim()}>
      <div className="neural-mesh3d-toolbar">
        <strong>{title ?? '3D neural mesh'}</strong>
        <span>
          {layerWidths.join(' → ')} · {nodeCount} nodes · {edgeCount} edges · drag to orbit · scroll to zoom
        </span>
      </div>
      <canvas
        ref={canvasRef}
        className="neural-mesh3d-canvas"
        role="img"
        aria-label="Explorable three-dimensional neural network mesh"
      />
      <p className="dashboard-hint neural-mesh3d-hint">
        Fixed spacing ({MESH_NODE_GAP}px node / {MESH_LAYER_GAP}px layer). Explore the volume — deep layers are
        lattices in Z, not a packed 2D strip. Colors update every training/replay frame.
      </p>
    </div>
  )
}

/** Build dense edge keys for active topology parameter indexes when edges are fully connected. */
export function edgeKeysFromDenseParameterIndexes(
  layerWidths: number[],
  parameterIndexes: Iterable<number>,
): string[] {
  const keys: string[] = []
  const wanted = new Set(parameterIndexes)
  let parameterCursor = 0
  for (let layer = 0; layer < layerWidths.length - 1; layer++) {
    const sources = layerWidths[layer]
    const targets = layerWidths[layer + 1]
    for (let target = 0; target < targets; target++) {
      for (let source = 0; source < sources; source++) {
        if (wanted.has(parameterCursor)) keys.push(`${layer}:${source}:${target}`)
        parameterCursor += 1
      }
      // Bias parameter sits after each target's weights — skip it for edge lighting.
      parameterCursor += 1
    }
  }
  return keys
}
