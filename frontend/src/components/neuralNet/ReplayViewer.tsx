import { useEffect, useMemo, useState } from 'react'
import { FontAwesomeIcon } from '@fortawesome/react-fontawesome'
import { byPrefixAndName } from '../../icons/byPrefixAndName'
import type { NeuralNetReplay, ReplayEdge, ReplayNode } from '../../types/neuralNetReplay'

type SparseValue = { index: number; value: number }
type ForwardPayload = {
  edgeContributions?: SparseValue[]
  nodeActivations?: SparseValue[]
}
type BackpropPayload = {
  weightGradients?: SparseValue[]
  biasGradients?: SparseValue[]
}
type LossPayload = {
  function?: string
  evidenceLoss?: number
  relevanceLoss?: number
  categoryLoss?: number
  totalLoss?: number
}
type PathTone = 'forward' | 'reeval' | 'backprop' | 'accepted' | 'revision' | null

const PAYLOAD_COLLECTION: Record<string, string> = {
  Llm1Input: 'inputs',
  InitialForward: 'forwardPasses',
  Llm2Evaluation: 'evaluations',
  VoteResolution: 'voteSampling',
  EpochForward: 'forwardPasses',
  LossCalculation: 'losses',
  BackwardPropagation: 'backpropagations',
  ParameterUpdate: 'parameterUpdates',
  PostUpdateForward: 'forwardPasses',
  FinalVerdict: 'finalVerdicts',
}

function isForwardPhase(phase: string | undefined): boolean {
  return phase === 'InitialForward' || phase === 'EpochForward' || phase === 'PostUpdateForward'
}

function isReevalPhase(phase: string | undefined): boolean {
  return phase === 'Llm2Evaluation' || phase === 'VoteResolution'
}

function pathToneForPhase(phase: string | undefined, accepted?: boolean): PathTone {
  if (!phase) return null
  if (phase === 'BackwardPropagation') return 'backprop'
  if (isReevalPhase(phase)) return 'reeval'
  if (phase === 'FinalVerdict') return accepted ? 'accepted' : 'revision'
  if (isForwardPhase(phase)) return 'forward'
  return null
}

function edgeClassForTone(tone: PathTone, onPath: boolean): string {
  if (!onPath || !tone) return 'neural-edge neural-edge--recorded neural-edge--idle'
  if (tone === 'forward') return 'neural-edge neural-edge--recorded neural-edge--path-forward'
  if (tone === 'reeval') return 'neural-edge neural-edge--recorded neural-edge--path-reeval'
  if (tone === 'backprop') return 'neural-edge neural-edge--recorded neural-edge--path-backprop'
  if (tone === 'accepted') return 'neural-edge neural-edge--recorded neural-edge--accepted'
  return 'neural-edge neural-edge--recorded neural-edge--revision'
}

function nodeClassForTone(tone: PathTone, onPath: boolean, selected: boolean): string {
  const classes = ['neural-node']
  if (selected) classes.push('neural-node--selected')
  if (!onPath || !tone) {
    classes.push('neural-node--dim')
    return classes.join(' ')
  }
  if (tone === 'forward') classes.push('neural-node--path-forward')
  else if (tone === 'reeval') classes.push('neural-node--path-reeval')
  else if (tone === 'backprop') classes.push('neural-node--path-backprop')
  else if (tone === 'accepted') classes.push('neural-node--accepted')
  else classes.push('neural-node--revision')
  return classes.join(' ')
}

