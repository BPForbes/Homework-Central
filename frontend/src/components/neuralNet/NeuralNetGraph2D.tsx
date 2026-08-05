import { useMemo, useState } from 'react'
import type { ReplayEdge, ReplayNode } from '../../types/neuralNetReplay'
import type { MeshPathTone } from './NeuralNetMesh3D'

export type Graph2DPathTone = MeshPathTone | null

type Point = { x: number; y: number }

type Props = {
  layerWidths: number[]
  layerLabels?: string[]
  /** Optional topology override (replay). When omitted, a dense lattice is built from layerWidths. */
  nodes?: ReplayNode[]
  edges?: ReplayEdge[]
  detail?: number
  pathTone?: Graph2DPathTone
  activeNodeIndexes?: readonly number[]
  activeEdgeParameterIndexes?: readonly number[]
  className?: string
  ariaLabel?: string
  selectable?: boolean
  onSelectNode?: (node: ReplayNode | null) => void
  selectedNodeIndex?: number | null
}

/** Even sample for preview detail only — max quality renders the full layer. */
function takeEvenly<T>(items: T[], cap: number): T[] {
  if (cap <= 0 || items.length === 0) return []
  if (items.length <= cap) return items
  if (cap === 1) return [items[0]]
  return Array.from({ length: cap }, (_, index) => {
    const sourceIndex = Math.round((index * (items.length - 1)) / (cap - 1))
    return items[sourceIndex]
  })
}

/** Cubic curve fit between layers so dense fans separate instead of stacking as a solid bar. */
function curvedEdgePath(from: Point, to: Point, bend: number): string {
  const dx = Math.abs(to.x - from.x)
  const controlOffset = Math.max(28, dx * 0.42)
  const c1x = from.x + controlOffset
  const c2x = to.x - controlOffset
  const c1y = from.y + bend
  const c2y = to.y - bend
  return `M ${from.x} ${from.y} C ${c1x} ${c1y}, ${c2x} ${c2y}, ${to.x} ${to.y}`
}

function edgeBend(fromY: number, toY: number, edgeIndex: number): number {
  const vertical = toY - fromY
  const fan = ((edgeIndex % 7) - 3) * 2.4
  return vertical * 0.12 + fan
}

function edgeClassForTone(tone: Graph2DPathTone, onPath: boolean): string {
  if (!onPath || !tone || tone === 'idle') return 'neural-edge neural-edge--recorded neural-edge--idle'
  if (tone === 'forward') return 'neural-edge neural-edge--recorded neural-edge--path-forward'
  if (tone === 'reeval') return 'neural-edge neural-edge--recorded neural-edge--path-reeval'
  if (tone === 'backprop') return 'neural-edge neural-edge--recorded neural-edge--path-backprop'
  if (tone === 'accepted') return 'neural-edge neural-edge--recorded neural-edge--accepted'
  return 'neural-edge neural-edge--recorded neural-edge--revision'
}

function nodeClassForTone(tone: Graph2DPathTone, onPath: boolean, selected: boolean): string {
  const classes = ['neural-node']
  if (selected) classes.push('neural-node--selected')
  if (!onPath || !tone || tone === 'idle') {
    classes.push('neural-node--dim')
    return classes.join(' ')
  }
  switch (tone) {
    case 'forward':
      classes.push('neural-node--path-forward')
      break
    case 'reeval':
      classes.push('neural-node--path-reeval')
      break
    case 'backprop':
      classes.push('neural-node--path-backprop')
      break
    case 'accepted':
      classes.push('neural-node--accepted')
      break
    default:
      classes.push('neural-node--revision')
      break
  }
  return classes.join(' ')
}

function layerLabelText(layerId: string): string {
  return layerId.replace(/-/g, ' ')
}

/** Dense topology matching checkpoint flatten order (weights then bias per target). */
function buildDenseGraphTopology(
  layerWidths: number[],
  layerLabels?: string[],
): { nodes: ReplayNode[]; edges: ReplayEdge[] } {
  const labels =
    layerLabels && layerLabels.length === layerWidths.length
      ? layerLabels
      : layerWidths.map((_, index) =>
          index === 0 ? 'input' : index === layerWidths.length - 1 ? 'output' : `hidden-${index}`,
        )

  const nodes: ReplayNode[] = []
  let nodeIndex = 0
  for (let layer = 0; layer < layerWidths.length; layer++) {
    const layerId = labels[layer] ?? `layer-${layer}`
    for (let local = 0; local < layerWidths[layer]; local++) {
      nodes.push({
        index: nodeIndex,
        nodeId: `${layerId}-${local}`,
        layerId,
        label: `${layerId}[${local}]`,
      })
      nodeIndex += 1
    }
  }

  const edges: ReplayEdge[] = []
  let sourceOffset = 0
  let parameterIndex = 0
  let edgeIndex = 0
  for (let layer = 0; layer < layerWidths.length - 1; layer++) {
    const sources = layerWidths[layer]
    const targets = layerWidths[layer + 1]
    const targetOffset = sourceOffset + sources
    for (let target = 0; target < targets; target++) {
      for (let source = 0; source < sources; source++) {
        edges.push({
          index: edgeIndex,
          sourceNodeIndex: sourceOffset + source,
          targetNodeIndex: targetOffset + target,
          parameterIndex,
        })
        edgeIndex += 1
        parameterIndex += 1
      }
      parameterIndex += 1 // bias after each target's weights
    }
    sourceOffset = targetOffset
  }

  return { nodes, edges }
}

