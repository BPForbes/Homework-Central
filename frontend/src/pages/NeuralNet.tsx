import { ChangeEvent, useEffect, useMemo, useState } from 'react'
import { Link, useLocation } from 'react-router-dom'
import { FontAwesomeIcon } from '@fortawesome/react-fontawesome'
import { faBrain, faCheck, faDatabase, faDiagramProject, faFileImport, faPlay, faXmark } from '@fortawesome/free-solid-svg-icons'
import { neuralNetApi } from '../api/neuralNetApi'
import { ServerMaintenanceNav } from '../components/layout/ServerMaintenanceNav'
import { LoadingBars } from '../components/LoadingBars'
import { ReplayViewer } from '../components/neuralNet/ReplayViewer'
import type { NeuralNetReplay } from '../types/neuralNetReplay'
import { parseReplayImport } from '../utils/neuralNetReplay'
import type { NeuralModelKindChatMonitoring, NeuralNetDataManagement, NeuralNetTrainingFeedback, NeuralNetTrainingSession, NeuralNetVisualizer, NeuralTrainingMode } from '../types/neuralNet'

type NeuralView = 'training' | 'feedback' | 'data' | 'visualizer'
type ReplayReport = { schemaVersion?: string; initialParameters?: unknown; finalParameters?: unknown; topology?: { nodes?: unknown[]; edges?: unknown[] }; initialState?: { hiddenBias?: number[]; outputBias?: number[] }; finalState?: { hiddenBias?: number[]; outputBias?: number[] }; tickets?: unknown[]; sessionId?: string }

function viewForPath(pathname: string): NeuralView {
  if (pathname.endsWith('/Training')) return 'training'
  if (pathname.endsWith('/DataManagement')) return 'data'
  if (pathname.endsWith('/Visualizer')) return 'visualizer'
  return 'feedback'
}

