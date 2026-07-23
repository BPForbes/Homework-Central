import { useEffect, useMemo, useState } from 'react'
import { FontAwesomeIcon } from '@fortawesome/react-fontawesome'
import { byPrefixAndName } from '../../icons/byPrefixAndName'
import type { NeuralNetReplay, ReplayEdge, ReplayNode } from '../../types/neuralNetReplay'
import { normalizeReplayPhase, payloadCollectionForPhase } from '../../utils/neuralNetReplay'

type SparseValue = { index: number; value: number }
type ForwardPayload = {
  edgeContributions?: SparseValue[]
  nodeActivations?: SparseValue[]
}
type BackpropPayload = {
  weightGradients?: SparseValue[]
  biasGradients?: SparseValue[]
  gradientL2Norm?: number
}
type LossPayload = {
  function?: string
  evidenceLoss?: number
  relevanceLoss?: number
  categoryLoss?: number
  totalLoss?: number
}
type Llm1Payload = {
  requirement?: string | number
  contextSnapshot?: string | number
  message?: string | number
  channel?: string | number
  authorRole?: string | number
}
type Llm2Payload = {
  feedback?: string | number
  accepted?: boolean
  targetScore?: number
  targetRelevance?: number
  evaluatorConfidence?: number
}
type ParameterUpdatePayload = {
  learningRate?: number
  optimizer?: string
  parameters?: { parameterIndex?: number; delta?: number; gradient?: number; valueAfter?: number }[]
}
type PathTone = 'forward' | 'reeval' | 'backprop' | 'accepted' | 'revision' | null

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
  if (phase === 'ParameterUpdate' || phase === 'LossCalculation') return 'backprop'
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
function curvedEdgePath(
  from: { x: number; y: number },
  to: { x: number; y: number },
  bend: number,
): string {
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
  // Small fitted offset so parallel edges fan instead of overlapping one stroke.
  const fan = ((edgeIndex % 7) - 3) * 2.4
  return vertical * 0.12 + fan
}

function resolveString(value: string | number | undefined, strings: string[] | undefined): string | undefined {
  if (value === undefined || value === null) return undefined
  if (typeof value === 'number' && strings && strings[value] !== undefined) return strings[value]
  return String(value)
}

function phaseOpsLabel(phase: string | undefined): string {
  switch (phase) {
    case 'InitialForward':
    case 'EpochForward':
    case 'PostUpdateForward':
      return 'Forward · Leaky ReLU hidden · sigmoid evidence/relevance · softmax categories'
    case 'LossCalculation':
      return 'Loss · BCE (evidence/relevance) + categorical CE (CCEL)'
    case 'BackwardPropagation':
      return 'Backprop · ∂C/∂w via chain rule through ReLU gates'
    case 'ParameterUpdate':
      return 'Parameter update · momentum mini-batch SGD'
    case 'Llm1Input':
      return 'LLM 1 · synthetic ticket / message payload into the cascade'
    case 'Llm2Evaluation':
      return 'LLM 2 · teacher / audit feedback (balanced; not over-steering)'
    case 'VoteResolution':
      return 'Community vote resolution · blocking / reevaluation'
    case 'FinalVerdict':
      return 'Final verdict · accept within tolerance or revise'
    default:
      return 'Recorded training step'
  }
}

