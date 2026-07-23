import { useEffect, useMemo, useRef, useState } from 'react'
import {
  type MeshDrawCommand,
  type MeshPathTone,
  type MeshProjectRequest,
  type MeshProjectResponse,
  type MeshViewMode,
  type SlicePerspective,
} from './meshProject.worker'

export type { MeshPathTone }

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

const EMPTY_LABELS: string[] = []

function cssColor(variable: string, fallback: string): string {
  if (typeof window === 'undefined') {
    return fallback
  }
  const value = getComputedStyle(document.documentElement).getPropertyValue(variable).trim()
  return value || fallback
}

function readThemeColors(): MeshProjectRequest['colors'] {
  return {
    surface: cssColor('--color-surface-sunken', '#0d2130'),
    border: cssColor('--color-border', '#2a4a5c'),
    borderStrong: cssColor('--color-border-strong', '#3d6a7e'),
    inkSecondary: cssColor('--color-ink-secondary', '#afd5e5'),
    success: cssColor('--color-success', '#2f8f5b'),
    warning: cssColor('--color-warning', '#b8860b'),
    danger: cssColor('--color-danger', '#c1392b'),
    primary: cssColor('--color-primary', '#176f91'),
    surfaceAlt: cssColor('--color-surface-alt', '#123044'),
  }
}

function createWorker(): Worker {
  return new Worker(new URL('./meshProject.worker.ts', import.meta.url), {
    type: 'module',
  })
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
        if (wanted.has(parameterCursor)) {
          keys.push(`${layer}:${source}:${target}`)
        }
        parameterCursor += 1
      }
      // Bias parameter sits after each target's weights — skip it for edge lighting.
      parameterCursor += 1
    }
  }
  return keys
}