function NetworkGraph({ visualizer }: { visualizer: NeuralNetVisualizer; replay: ReplayReport | null }) {
  const models = visualizer.models?.length ? visualizer.models : [{
    chatMonitoringKind: 'Moderation' as NeuralModelKindChatMonitoring,
    modelVersion: visualizer.modelVersion,
    layerWidths: [visualizer.inputNodes, visualizer.hiddenNodes, 2],
    layerLabels: ['input', 'hidden', 'output'],
    parameterCount: 0,
    supportExamples: 0,
    nodeCount: visualizer.inputNodes + visualizer.hiddenNodes + 2,
  }]
  const [selectedKind, setSelectedKind] = useState<NeuralModelKindChatMonitoring>(models[0].chatMonitoringKind)
  const [selected, setSelected] = useState('Evidence score')
  const model = models.find(item => item.chatMonitoringKind === selectedKind) ?? models[0]
  const layerWidths = model.layerWidths.length ? model.layerWidths : [48, 20, 30, 24, 18, 2]
  const layerLabels = model.layerLabels.length === layerWidths.length
    ? model.layerLabels
    : layerWidths.map((_, index) => index === 0 ? 'input' : index === layerWidths.length - 1 ? 'output' : `hidden-${index}`)
  const layerX = (index: number) => 90 + index * (1260 / Math.max(1, layerWidths.length - 1))
  const nodeY = (layerSize: number, nodeIndex: number) => 70 + nodeIndex * (540 / Math.max(1, layerSize - 1))
  return <section className="sm-panel neural-graph-panel">
    <div className="sm-panel-header"><h3><FontAwesomeIcon icon={faDiagramProject} /> Dual chat-monitor map</h3></div>
    <p className="dashboard-hint">Separate Moderation and Tutoring hashed-MLP lineages. Select a monitor to inspect its fixed topology ({model.modelVersion}; {model.parameterCount} parameters; {model.supportExamples} support examples).</p>
    <div className="sm-form-actions">{models.map(item => <button key={item.chatMonitoringKind} type="button" className={item.chatMonitoringKind === model.chatMonitoringKind ? 'btn-primary' : 'btn-secondary'} onClick={() => { setSelectedKind(item.chatMonitoringKind); setSelected(item.chatMonitoringKind === 'Tutoring' ? 'Evidence score' : 'Evidence score') }}>{item.chatMonitoringKind} · {item.layerWidths.join('→')}</button>)}</div>
    <svg className="neural-graph neural-graph--replay" viewBox="0 0 1440 680" role="img" aria-label={`${model.chatMonitoringKind} chat-monitoring neural network`}>
      {layerLabels.map((label, layerIndex) => <text key={`label-${label}`} x={layerX(layerIndex)} y="28" textAnchor="middle" className="neural-layer-label">{label.replace(/-/g, ' ')}</text>)}
      {layerWidths.slice(0, -1).flatMap((width, layerIndex) => Array.from({ length: Math.min(width, 12) }, (_, source) => Array.from({ length: Math.min(layerWidths[layerIndex + 1], 12) }, (_, target) => <line key={`e-${layerIndex}-${source}-${target}`} x1={layerX(layerIndex)} y1={nodeY(Math.min(width, 12), source)} x2={layerX(layerIndex + 1)} y2={nodeY(Math.min(layerWidths[layerIndex + 1], 12), target)} className="neural-edge" style={{ opacity: 0.12 }} />)))}
      {layerWidths.flatMap((width, layerIndex) => {
        const shown = Math.min(width, 12)
        return Array.from({ length: shown }, (_, nodeIndex) => {
          const isOutput = layerIndex === layerWidths.length - 1
          const label = isOutput ? (nodeIndex === 0 ? 'Evidence score' : 'Relevance') : `${layerLabels[layerIndex]} ${nodeIndex + 1}`
          const x = layerX(layerIndex)
          const y = nodeY(shown, nodeIndex)
          return <g key={`${layerIndex}-${nodeIndex}`} onClick={() => setSelected(label)} className="neural-node-group">
            {isOutput
              ? <rect x={x - 70} y={y - 18} width="140" height="36" rx="10" className={`neural-node ${selected === label ? 'neural-node--selected' : ''}`} />
              : <circle cx={x} cy={y} r={layerIndex === 0 ? 10 : 14} className={`neural-node ${selected === label ? 'neural-node--selected' : ''}`} />}
            {(isOutput || shown <= 8) && <text x={x} y={isOutput ? y + 5 : y + 32} textAnchor="middle" className="neural-node-text">{isOutput ? label : (layerIndex === 0 ? `f${nodeIndex}` : `${nodeIndex + 1}`)}</text>}
            {shown < width && nodeIndex === shown - 1 && <text x={x} y={y + 48} textAnchor="middle" className="neural-layer-label">+{width - shown}</text>}
          </g>
        })
      })}
    </svg>
    <p className="dashboard-hint"><strong>Selected:</strong> {selected}. {model.chatMonitoringKind} monitor · {model.nodeCount} nodes · outputs {visualizer.outputNodes.join(', ')}.</p>
  </section>
}