export function ReplayViewer({ replay }: { replay: NeuralNetReplay }) {
  const [frameIndex, setFrameIndex] = useState(0)
  const [detail, setDetail] = useState(2)
  const [playing, setPlaying] = useState(false)
  const [ticket, setTicket] = useState<number | 'all'>('all')
  const [selected, setSelected] = useState<ReplayNode | null>(null)
  const [reducedMotion, setReducedMotion] = useState(() =>
    window.matchMedia('(prefers-reduced-motion: reduce)').matches,
  )

  const frames = useMemo(() => {
    const normalized = replay.frames.map((frame) => ({
      ...frame,
      phase: normalizeReplayPhase(frame.phase as string | number),
    }))
    return ticket === 'all' ? normalized : normalized.filter((frame) => frame.ticketIndex === ticket)
  }, [replay, ticket])
  const frame = frames[Math.min(frameIndex, Math.max(0, frames.length - 1))]
  const phase = frame?.phase

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

  const layerIds = useMemo(
    () => Array.from(new Set(replay.topology.nodes.map((node) => node.layerId))),
    [replay.topology.nodes],
  )
  // Preview samples; max quality (detail 2) keeps every node in every layer.
  const maxQuality = detail >= 2
  const shownCap = detail === 0 ? 0 : detail === 1 ? 8 : Number.POSITIVE_INFINITY
  const nodesByLayer = useMemo(() => {
    const map = new Map<string, ReplayNode[]>()
    for (const layerId of layerIds) {
      const layerNodes = replay.topology.nodes.filter((node) => node.layerId === layerId)
      map.set(layerId, maxQuality ? layerNodes : takeEvenly(layerNodes, shownCap))
    }
    return map
  }, [replay.topology.nodes, layerIds, maxQuality, shownCap])

  const nodes: ReplayNode[] = useMemo(
    () => layerIds.flatMap((layerId) => nodesByLayer.get(layerId) ?? []),
    [layerIds, nodesByLayer],
  )
  const layerEdges = useMemo(() => {
    if (detail === 0) return [] as ReplayEdge[]
    const visibleIds = new Set(nodes.map((node) => node.index))
    return replay.topology.edges.filter(
      (edge) => visibleIds.has(edge.sourceNodeIndex) && visibleIds.has(edge.targetNodeIndex),
    )
  }, [detail, replay.topology.edges, nodes])

  const layerLabel = (layerId: string): string => layerId.replace(/-/g, ' ')
  const layerIndex = (layerId: string): number => layerIds.indexOf(layerId)
  const nodesInLayer = (layerId: string): ReplayNode[] => nodesByLayer.get(layerId) ?? []

  const maxLayerCount = Math.max(1, ...layerIds.map((layerId) => Math.max(1, nodesInLayer(layerId).length)))
  // Fixed Stage-1-like gap (~36px) so tall layers grow the canvas instead of packing into a bar.
  const layerGap = maxQuality
    ? Math.max(190, Math.min(260, 1180 / Math.max(1, layerIds.length - 1)))
    : Math.max(150, Math.min(210, 960 / Math.max(1, layerIds.length - 1)))
  const nodeGap = maxQuality ? 36 : Math.max(34, Math.min(48, 300 / Math.max(1, maxLayerCount - 1)))
  const viewWidth = Math.max(720, 100 + (layerIds.length - 1) * layerGap + 100)
  const viewHeight = Math.max(280, 64 + (maxLayerCount - 1) * nodeGap + 80)

  const layerX = (layerId: string): number => 90 + layerIndex(layerId) * layerGap
  const nodeAt = (node: ReplayNode): { x: number; y: number } => {
    const layerNodes = nodesInLayer(node.layerId)
    const index = layerNodes.findIndex((item) => item.index === node.index)
    if (index < 0) return { x: layerX(node.layerId), y: viewHeight / 2 }
    return { x: layerX(node.layerId), y: 56 + index * nodeGap }
  }

  const nodeByIndex = new Map(replay.topology.nodes.map((node) => [node.index, node]))
  const stringTable = (replay as { strings?: { values?: string[] } | string[] }).strings
  const strings = Array.isArray(stringTable)
    ? stringTable
    : Array.isArray(stringTable?.values)
      ? stringTable.values
      : undefined

  const collectionKey = payloadCollectionForPhase(phase)
  const payload =
    frame && collectionKey ? replay.payloads?.[collectionKey]?.[frame.payloadIndex] : null

  const forwardPayload =
    frame && isForwardPhase(phase) ? (payload as ForwardPayload | undefined) : undefined
  const backpropPayload =
    phase === 'BackwardPropagation' ? (payload as BackpropPayload | undefined) : undefined
  const lossPayload = phase === 'LossCalculation' ? (payload as LossPayload | undefined) : undefined
  const llm1Payload = phase === 'Llm1Input' ? (payload as Llm1Payload | undefined) : undefined
  const llm2Payload = phase === 'Llm2Evaluation' ? (payload as Llm2Payload | undefined) : undefined
  const updatePayload =
    phase === 'ParameterUpdate' ? (payload as ParameterUpdatePayload | undefined) : undefined
  const finalVerdict =
    phase === 'FinalVerdict'
      ? (payload as { accepted?: boolean; reason?: string | number } | undefined)
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

  const recentWeightFeed = useMemo(() => {
    const lines: string[] = []
    for (let i = Math.max(0, frameIndex - 24); i <= frameIndex; i += 1) {
      const item = frames[i]
      if (!item) continue

      if (item.phase === 'ParameterUpdate') {
        const update = replay.payloads?.parameterUpdates?.[item.payloadIndex] as
          | ParameterUpdatePayload
          | undefined
        const deltaCount = update?.parameters?.length ?? 0
        lines.push(
          deltaCount > 0
            ? `epoch ${item.epoch ?? '—'} · SGD Δw ×${deltaCount} · lr ${update?.learningRate ?? '—'}`
            : `epoch ${item.epoch ?? '—'} · compact SGD step (ReLU/backprop)`,
        )
        continue
      }

      if (item.phase === 'LossCalculation') {
        const loss = replay.payloads?.losses?.[item.payloadIndex] as LossPayload | undefined
        if (!loss) continue
        lines.push(
          `epoch ${item.epoch ?? '—'} · BCE+CCEL total ${loss.totalLoss?.toFixed?.(4) ?? '—'} · catCE ${loss.categoryLoss?.toFixed?.(4) ?? '—'}`,
        )
        continue
      }

      if (item.phase !== 'BackwardPropagation') continue
      const back = replay.payloads?.backpropagations?.[item.payloadIndex] as BackpropPayload | undefined
      lines.push(
        `epoch ${item.epoch ?? '—'} · backprop ‖∇‖ ${back?.gradientL2Norm?.toFixed?.(4) ?? 'compact'}`,
      )
    }
    return lines.slice(-8)
  }, [frameIndex, frames, replay.payloads])

  const pathTone = pathToneForPhase(phase, finalVerdict?.accepted)

  const activeEdgeParams = useMemo(() => {
    if (pathTone === 'backprop') {
      const gradients = backpropPayload?.weightGradients ?? []
      if (gradients.length > 0) {
        return new Set(gradients.filter((item) => item.value !== 0).map((item) => item.index))
      }
      // Compact traces omit per-weight grads — light the visible edges so the phase still colorizes.
      return new Set(layerEdges.map((edge) => edge.parameterIndex))
    }
    const source =
      pathTone === 'forward'
        ? forwardPayload
        : pathTone === 'reeval' || pathTone === 'accepted' || pathTone === 'revision'
          ? lastForwardPayload
          : undefined
    const contributions = source?.edgeContributions ?? []
    if (contributions.length === 0 && (pathTone === 'forward' || pathTone === 'reeval')) {
      return new Set(layerEdges.map((edge) => edge.parameterIndex))
    }
    return new Set(contributions.filter((item) => Math.abs(item.value) > 1e-6).map((item) => item.index))
  }, [pathTone, backpropPayload, forwardPayload, lastForwardPayload, layerEdges])

  const activeNodeIds = useMemo(() => {
    const ids = new Set<number>()
    if (pathTone === 'backprop') {
      for (const edge of layerEdges) {
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
    for (const edge of layerEdges) {
      if (!activeEdgeParams.has(edge.parameterIndex)) continue
      ids.add(edge.sourceNodeIndex)
      ids.add(edge.targetNodeIndex)
    }
    if (ids.size === 0 && pathTone) {
      for (const node of nodes) ids.add(node.index)
    }
    return ids
  }, [pathTone, layerEdges, activeEdgeParams, forwardPayload, lastForwardPayload, nodes])

  // Max quality draws every edge as a curve; preview keeps a lighter idle mesh plus the thought path.
  const drawnEdges = useMemo(() => {
    if (layerEdges.length === 0) return [] as ReplayEdge[]
    if (maxQuality) return layerEdges
    const pathEdges = layerEdges.filter((edge) => activeEdgeParams.has(edge.parameterIndex))
    const idleSample = layerEdges.filter(
      (edge, index) => !activeEdgeParams.has(edge.parameterIndex) && index % 5 === 0,
    )
    return [...idleSample, ...pathEdges]
  }, [layerEdges, maxQuality, activeEdgeParams])

  const hasThoughtPath = activeEdgeParams.size > 0 || activeNodeIds.size > 0
  const totalNodeCount = replay.topology.nodes.length
  const totalInputCount = replay.topology.nodes.filter((node) => node.layerId === 'input').length

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
        Frame {frames.length ? frameIndex + 1 : 0} of {frames.length} · {phase || 'No recorded frames'} ·{' '}
        {detail === 0
          ? `Clustered: ${totalInputCount} input nodes, ${replay.topology.edges.length} edges`
          : maxQuality
            ? `Max quality · all ${totalNodeCount} nodes · ${layerEdges.length} curve-fit edges · color thought path`
            : `Preview · ≤${shownCap} nodes/layer · sampled curve-fit edges · color thought path`}
        {reducedMotion ? ' · Playback disabled by reduced-motion preference' : ''}
      </p>
      <p className="neural-path-legend" aria-label="Thought path color legend">
        <span>
          <i className="neural-path-swatch neural-path-swatch--forward" aria-hidden /> Forward / ReLU
        </span>
        <span>
          <i className="neural-path-swatch neural-path-swatch--reeval" aria-hidden /> LLM2 / blocking
        </span>
        <span>
          <i className="neural-path-swatch neural-path-swatch--backprop" aria-hidden /> CCEL / backprop
        </span>
      </p>
      <p className="dashboard-hint neural-ops-strip">{phaseOpsLabel(phase)}</p>
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
              y="24"
              textAnchor="middle"
              className="neural-layer-label"
            >
              {layerLabel(layerId)}
            </text>
          ))}
          {drawnEdges.map((edge) => {
            const source = nodeByIndex.get(edge.sourceNodeIndex)
            const target = nodeByIndex.get(edge.targetNodeIndex)
            if (!source || !target) return null
            const from = nodeAt(source)
            const to = nodeAt(target)
            const onPath = !hasThoughtPath
              ? Boolean(pathTone)
              : activeEdgeParams.has(edge.parameterIndex)
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
          {detail === 0
            ? layerIds.map((layerId) => {
                const layerNodes = replay.topology.nodes.filter((node) => node.layerId === layerId)
                return (
                  <g key={`cluster-${layerId}`}>
                    <circle
                      cx={layerX(layerId)}
                      cy={viewHeight / 2}
                      r="42"
                      className={nodeClassForTone(pathTone, Boolean(pathTone), false)}
                    />
                    <title>{`${layerLabel(layerId)} · ${layerNodes.length} nodes`}</title>
                  </g>
                )
              })
            : nodes.map((node) => {
                const point = nodeAt(node)
                const onPath = !hasThoughtPath ? Boolean(pathTone) : activeNodeIds.has(node.index)
                const isOutput = node.layerId === 'output'
                const isInput = node.layerId === 'input'
                if (isOutput) {
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
                        x={point.x - 16}
                        y={point.y - 11}
                        width="32"
                        height="22"
                        rx="8"
                        className={nodeClassForTone(pathTone, onPath, selected?.index === node.index)}
                      />
                      <title>{node.label}</title>
                    </g>
                  )
                }
                return (
                  <g
                    key={node.nodeId}
                    className={isInput ? undefined : 'neural-node-group'}
                    onClick={isInput ? undefined : () => setSelected(node)}
                    role={isInput ? undefined : 'button'}
                    tabIndex={isInput ? undefined : 0}
                    aria-label={isInput ? undefined : node.label}
                    onKeyDown={
                      isInput
                        ? undefined
                        : (event) => {
                            if (event.key === 'Enter' || event.key === ' ') {
                              event.preventDefault()
                              setSelected(node)
                            }
                          }
                    }
                  >
                    <circle
                      cx={point.x}
                      cy={point.y}
                      r={isInput ? 7 : 10}
                      className={nodeClassForTone(pathTone, onPath, selected?.index === node.index)}
                    >
                      <title>{node.label}</title>
                    </circle>
                  </g>
                )
              })}
        </svg>
      </div>

      <div className="neural-replay-panels" aria-live="polite">
        <section className="neural-replay-panel">
          <h4>LLM 1 → cascade</h4>
          {llm1Payload ? (
            <ul className="neural-feed-list">
              <li>Channel: {resolveString(llm1Payload.channel, strings) ?? '—'}</li>
              <li>Role: {resolveString(llm1Payload.authorRole, strings) ?? '—'}</li>
              <li>{resolveString(llm1Payload.message, strings) ?? 'Message payload recorded'}</li>
            </ul>
          ) : (
            <p className="dashboard-hint">Step to an Llm1Input frame for the synthetic ticket feed.</p>
          )}
        </section>
        <section className="neural-replay-panel">
          <h4>LLM 2 feedback</h4>
          {llm2Payload ? (
            <ul className="neural-feed-list">
              <li>
                {llm2Payload.accepted ? 'LGTM / within tolerance' : 'REVISE'} · evidence{' '}
                {llm2Payload.targetScore?.toFixed?.(3) ?? '—'} · relevance{' '}
                {llm2Payload.targetRelevance?.toFixed?.(3) ?? '—'}
              </li>
              <li>{resolveString(llm2Payload.feedback, strings) ?? 'No written feedback'}</li>
            </ul>
          ) : (
            <p className="dashboard-hint">Step to Llm2Evaluation for teacher / audit notes.</p>
          )}
        </section>
        <section className="neural-replay-panel neural-replay-panel--wide">
          <h4>Weight update feed</h4>
          {recentWeightFeed.length > 0 ? (
            <ul className="neural-feed-list neural-feed-list--mono">
              {recentWeightFeed.map((line, index) => (
                <li key={`${index}-${line}`}>{line}</li>
              ))}
            </ul>
          ) : (
            <p className="dashboard-hint">
              Loss / backprop / SGD lines appear as the playhead reaches training epochs.
            </p>
          )}
          {updatePayload && (
            <p className="dashboard-hint">
              Current update · {updatePayload.optimizer ?? 'momentum-SGD'} · lr{' '}
              {updatePayload.learningRate ?? '—'} · Δ params {updatePayload.parameters?.length ?? 0}
            </p>
          )}
        </section>
      </div>

      <p className="dashboard-hint">
        Selected: {selected ? `${selected.label} (${selected.layerId})` : 'none'} · Integrity:{' '}
        {replay.integrity?.reportChecksum ? 'recorded checksum' : 'not supplied'} · Completion:{' '}
        {replay.completionStatus}. Max quality (+ Detail) renders every node with fixed open spacing and cubic
        curve-fit edges. Hover or select a node for its name.
      </p>
      <section className="sm-panel">
        <div className="sm-panel-header">
          <h3>Recorded frame inspector</h3>
        </div>
        <p className="dashboard-hint">
          Phase: {phase || 'none'} · ticket {frame?.ticketIndex ?? '—'} · pass {frame?.passIndex ?? '—'} · epoch{' '}
          {frame?.epoch ?? '—'}. Loss frames use binary CE on evidence/relevance and categorical cross-entropy on the
          prediction classes.
        </p>
        <pre className="dashboard-hint">
          {payload ? JSON.stringify(payload, null, 2) : 'No payload for this frame.'}
        </pre>
      </section>
    </section>
  )
}
