import { useEffect, useMemo, useState } from 'react'
import { FontAwesomeIcon } from '@fortawesome/react-fontawesome'
import { byPrefixAndName } from '../../icons/byPrefixAndName'
import type { NeuralNetReplay, ReplayEdge, ReplayNode } from '../../types/neuralNetReplay'

export function ReplayViewer({ replay }: { replay: NeuralNetReplay }) {
  const [frameIndex, setFrameIndex] = useState(0)
  const [detail, setDetail] = useState(0)
  const [playing, setPlaying] = useState(false)
  const [ticket, setTicket] = useState<number | 'all'>('all')
  const [selected, setSelected] = useState<ReplayNode | null>(null)
  const [reducedMotion, setReducedMotion] = useState(() => window.matchMedia('(prefers-reduced-motion: reduce)').matches)
  const frames = useMemo(() => ticket === 'all' ? replay.frames : replay.frames.filter(frame => frame.ticketIndex === ticket), [replay, ticket])
  const frame = frames[Math.min(frameIndex, Math.max(0, frames.length - 1))]
  useEffect(() => { setFrameIndex(0) }, [ticket, replay.sessionId])
  useEffect(() => { const query = window.matchMedia('(prefers-reduced-motion: reduce)'); const change = () => setReducedMotion(query.matches); query.addEventListener('change', change); return () => query.removeEventListener('change', change) }, [])
  useEffect(() => {
    if (!playing || reducedMotion) return
    const timer = window.setInterval(() => setFrameIndex(value => value + 1 < frames.length ? value + 1 : (setPlaying(false), value)), 600)
    return () => window.clearInterval(timer)
  }, [playing, frames.length, reducedMotion])
  const inputs = replay.topology.nodes.filter(node => node.layerId === 'input')
  const hidden = replay.topology.nodes.filter(node => node.layerId === 'hidden')
  const output = replay.topology.nodes.filter(node => node.layerId === 'output')
  const visibleInputs = detail === 2 ? inputs : detail === 1 ? inputs.slice(0, 16) : []
  const nodes: ReplayNode[] = [...visibleInputs, ...hidden, ...output]
  const visibleIds = new Set(nodes.map(node => node.index))
  const edges: ReplayEdge[] = detail === 0 ? [] : replay.topology.edges.filter(edge => visibleIds.has(edge.sourceNodeIndex) && visibleIds.has(edge.targetNodeIndex))
  const nodeAt = (node: ReplayNode): { x: number; y: number } => {
    if (node.layerId === 'input') { const index = visibleInputs.findIndex(item => item.index === node.index); return { x: 140, y: 20 + index * (360 / Math.max(1, visibleInputs.length - 1)) } }
    if (node.layerId === 'hidden') { const index = hidden.findIndex(item => item.index === node.index); return { x: 450, y: 45 + index * 45 } }
    const index = output.findIndex(item => item.index === node.index); return { x: 720, y: 147 + index * 120 }
  }
  const nodeByIndex = new Map(replay.topology.nodes.map(node => [node.index, node]))
  const isBackward = frame?.phase === 'BackwardPropagation'
  const forwardPayload = frame && (frame.phase === 'InitialForward' || frame.phase === 'EpochForward' || frame.phase === 'PostUpdateForward')
    ? replay.payloads?.forwardPasses?.[frame.payloadIndex] as { edgeContributions?: { index: number; value: number }[] } | undefined : undefined
  const activeParameters = new Set((forwardPayload?.edgeContributions ?? []).filter(item => item.value !== 0).map(item => item.index))
  const finalVerdict = frame?.phase === 'FinalVerdict' ? replay.payloads?.finalVerdicts?.[frame.payloadIndex] as { accepted?: boolean } | undefined : undefined
  const payloadCollection: Record<string, string> = { Llm1Input: 'inputs', InitialForward: 'forwardPasses', Llm2Evaluation: 'evaluations', VoteResolution: 'voteSampling', EpochForward: 'forwardPasses', LossCalculation: 'losses', BackwardPropagation: 'backpropagations', ParameterUpdate: 'parameterUpdates', PostUpdateForward: 'forwardPasses', FinalVerdict: 'finalVerdicts' }
  const payload = frame ? replay.payloads?.[payloadCollection[frame.phase]]?.[frame.payloadIndex] : null
  return <section className="sm-panel neural-graph-panel">
    <div className="sm-panel-header"><h3>V2 replay</h3></div>
    <div className="sm-form-actions">
      <label className="sm-label">Ticket <select className="sm-input" value={ticket} onChange={event => setTicket(event.target.value === 'all' ? 'all' : Number(event.target.value))}><option value="all">All</option>{replay.tickets.map(item => <option key={item.ticketIndex} value={item.ticketIndex}>Ticket {item.ticketIndex}</option>)}</select></label>
      <button type="button" className="btn-secondary" onClick={() => setDetail(value => Math.max(0, value - 1))}>− Detail</button><button type="button" className="btn-secondary" onClick={() => setDetail(value => Math.min(2, value + 1))}>+ Detail</button>
      <button type="button" className="btn-secondary" disabled={frameIndex === 0} onClick={() => setFrameIndex(value => Math.max(0, value - 1))}><FontAwesomeIcon icon={byPrefixAndName.fas['backward-step']} /> Step back</button>
      <button type="button" className="btn-secondary" disabled={frameIndex >= frames.length - 1} onClick={() => setFrameIndex(value => Math.min(frames.length - 1, value + 1))}><FontAwesomeIcon icon={byPrefixAndName.fas['backward-step']} rotation={180} /> Step forward</button>
      <button type="button" className="btn-primary" disabled={frames.length === 0 || reducedMotion} onClick={() => setPlaying(value => !value)}><FontAwesomeIcon icon={playing ? byPrefixAndName.fas.pause : byPrefixAndName.fas.play} /> {playing ? 'Pause' : 'Play'}</button>
    </div>
    <p className="dashboard-hint">Frame {frames.length ? frameIndex + 1 : 0} of {frames.length} · {frame?.phase ?? 'No recorded frames'} · {detail === 0 ? `Clustered: ${inputs.length} input nodes, ${replay.topology.edges.length} edges` : detail === 1 ? 'Layer detail' : 'Full detail'}{reducedMotion ? ' · Playback disabled by reduced-motion preference' : ''}</p>
    <svg className="neural-graph" viewBox="0 0 900 420" role="img" aria-label="Recorded neural network topology">
      {edges.map(edge => { const source = nodeByIndex.get(edge.sourceNodeIndex); const target = nodeByIndex.get(edge.targetNodeIndex); if (!source || !target) return null; const from = nodeAt(source); const to = nodeAt(target); const active = activeParameters.has(edge.parameterIndex); const verdictClass = finalVerdict ? (finalVerdict.accepted ? ' neural-edge--accepted' : ' neural-edge--revision') : ''; return <line key={edge.index} x1={from.x} y1={from.y} x2={to.x} y2={to.y} className={`neural-edge neural-edge--recorded${active ? ' neural-edge--active' : ''}${isBackward ? ' neural-edge--backward' : ''}${verdictClass}`} /> })}
      {detail === 0 ? <><circle cx="160" cy="210" r="56" className="neural-node" /><text x="160" y="215" textAnchor="middle" className="neural-node-text">{inputs.length} inputs</text></> : nodes.filter(node => node.layerId === 'input').map((node, index) => <circle key={node.nodeId} cx="140" cy={20 + index * (360 / Math.max(1, visibleInputs.length - 1))} r="7" className="neural-node" />)}
      {hidden.map((node, index) => <g key={node.nodeId} className="neural-node-group" onClick={() => setSelected(node)}><circle cx="450" cy={45 + index * 45} r="15" className={`neural-node ${selected?.index === node.index ? 'neural-node--selected' : ''}`} /><text x="450" y={50 + index * 45} textAnchor="middle" className="neural-node-text">{index + 1}</text></g>)}
      {output.map((node, index) => <g key={node.nodeId} className="neural-node-group" onClick={() => setSelected(node)}><rect x="720" y={125 + index * 120} width="135" height="44" rx="12" className={`neural-node ${selected?.index === node.index ? 'neural-node--selected' : ''}`} /><text x="787" y={152 + index * 120} textAnchor="middle" className="neural-node-text">{node.label}</text></g>)}
    </svg>
    <p className="dashboard-hint">Selected: {selected ? `${selected.label} (${selected.layerId})` : 'none'} · Integrity: {replay.integrity?.reportChecksum ? 'recorded checksum' : 'not supplied'} · Completion: {replay.completionStatus}. The visible topology and timeline are recorded report data; detail only changes presentation.</p>
    <section className="sm-panel"><div className="sm-panel-header"><h3>Recorded frame inspector</h3></div><p className="dashboard-hint">Phase: {frame?.phase ?? 'none'} · ticket {frame?.ticketIndex ?? '—'} · pass {frame?.passIndex ?? '—'} · epoch {frame?.epoch ?? '—'}. This panel shows the recorded LLM context, loss, gradients, parameter updates, vote resolution, or verdict for the selected canonical frame.</p><pre className="dashboard-hint">{payload ? JSON.stringify(payload, null, 2) : 'No payload for this frame.'}</pre></section>
  </section>
}