export function NeuralNet() {
  const { pathname } = useLocation(); const view = viewForPath(pathname)
  const [feedback, setFeedback] = useState<NeuralNetTrainingFeedback[]>([]); const [data, setData] = useState<NeuralNetDataManagement | null>(null); const [visualizer, setVisualizer] = useState<NeuralNetVisualizer | null>(null); const [sessions, setSessions] = useState<NeuralNetTrainingSession[]>([])
  const [loading, setLoading] = useState(true); const [busyId, setBusyId] = useState<string | null>(null); const [error, setError] = useState(''); const [ticketCount, setTicketCount] = useState(3); const [maxPasses, setMaxPasses] = useState(3); const [mode, setMode] = useState<NeuralTrainingMode>('Both'); const [replay, setReplay] = useState<ReplayReport | null>(null)
  useEffect(() => { let cancelled = false; setLoading(true); setError(''); const load = async () => { try { if (view === 'feedback') { const r = await neuralNetApi.listFeedback(); if (!cancelled) setFeedback(r.data) } else if (view === 'training') { const r = await neuralNetApi.listTrainingSessions(); if (!cancelled) setSessions(r.data) } else if (view === 'data') { const r = await neuralNetApi.getDataManagement(); if (!cancelled) setData(r.data) } else { const r = await neuralNetApi.getVisualizer(); if (!cancelled) setVisualizer(r.data) } } catch { if (!cancelled) setError('Could not load neural-network administration data.') } finally { if (!cancelled) setLoading(false) } }; void load(); return () => { cancelled = true } }, [view])
  async function decide(id: string, approve: boolean) { setBusyId(id); try { if (approve) await neuralNetApi.approve(id); else await neuralNetApi.reject(id); setFeedback(items => items.filter(item => item.scoreEventId !== id)) } catch { setError('The feedback decision could not be saved.') } finally { setBusyId(null) } }
  async function startTraining() { setBusyId('training'); try { await neuralNetApi.startTraining({ ticketCount, maxPassesPerTicket: maxPasses, mode }); const r = await neuralNetApi.listTrainingSessions(); setSessions(r.data) } catch { setError('Training could not be queued.') } finally { setBusyId(null) } }
  async function removeSession(sessionId: string) { const busyKey = `remove-${sessionId}`; setBusyId(busyKey); try { await neuralNetApi.removeTrainingSession(sessionId); setSessions(items => items.filter(item => item.sessionId !== sessionId)) } catch { setError('That training request could not be removed. It may still be running.') } finally { setBusyId(null) } }
  async function downloadReport(sessionId: string, chatMonitoringKind?: NeuralModelKindChatMonitoring) {
    const downloadId = `${sessionId}-${chatMonitoringKind ?? 'legacy'}`
    setBusyId(downloadId)
    try {
      const response = await neuralNetApi.downloadTrainingReport(sessionId, chatMonitoringKind)
      const url = URL.createObjectURL(response.data)
      const link = document.createElement('a')
      link.href = url
      link.download = `neural-net-training-${sessionId}${chatMonitoringKind ? `-${chatMonitoringKind.toLowerCase()}` : ''}.json`
      link.click()
      URL.revokeObjectURL(url)
    } catch {
      setError('The training report could not be downloaded.')
    } finally { setBusyId(null) }
  }

  function importReplay(event: ChangeEvent<HTMLInputElement>) { const file = event.target.files?.[0]; if (!file) return; const reader = new FileReader(); reader.onload = () => { try { const parsed = parseReplayImport(String(reader.result)); setReplay(parsed); setError('') } catch { setError('That file is not a valid supported V2 neural-network replay.') } }; reader.readAsText(file) }
  const nav = useMemo(() => <div className="server-page-card"><p><Link to="/server/NeuralNet/Training">Training</Link>{' | '}<Link to="/server/NeuralNet/TrainingFeedback">Training Feedback</Link>{' | '}<Link to="/server/NeuralNet/DataManagement">Data Management</Link>{' | '}<Link to="/server/NeuralNet/Visualizer">Visualizer & Replay</Link></p></div>, [])
  return <div className="server-page sm-page"><ServerMaintenanceNav title="Server · Neural Network" /><header className="sm-hero"><div className="sm-hero-icon"><FontAwesomeIcon icon={faBrain} /></div><div className="sm-hero-copy"><h2>Neural Network</h2><p className="server-page-subtitle">A server tool for low-memory scoring, review, training, and replay.</p></div></header>{nav}{error && <p className="error">{error}</p>}{loading ? <LoadingBars message="Loading neural-network data…" /> : <div className="sm-layout sm-layout--single">
    {view === 'training' && <section className="sm-panel"><div className="sm-panel-header"><h3><FontAwesomeIcon icon={faPlay} /> Synthetic training</h3></div><p className="dashboard-hint">LLM 1 produces fictional ticket threads, messages, channels, and roles. LLM 2 reviews each message without seeing LLM 1's vote proposal. This never uses opted-out real tickets.</p><div className="sm-form"><label className="sm-label">Training mode <select className="sm-input" value={mode} onChange={e => setMode(e.target.value as NeuralTrainingMode)}><option value="Both">Both</option><option value="Moderation">Moderation</option><option value="Tutoring">Tutoring</option></select></label><label className="sm-label">Tickets <input className="sm-input" type="number" min="1" max="10" value={ticketCount} onChange={e => setTicketCount(Number(e.target.value))} /></label><label className="sm-label">Maximum passes per message <input className="sm-input" type="number" min="1" max="6" value={maxPasses} onChange={e => setMaxPasses(Number(e.target.value))} /></label><div className="sm-form-actions"><button type="button" className="btn-primary" disabled={busyId === 'training'} onClick={() => void startTraining()}><FontAwesomeIcon icon={faPlay} /> Start training</button></div></div><ul className="ticket-watches-list">{sessions.map(s => <li key={s.sessionId} className="ticket-watch-chip"><div className="ticket-watch-chip-header"><strong>{s.status} · {s.mode} · {s.requestedTicketCount} tickets</strong><button type="button" className="ticket-watch-chip-remove" aria-label="Remove training request" title={s.status === 'Running' ? 'Running sessions cannot be removed yet' : 'Remove training request'} disabled={s.status === 'Running' || busyId === `remove-${s.sessionId}`} onClick={() => void removeSession(s.sessionId)}><FontAwesomeIcon icon={faXmark} /></button></div><span>Up to {s.maxPassesPerTicket} passes per message</span>{(s.chatMonitoringRuns ?? []).map(run => <div key={run.chatMonitoringKind} className="sm-form-actions"><span>{run.chatMonitoringKind} monitor · {run.status}{run.canonicalGeneration !== undefined ? ` · canonical generation ${run.canonicalGeneration}` : ''}</span>{run.hasWorkerReplay && <button type="button" className="btn-secondary" disabled={busyId === `${s.sessionId}-${run.chatMonitoringKind}`} onClick={() => void downloadReport(s.sessionId, run.chatMonitoringKind)}>Download {run.chatMonitoringKind} replay</button>}</div>)}{s.hasReport && <button type="button" className="btn-secondary" disabled={busyId === `${s.sessionId}-legacy`} onClick={() => void downloadReport(s.sessionId)}>Download legacy report</button>}{s.failureReason && <small>{s.failureReason}</small>}</li>)}</ul></section>}
    {view === 'feedback' && <section className="sm-panel"><div className="sm-panel-header"><h3>Training Feedback</h3></div>{feedback.length === 0 ? <p className="dashboard-hint">No reviewer feedback is awaiting approval.</p> : <ul className="ticket-watches-list">{feedback.map(item => <li key={item.scoreEventId} className="ticket-watch-chip"><strong>{item.category} · student {item.studentScore.toFixed(3)} → reviewer {item.reviewerScore.toFixed(3)}</strong><span>{item.messagePreview}</span><small>{item.explanation ?? 'No reviewer explanation supplied.'}</small><div className="sm-form-actions"><button type="button" className="btn-primary" disabled={busyId === item.scoreEventId} onClick={() => void decide(item.scoreEventId, true)}><FontAwesomeIcon icon={faCheck} /> Approve</button><button type="button" className="btn-secondary" disabled={busyId === item.scoreEventId} onClick={() => void decide(item.scoreEventId, false)}><FontAwesomeIcon icon={faXmark} /> Reject</button></div></li>)}</ul>}</section>}
    {view === 'data' && data && <section className="sm-panel"><div className="sm-panel-header"><h3><FontAwesomeIcon icon={faDatabase} /> Data Management</h3></div><p className="dashboard-hint">PostgreSQL is authoritative; the vector store is a retrieval mirror.</p><ul className="ticket-watches-list"><li className="ticket-watch-chip"><strong>{data.trainingExamples}</strong><span>Approved examples</span></li><li className="ticket-watch-chip"><strong>{data.vectorExamples}</strong><span>Vector examples</span></li><li className="ticket-watch-chip"><strong>{data.pendingFeedback}</strong><span>Pending feedback</span></li></ul></section>}
    {view === 'visualizer' && visualizer && <><section className="sm-panel"><div className="sm-panel-header"><h3><FontAwesomeIcon icon={faFileImport} /> Import a replay file</h3></div><p className="dashboard-hint">Load a downloaded V2 report to replay its recorded topology and canonical frames locally. Importing does not train the model.</p><input className="sm-input" type="file" accept="application/json,.json" onChange={importReplay} /></section>{replay?.schemaVersion ? <ReplayViewer replay={replay as NeuralNetReplay} /> : <NetworkGraph visualizer={visualizer} replay={replay} />}</>}
  </div>}</div>
}