export function ReplayViewer({ replay }: { replay: NeuralNetReplay }) {
  const [frameIndex, setFrameIndex] = useState(0)
  const [detail, setDetail] = useState(1)
  const [playing, setPlaying] = useState(false)
  const [ticket, setTicket] = useState<number | 'all'>('all')
  const [selected, setSelected] = useState<ReplayNode | null>(null)
  const [reducedMotion, setReducedMotion] = useState(() =>
    window.matchMedia('(prefers-reduced-motion: reduce)').matches,
  )

  const frames = useMemo(
    () => (ticket === 'all' ? replay.frames : replay.frames.filter((frame) => frame.ticketIndex === ticket)),
    [replay, ticket],
  )
  const frame = frames[Math.min(frameIndex, Math.max(0, frames.length - 1))]

  useEffect(() => {
    setFrameIndex(0)
  }, [ticket, replay.sessionId])

  useEffect(() => {
    const query = window.matchMedia('(prefers-reduced-motion: reduce)')
    const change = () => setReducedMotion(query.matches)
    query.addEventListener('change', change)
    return () => query.removeEventListener('change', change)
  }, [])

  useEffect(() => {
    if (!playing || reducedMotion) return
    const timer = window.setInterval(
      () => setFrameIndex((value) => (value + 1 < frames.length ? value + 1 : (setPlaying(false), value))),
      600,
    )
    return () => window.clearInterval(timer)
  }, [playing, frames.length, reducedMotion])

  const inputs = replay.topology.nodes.filter((node) => node.layerId === 'input')
  const output = replay.topology.nodes.filter((node) => node.layerId === 'output')
  const layerIds = Array.from(new Set(replay.topology.nodes.map((node) => node.layerId)))
  const intermediateLayerIds = layerIds.filter((layerId) => layerId !== 'input' && layerId !== 'output')
  const visibleInputs = detail === 2 ? inputs : detail === 1 ? inputs.slice(0, 24) : []
  const intermediate = intermediateLayerIds.flatMap((layerId) =>
    replay.topology.nodes.filter((node) => node.layerId === layerId),
  )
  const nodes: ReplayNode[] = [...visibleInputs, ...intermediate, ...output]
  const visibleIds = new Set(nodes.map((node) => node.index))
  const edges: ReplayEdge[] =
    detail === 0
      ? []
      : replay.topology.edges.filter(
          (edge) => visibleIds.has(edge.sourceNodeIndex) && visibleIds.has(edge.targetNodeIndex),
        )

  const layerLabel = (layerId: string): string => layerId.replace(/-/g, ' ')
  const layerIndex = (layerId: string): number => layerIds.indexOf(layerId)
  const nodesInLayer = (layerId: string): ReplayNode[] =>
    layerId === 'input' ? visibleInputs : replay.topology.nodes.filter((node) => node.layerId === layerId)

  const maxLayerCount = Math.max(1, ...layerIds.map((layerId) => Math.max(1, nodesInLayer(layerId).length)))
  const layerGap = Math.max(160, Math.min(280, 1400 / Math.max(1, layerIds.length - 1)))
  const nodeGap = Math.max(22, Math.min(42, 920 / Math.max(1, maxLayerCount - 1)))
  const viewWidth = Math.max(960, 140 + (layerIds.length - 1) * layerGap + 140)
  const viewHeight = Math.max(420, 70 + (maxLayerCount - 1) * nodeGap + 90)

  const layerX = (layerId: string): number => 120 + layerIndex(layerId) * layerGap
  const nodeAt = (node: ReplayNode): { x: number; y: number } => {
    const layerNodes = nodesInLayer(node.layerId)
    const index = layerNodes.findIndex((item) => item.index === node.index)
    const span = Math.max(1, layerNodes.length - 1)
    return { x: layerX(node.layerId), y: 58 + index * ((viewHeight - 110) / span) }
  }

  const nodeByIndex = new Map(replay.topology.nodes.map((node) => [node.index, node]))

  const forwardPayload =
    frame && isForwardPhase(frame.phase)
      ? (replay.payloads?.forwardPasses?.[frame.payloadIndex] as ForwardPayload | undefined)
      : undefined
  const backpropPayload =
    frame?.phase === 'BackwardPropagation'
      ? (replay.payloads?.backpropagations?.[frame.payloadIndex] as BackpropPayload | undefined)
      : undefined
  const lossPayload =
    frame?.phase === 'LossCalculation'
      ? (replay.payloads?.losses?.[frame.payloadIndex] as LossPayload | undefined)
      : undefined
  const finalVerdict =
    frame?.phase === 'FinalVerdict'
      ? (replay.payloads?.finalVerdicts?.[frame.payloadIndex] as { accepted?: boolean } | undefined)
      : undefined

  const lastForwardPayload = useMemo(() => {
    if (!frame) return undefined
    for (let i = frameIndex; i >= 0; i -= 1) {
      const prior = frames[i]
      if (!prior || !isForwardPhase(prior.phase)) continue
      return replay.payloads?.forwardPasses?.[prior.payloadIndex] as ForwardPayload | undefined
    }
    return undefined
  }, [frame, frameIndex, frames, replay.payloads])

  const pathTone = pathToneForPhase(frame?.phase, finalVerdict?.accepted)

  const activeEdgeParams = useMemo(() => {
    if (pathTone === 'backprop') {
      return new Set(
        (backpropPayload?.weightGradients ?? [])
          .filter((item) => item.value !== 0)
          .map((item) => item.index),
      )
    }
    const source =
      pathTone === 'forward'
        ? forwardPayload
        : pathTone === 'reeval' || pathTone === 'accepted' || pathTone === 'revision'
          ? lastForwardPayload
          : undefined
    return new Set(
      (source?.edgeContributions ?? [])
        .filter((item) => Math.abs(item.value) > 1e-6)
        .map((item) => item.index),
    )
  }, [pathTone, backpropPayload, forwardPayload, lastForwardPayload])

  const activeNodeIds = useMemo(() => {
    const ids = new Set<number>()
    if (pathTone === 'backprop') {
      for (const edge of edges) {
        if (!activeEdgeParams.has(edge.parameterIndex)) continue
        ids.add(edge.sourceNodeIndex)
        ids.add(edge.targetNodeIndex)
      }
      return ids
    }
    const source =
      pathTone === 'forward'
        ? forwardPayload
        : pathTone === 'reeval' || pathTone === 'accepted' || pathTone === 'revision'
          ? lastForwardPayload
          : undefined
    for (const item of source?.nodeActivations ?? []) {
      if (Math.abs(item.value) > 1e-6) ids.add(item.index)
    }
    for (const edge of edges) {
      if (!activeEdgeParams.has(edge.parameterIndex)) continue
      ids.add(edge.sourceNodeIndex)
      ids.add(edge.targetNodeIndex)
    }
    return ids
  }, [pathTone, edges, activeEdgeParams, forwardPayload, lastForwardPayload])

  const payload = frame ? replay.payloads?.[PAYLOAD_COLLECTION[frame.phase]]?.[frame.payloadIndex] : null
  const hasThoughtPath = activeEdgeParams.size > 0 || activeNodeIds.size > 0

  return (
    <section className="sm-panel neural-graph-panel">
      <div className="sm-panel-header">
        <h3>V2 replay</h3>
      </div>
      <div className="sm-form-actions">
        <label className="sm-label">
          Ticket{' '}
          <select
            className="sm-input"
            value={ticket}
            onChange={(event) => setTicket(event.target.value === 'all' ? 'all' : Number(event.target.value))}
          >
            <option value="all">All</option>
            {replay.tickets.map((item) => (
              <option key={item.ticketIndex} value={item.ticketIndex}>
                Ticket {item.ticketIndex}
              </option>
            ))}
          </select>
        </label>
        <button type="button" className="btn-secondary" onClick={() => setDetail((value) => Math.max(0, value - 1))}>
          − Detail
        </button>
        <button type="button" className="btn-secondary" onClick={() => setDetail((value) => Math.min(2, value + 1))}>
          + Detail
        </button>
        <button
          type="button"
          className="btn-secondary"
          disabled={frameIndex === 0}
          onClick={() => setFrameIndex((value) => Math.max(0, value - 1))}
        >
          <FontAwesomeIcon icon={byPrefixAndName.fas['backward-step']} /> Step back
        </button>
        <button
          type="button"
          className="btn-secondary"
          disabled={frameIndex >= frames.length - 1}
          onClick={() => setFrameIndex((value) => Math.min(frames.length - 1, value + 1))}
        >
          <FontAwesomeIcon icon={byPrefixAndName.fas['backward-step']} rotation={180} /> Step forward
        </button>
        <button
          type="button"
          className="btn-primary"
          disabled={frames.length === 0 || reducedMotion}
          onClick={() => setPlaying((value) => !value)}
        >
          <FontAwesomeIcon icon={playing ? byPrefixAndName.fas.pause : byPrefixAndName.fas.play} />{' '}
          {playing ? 'Pause' : 'Play'}
        </button>
      </div>
      <p className="dashboard-hint">
        Frame {frames.length ? frameIndex + 1 : 0} of {frames.length} · {frame?.phase ?? 'No recorded frames'} ·{' '}
        {detail === 0
          ? `Clustered: ${inputs.length} input nodes, ${replay.topology.edges.length} edges`
          : detail === 1
            ? 'Layer detail · color-only thought path'
            : 'Full detail · color-only thought path'}
        {reducedMotion ? ' · Playback disabled by reduced-motion preference' : ''}
      </p>
      <p className="neural-path-legend" aria-label="Thought path color legend">
        <span>
          <i className="neural-path-swatch neural-path-swatch--forward" aria-hidden /> Forward progression
        </span>
        <span>
          <i className="neural-path-swatch neural-path-swatch--reeval" aria-hidden /> Blocking / reevaluation
        </span>
        <span>
          <i className="neural-path-swatch neural-path-swatch--backprop" aria-hidden /> Backpropagation
        </span>
      </p>
      {lossPayload && (
        <p className="dashboard-hint">
          Loss · {lossPayload.function ?? 'bce+categorical-cross-entropy'} · evidence{' '}
          {lossPayload.evidenceLoss?.toFixed?.(4) ?? '—'} · relevance {lossPayload.relevanceLoss?.toFixed?.(4) ?? '—'}{' '}
          · categorical CE {lossPayload.categoryLoss?.toFixed?.(4) ?? '—'} · total{' '}
          {lossPayload.totalLoss?.toFixed?.(4) ?? '—'}
        </p>
      )}
      <div className="neural-graph-scroll">
        <svg
          className="neural-graph neural-graph--replay"
          viewBox={`0 0 ${viewWidth} ${viewHeight}`}
          width={viewWidth}
          height={viewHeight}
          role="img"
          aria-label="Recorded neural network topology with thought-path coloring"
        >
          {layerIds.map((layerId) => (
            <text
              key={`label-${layerId}`}
              x={layerX(layerId)}
              y="28"
              textAnchor="middle"
              className="neural-layer-label"
            >
              {layerLabel(layerId)}
            </text>
          ))}
          {edges.map((edge) => {
            const source = nodeByIndex.get(edge.sourceNodeIndex)
            const target = nodeByIndex.get(edge.targetNodeIndex)
            if (!source || !target) return null
            const from = nodeAt(source)
            const to = nodeAt(target)
            const onPath = !hasThoughtPath
              ? pathTone === 'backprop' || pathTone === 'reeval'
              : activeEdgeParams.has(edge.parameterIndex)
            return (
              <line
                key={edge.index}
                x1={from.x}
                y1={from.y}
                x2={to.x}
                y2={to.y}
                className={edgeClassForTone(pathTone, Boolean(onPath && pathTone))}
              />
            )
          })}
          {detail === 0
            ? layerIds.map((layerId) => {
                const layerNodes = replay.topology.nodes.filter((node) => node.layerId === layerId)
                return (
                  <g key={`cluster-${layerId}`}>
                    <circle
                      cx={layerX(layerId)}
                      cy={viewHeight / 2}
                      r="58"
                      className={nodeClassForTone(pathTone, Boolean(pathTone), false)}
                    />
                    <title>{`${layerLabel(layerId)} · ${layerNodes.length} nodes`}</title>
                  </g>
                )
              })
            : nodes
                .filter((node) => node.layerId === 'input')
                .map((node) => {
                  const point = nodeAt(node)
                  const onPath = !hasThoughtPath ? Boolean(pathTone) : activeNodeIds.has(node.index)
                  return (
                    <circle
                      key={node.nodeId}
                      cx={point.x}
                      cy={point.y}
                      r="6"
                      className={nodeClassForTone(pathTone, onPath, false)}
                    >
                      <title>{node.label}</title>
                    </circle>
                  )
                })}
          {detail > 0 &&
            intermediate.map((node) => {
              const point = nodeAt(node)
              const onPath = !hasThoughtPath ? Boolean(pathTone) : activeNodeIds.has(node.index)
              return (
                <g
                  key={node.nodeId}
                  className="neural-node-group"
                  onClick={() => setSelected(node)}
                  role="button"
                  tabIndex={0}
                  aria-label={node.label}
                  onKeyDown={(event) => {
                    if (event.key === 'Enter' || event.key === ' ') {
                      event.preventDefault()
                      setSelected(node)
                    }
                  }}
                >
                  <circle
                    cx={point.x}
                    cy={point.y}
                    r="11"
                    className={nodeClassForTone(pathTone, onPath, selected?.index === node.index)}
                  />
                  <title>{node.label}</title>
                </g>
              )
            })}
          {detail > 0 &&
            output.map((node) => {
              const point = nodeAt(node)
              const onPath = !hasThoughtPath ? Boolean(pathTone) : activeNodeIds.has(node.index)
              return (
                <g
                  key={node.nodeId}
                  className="neural-node-group"
                  onClick={() => setSelected(node)}
                  role="button"
                  tabIndex={0}
                  aria-label={node.label}
                  onKeyDown={(event) => {
                    if (event.key === 'Enter' || event.key === ' ') {
                      event.preventDefault()
                      setSelected(node)
                    }
                  }}
                >
                  <rect
                    x={point.x - 18}
                    y={point.y - 12}
                    width="36"
                    height="24"
                    rx="8"
                    className={nodeClassForTone(pathTone, onPath, selected?.index === node.index)}
                  />
                  <title>{node.label}</title>
                </g>
              )
            })}
        </svg>
      </div>
      <p className="dashboard-hint">
        Selected: {selected ? `${selected.label} (${selected.layerId})` : 'none'} · Integrity:{' '}
        {replay.integrity?.reportChecksum ? 'recorded checksum' : 'not supplied'} · Completion:{' '}
        {replay.completionStatus}. Node labels stay off during replay — hover or select a node for its name. Canvas
        size grows with layer depth so connections stay readable.
      </p>
      <section className="sm-panel">
        <div className="sm-panel-header">
          <h3>Recorded frame inspector</h3>
        </div>
        <p className="dashboard-hint">
          Phase: {frame?.phase ?? 'none'} · ticket {frame?.ticketIndex ?? '—'} · pass {frame?.passIndex ?? '—'} · epoch{' '}
          {frame?.epoch ?? '—'}. Loss frames use binary CE on evidence/relevance and categorical cross-entropy on the
          prediction classes.
        </p>
        <pre className="dashboard-hint">{payload ? JSON.stringify(payload, null, 2) : 'No payload for this frame.'}</pre>
      </section>
    </section>
  )
}