export function NeuralNetMesh3D({ layerWidths, layerLabels, frame, title, className }: Props) {
  const shellRef = useRef<HTMLDivElement | null>(null)
  const canvasRef = useRef<HTMLCanvasElement | null>(null)
  const workerRef = useRef<Worker | null>(null)
  const requestIdRef = useRef(0)
  const orbitRef = useRef({ yaw: 0.55, pitch: 0.35, zoom: 1 })
  const dragRef = useRef<{ x: number; y: number; yaw: number; pitch: number } | null>(null)
  const [size, setSize] = useState({ width: 720, height: 420 })
  const [orbitTick, setOrbitTick] = useState(0)
  const [viewMode, setViewMode] = useState<MeshViewMode>('volume')
  const [slicePerspective, setSlicePerspective] = useState<SlicePerspective>('top')
  const [sliceIndex, setSliceIndex] = useState(0)
  const [drawCommands, setDrawCommands] = useState<MeshDrawCommand[]>([])
  const [hud, setHud] = useState('Building mesh…')

  const labels = layerLabels ?? EMPTY_LABELS
  const maxSliceIndex = Math.max(0, layerWidths.length - 1)
  const clampedSliceIndex = Math.min(Math.max(0, sliceIndex), maxSliceIndex)

  useEffect(() => {
    setSliceIndex(0)
  }, [layerWidths.join(',')])

  useEffect(() => {
    const worker = createWorker()
    workerRef.current = worker
    worker.onmessage = (event: MessageEvent<MeshProjectResponse>) => {
      const response = event.data
      if (response.requestId !== requestIdRef.current) {
        return
      }
      setDrawCommands(response.commands)
      setHud(response.hud)
    }
    return () => {
      worker.terminate()
      workerRef.current = null
    }
  }, [])

  useEffect(() => {
    const shell = shellRef.current
    if (!shell) {
      return
    }
    const observe = new ResizeObserver((entries) => {
      const entry = entries[0]
      if (!entry) {
        return
      }
      const width = Math.max(320, Math.floor(entry.contentRect.width))
      const nodeCount = layerWidths.reduce((sum, widthValue) => sum + widthValue, 0)
      const depthHint = Math.max(...layerWidths.map((widthValue) => Math.ceil(Math.sqrt(widthValue))), 1)
      const height = Math.min(720, Math.max(360, 280 + depthHint * 18 + Math.min(180, nodeCount * 0.15)))
      setSize({ width, height })
    })
    observe.observe(shell)
    return () => observe.disconnect()
  }, [layerWidths])

  const requestKey = useMemo(
    () => ({
      layerWidths,
      labels,
      frame,
      viewMode,
      slicePerspective,
      clampedSliceIndex,
      size,
      orbitTick,
    }),
    [clampedSliceIndex, frame, labels, layerWidths, orbitTick, size, slicePerspective, viewMode],
  )

  useEffect(() => {
    const worker = workerRef.current
    if (!worker || layerWidths.length === 0) {
      setDrawCommands([])
      setHud('No mesh topology')
      return
    }

    const requestId = requestIdRef.current + 1
    requestIdRef.current = requestId
    const orbit = orbitRef.current
    const payload: MeshProjectRequest = {
      requestId,
      layerWidths,
      layerLabels: labels,
      pathTone: frame.pathTone,
      activeNodeIndexes: frame.activeNodeIndexes,
      activeEdgeKeys: frame.activeEdgeKeys,
      viewMode,
      slicePerspective,
      sliceIndex: clampedSliceIndex,
      yaw: orbit.yaw,
      pitch: orbit.pitch,
      zoom: orbit.zoom,
      width: size.width,
      height: size.height,
      nodeGap: MESH_NODE_GAP,
      layerGap: MESH_LAYER_GAP,
      colors: readThemeColors(),
    }
    worker.postMessage(payload)
  }, [requestKey, clampedSliceIndex, frame, labels, layerWidths, size.height, size.width, slicePerspective, viewMode])

  useEffect(() => {
    const canvas = canvasRef.current
    if (!canvas) {
      return
    }
    const context = canvas.getContext('2d')
    if (!context) {
      return
    }

    const dpr = Math.min(window.devicePixelRatio || 1, 2)
    canvas.width = Math.floor(size.width * dpr)
    canvas.height = Math.floor(size.height * dpr)
    canvas.style.width = `${size.width}px`
    canvas.style.height = `${size.height}px`
    context.setTransform(dpr, 0, 0, dpr, 0, 0)
    context.clearRect(0, 0, size.width, size.height)

    for (const command of drawCommands) {
      if (command.kind === 'grid') {
        if (command.x1 === 0 && command.y1 === 0 && command.x2 === size.width && command.y2 === size.height) {
          context.globalAlpha = command.alpha
          context.fillStyle = command.color
          context.fillRect(0, 0, size.width, size.height)
          context.globalAlpha = 1
          continue
        }
        context.beginPath()
        context.moveTo(command.x1, command.y1)
        context.lineTo(command.x2, command.y2)
        context.strokeStyle = command.color
        context.globalAlpha = command.alpha
        context.lineWidth = 1
        context.stroke()
        context.globalAlpha = 1
        continue
      }

      if (command.kind === 'edge') {
        context.beginPath()
        context.strokeStyle = command.color
        context.globalAlpha = command.alpha
        context.lineWidth = command.width
        context.setLineDash(command.dashed ? [5, 4] : [])
        context.moveTo(command.x1, command.y1)
        context.quadraticCurveTo(command.cpx, command.cpy, command.x2, command.y2)
        context.stroke()
        context.setLineDash([])
        context.globalAlpha = 1
        continue
      }

      if (command.kind === 'node') {
        context.beginPath()
        context.fillStyle = command.color
        context.globalAlpha = command.alpha
        context.arc(command.x, command.y, command.radius, 0, Math.PI * 2)
        context.fill()
        context.globalAlpha = 1
        context.strokeStyle = command.stroke
        context.lineWidth = 1
        context.stroke()
        continue
      }

      context.fillStyle = command.color
      context.font = '12px var(--font-sans), sans-serif'
      context.textAlign = 'center'
      context.fillText(command.text, command.x, command.y)
    }
  }, [drawCommands, size.height, size.width])

  useEffect(() => {
    const canvas = canvasRef.current
    if (!canvas) {
      return
    }

    const onPointerDown = (event: PointerEvent) => {
      if (viewMode === 'slice' && slicePerspective === 'top') {
        return
      }
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
      if (!drag) {
        return
      }
      orbitRef.current.yaw = drag.yaw + (event.clientX - drag.x) * 0.008
      orbitRef.current.pitch = Math.max(-1.2, Math.min(1.2, drag.pitch + (event.clientY - drag.y) * 0.006))
      requestAnimationFrame(() => {
        setOrbitTick((value) => value + 1)
      })
    }

    const onPointerUp = (event: PointerEvent) => {
      dragRef.current = null
      if (canvas.hasPointerCapture(event.pointerId)) {
        canvas.releasePointerCapture(event.pointerId)
      }
    }

    const onWheel = (event: WheelEvent) => {
      event.preventDefault()
      orbitRef.current.zoom = Math.max(0.45, Math.min(2.8, orbitRef.current.zoom * (event.deltaY > 0 ? 0.92 : 1.08)))
      setOrbitTick((value) => value + 1)
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
  }, [slicePerspective, viewMode])

  const goPreviousSlice = () => {
    setSliceIndex((previous) => Math.max(0, previous - 1))
  }

  const goNextSlice = () => {
    setSliceIndex((previous) => Math.min(maxSliceIndex, previous + 1))
  }

  return (
    <div ref={shellRef} className={`neural-mesh3d ${className ?? ''}`.trim()}>
      <div className="neural-mesh3d-toolbar">
        <strong>{title ?? '3D neural mesh'}</strong>
        <span>{hud}</span>
        <div className="neural-mesh3d-view-controls" role="group" aria-label="Mesh view mode">
          <button
            type="button"
            className={viewMode === 'volume' ? 'btn-secondary neural-mesh3d-control--active' : 'btn-secondary'}
            onClick={() => setViewMode('volume')}
          >
            Volume
          </button>
          <button
            type="button"
            className={viewMode === 'slice' ? 'btn-secondary neural-mesh3d-control--active' : 'btn-secondary'}
            onClick={() => setViewMode('slice')}
          >
            2D slice
          </button>
        </div>
        {viewMode === 'slice' ? (
          <div className="neural-mesh3d-slice-controls" role="group" aria-label="Slice navigation">
            <button
              type="button"
              className={
                slicePerspective === 'top' ? 'btn-secondary neural-mesh3d-control--active' : 'btn-secondary'
              }
              onClick={() => setSlicePerspective('top')}
            >
              Top-down
            </button>
            <button
              type="button"
              className={
                slicePerspective === 'side' ? 'btn-secondary neural-mesh3d-control--active' : 'btn-secondary'
              }
              onClick={() => setSlicePerspective('side')}
            >
              Side
            </button>
            <button type="button" className="btn-secondary" onClick={goPreviousSlice} disabled={clampedSliceIndex <= 0}>
              Prev slice
            </button>
            <span className="neural-mesh3d-slice-index">
              Slice {clampedSliceIndex + 1}/{Math.max(1, layerWidths.length)}
            </span>
            <button
              type="button"
              className="btn-secondary"
              onClick={goNextSlice}
              disabled={clampedSliceIndex >= maxSliceIndex}
            >
              Next slice
            </button>
          </div>
        ) : null}
      </div>
      <canvas
        ref={canvasRef}
        className="neural-mesh3d-canvas"
        role="img"
        aria-label={
          viewMode === 'slice'
            ? 'Two-dimensional layer slice of the neural network mesh'
            : 'Explorable three-dimensional neural network mesh'
        }
      />
      <p className="dashboard-hint neural-mesh3d-hint">
        {viewMode === 'volume'
          ? `Fixed spacing (${MESH_NODE_GAP}px node / ${MESH_LAYER_GAP}px layer). Drag to orbit, scroll to zoom. Projection runs on a worker thread.`
          : slicePerspective === 'top'
            ? 'One layer at a time from above. Use Prev/Next to move through layers.'
            : 'Side perspective of one layer. Drag to tilt, Prev/Next moves layers.'}
      </p>
    </div>
  )
}

export default NeuralNetMesh3D