/**
 * Planar SVG topology shared by live training and visualizer replay.
 * Detail LOD mirrors the 3D mesh: 0 = layer clusters, 1 = ≤8 nodes/layer, 2 = full.
 */
export function NeuralNetGraph2D({
  layerWidths,
  layerLabels,
  nodes: nodesProp,
  edges: edgesProp,
  detail = 2,
  pathTone = null,
  activeNodeIndexes,
  activeEdgeParameterIndexes,
  className,
  ariaLabel = 'Neural network topology',
  selectable = false,
  onSelectNode,
  selectedNodeIndex = null,
}: Props) {
  const [localSelected, setLocalSelected] = useState<number | null>(null)
  const selectedIndex = selectedNodeIndex ?? localSelected

  const topology = useMemo(() => {
    if (nodesProp && edgesProp) return { nodes: nodesProp, edges: edgesProp }
    return buildDenseGraphTopology(layerWidths, layerLabels)
  }, [nodesProp, edgesProp, layerWidths, layerLabels])

  const layerIds = useMemo(() => {
    const ids: string[] = []
    for (const node of topology.nodes) {
      if (!ids.includes(node.layerId)) ids.push(node.layerId)
    }
    return ids
  }, [topology.nodes])

  const detailLevel = Math.max(0, Math.min(2, Math.floor(detail)))
  const maxQuality = detailLevel >= 2
  const shownCap = detailLevel === 0 ? 0 : detailLevel === 1 ? 8 : Number.POSITIVE_INFINITY

  const nodesByLayer = useMemo(() => {
    const map = new Map<string, ReplayNode[]>()
    for (const layerId of layerIds) {
      const layerNodes = topology.nodes.filter((node) => node.layerId === layerId)
      map.set(layerId, maxQuality ? layerNodes : takeEvenly(layerNodes, shownCap))
    }
    return map
  }, [topology.nodes, layerIds, maxQuality, shownCap])

  const nodes = useMemo(
    () => layerIds.flatMap((layerId) => nodesByLayer.get(layerId) ?? []),
    [layerIds, nodesByLayer],
  )

  const layerEdges = useMemo(() => {
    if (detailLevel === 0) return [] as ReplayEdge[]
    const visibleIds = new Set(nodes.map((node) => node.index))
    return topology.edges.filter(
      (edge) => visibleIds.has(edge.sourceNodeIndex) && visibleIds.has(edge.targetNodeIndex),
    )
  }, [detailLevel, topology.edges, nodes])

  const activeNodes = useMemo(() => new Set(activeNodeIndexes ?? []), [activeNodeIndexes])
  const activeEdges = useMemo(
    () => new Set(activeEdgeParameterIndexes ?? []),
    [activeEdgeParameterIndexes],
  )

  const drawnEdges = useMemo(() => {
    if (layerEdges.length === 0) return [] as ReplayEdge[]
    if (maxQuality) return layerEdges
    const pathEdges = layerEdges.filter((edge) => activeEdges.has(edge.parameterIndex))
    const idleSample = layerEdges.filter(
      (edge, index) => !activeEdges.has(edge.parameterIndex) && index % 5 === 0,
    )
    return [...idleSample, ...pathEdges]
  }, [layerEdges, maxQuality, activeEdges])

  const maxLayerCount = Math.max(1, ...layerIds.map((layerId) => Math.max(1, (nodesByLayer.get(layerId) ?? []).length)))
  const layerGap = maxQuality
    ? Math.max(190, Math.min(260, 1180 / Math.max(1, layerIds.length - 1)))
    : Math.max(150, Math.min(210, 960 / Math.max(1, layerIds.length - 1)))
  const nodeGap = maxQuality ? 36 : Math.max(34, Math.min(48, 300 / Math.max(1, maxLayerCount - 1)))
  const viewWidth = Math.max(720, 100 + (layerIds.length - 1) * layerGap + 100)
  const viewHeight = Math.max(280, 64 + (maxLayerCount - 1) * nodeGap + 80)

  const layerX = (layerId: string): number => 90 + layerIds.indexOf(layerId) * layerGap
  const nodeAt = (node: ReplayNode): Point => {
    const layerNodes = nodesByLayer.get(node.layerId) ?? []
    const index = layerNodes.findIndex((item) => item.index === node.index)
    if (index < 0) return { x: layerX(node.layerId), y: viewHeight / 2 }
    return { x: layerX(node.layerId), y: 56 + index * nodeGap }
  }

  const nodeByIndex = useMemo(
    () => new Map(topology.nodes.map((node) => [node.index, node])),
    [topology.nodes],
  )

  const hasThoughtPath = activeEdges.size > 0 || activeNodes.size > 0

  const selectNode = (node: ReplayNode | null) => {
    if (!selectable) return
    setLocalSelected(node?.index ?? null)
    onSelectNode?.(node)
  }

  return (
    <div className="neural-graph-scroll">
      <svg
        className={`neural-graph neural-graph--replay ${className ?? ''}`.trim()}
        viewBox={`0 0 ${viewWidth} ${viewHeight}`}
        width={viewWidth}
        height={viewHeight}
        role="img"
        aria-label={ariaLabel}
      >
        {layerIds.map((layerId) => (
          <text
            key={`label-${layerId}`}
            x={layerX(layerId)}
            y="24"
            textAnchor="middle"
            className="neural-layer-label"
          >
            {layerLabelText(layerId)}
          </text>
        ))}
        {drawnEdges.map((edge) => {
          const source = nodeByIndex.get(edge.sourceNodeIndex)
          const target = nodeByIndex.get(edge.targetNodeIndex)
          if (!source || !target) return null
          const from = nodeAt(source)
          const to = nodeAt(target)
          const onPath = !hasThoughtPath ? Boolean(pathTone && pathTone !== 'idle') : activeEdges.has(edge.parameterIndex)
          const bend = edgeBend(from.y, to.y, edge.index)
          return (
            <path
              key={edge.index}
              d={curvedEdgePath(from, to, bend)}
              fill="none"
              className={edgeClassForTone(pathTone, Boolean(onPath && pathTone))}
            />
          )
        })}
        {detailLevel === 0
          ? layerIds.map((layerId) => {
              const layerNodes = topology.nodes.filter((node) => node.layerId === layerId)
              return (
                <g key={`cluster-${layerId}`}>
                  <circle
                    cx={layerX(layerId)}
                    cy={viewHeight / 2}
                    r="42"
                    className={nodeClassForTone(pathTone, Boolean(pathTone && pathTone !== 'idle'), false)}
                  />
                  <title>{`${layerLabelText(layerId)} · ${layerNodes.length} nodes`}</title>
                </g>
              )
            })
          : nodes.map((node) => {
              const point = nodeAt(node)
              const onPath = !hasThoughtPath
                ? Boolean(pathTone && pathTone !== 'idle')
                : activeNodes.has(node.index)
              const isOutput = node.layerId === 'output' || layerIds.indexOf(node.layerId) === layerIds.length - 1
              const isInput = node.layerId === 'input' || layerIds.indexOf(node.layerId) === 0
              const selected = selectedIndex === node.index
              if (isOutput) {
                return (
                  <g
                    key={node.nodeId}
                    className={selectable ? 'neural-node-group' : undefined}
                    onClick={selectable ? () => selectNode(node) : undefined}
                    role={selectable ? 'button' : undefined}
                    tabIndex={selectable ? 0 : undefined}
                    aria-label={selectable ? node.label : undefined}
                    onKeyDown={
                      selectable
                        ? (event) => {
                            if (event.key === 'Enter' || event.key === ' ') {
                              event.preventDefault()
                              selectNode(node)
                            }
                          }
                        : undefined
                    }
                  >
                    <rect
                      x={point.x - 16}
                      y={point.y - 11}
                      width="32"
                      height="22"
                      rx="8"
                      className={nodeClassForTone(pathTone, onPath, selected)}
                    />
                    <title>{node.label}</title>
                  </g>
                )
              }
              return (
                <g
                  key={node.nodeId}
                  className={selectable && !isInput ? 'neural-node-group' : undefined}
                  onClick={selectable && !isInput ? () => selectNode(node) : undefined}
                  role={selectable && !isInput ? 'button' : undefined}
                  tabIndex={selectable && !isInput ? 0 : undefined}
                  aria-label={selectable && !isInput ? node.label : undefined}
                  onKeyDown={
                    selectable && !isInput
                      ? (event) => {
                          if (event.key === 'Enter' || event.key === ' ') {
                            event.preventDefault()
                            selectNode(node)
                          }
                        }
                      : undefined
                  }
                >
                  <circle
                    cx={point.x}
                    cy={point.y}
                    r={isInput ? 7 : 10}
                    className={nodeClassForTone(pathTone, onPath, selected)}
                  >
                    <title>{node.label}</title>
                  </circle>
                </g>
              )
            })}
      </svg>
    </div>
  )
}
