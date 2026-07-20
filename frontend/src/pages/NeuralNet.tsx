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
import type { NeuralNetDataManagement, NeuralNetTrainingFeedback, NeuralNetTrainingSession, NeuralNetVisualizer, NeuralTrainingMode } from '../types/neuralNet'

type NeuralView = 'training' | 'feedback' | 'data' | 'visualizer'
type ReplayReport = { schemaVersion?: string; initialParameters?: unknown; finalParameters?: unknown; topology?: { nodes?: unknown[]; edges?: unknown[] }; initialState?: { hiddenBias?: number[]; outputBias?: number[] }; finalState?: { hiddenBias?: number[]; outputBias?: number[] }; tickets?: unknown[]; sessionId?: string }

function viewForPath(pathname: string): NeuralView {
  if (pathname.endsWith('/Training')) return 'training'
  if (pathname.endsWith('/DataManagement')) return 'data'
  if (pathname.endsWith('/Visualizer')) return 'visualizer'
  return 'feedback'
}

function NetworkGraph({ visualizer, replay }: { visualizer: NeuralNetVisualizer; replay: ReplayReport | null }) {
  const [selected, setSelected] = useState('evidence')
  const [zoom, setZoom] = useState(1)
  const hidden = replay?.finalState?.hiddenBias ?? replay?.initialState?.hiddenBias ?? Array.from({ length: visualizer.hiddenNodes }, (_, index) => (index - 3) / 8)
  const inputs = ['Ticket requirement', 'Prior score', 'Chat context', 'Current message']
  const outputs = visualizer.outputNodes
  const edges = zoom > 1.15
  return <section className="sm-panel neural-graph-panel">
    <div className="sm-panel-header"><h3><FontAwesomeIcon icon={faDiagramProject} /> Interactive model map</h3></div>
    <p className="dashboard-hint">This is a clear, grouped view of the fixed low-memory model. Select a node to inspect it; zoom reveals its representative connections. Imported training reports replay recorded bias states.</p>
    <label className="sm-label" htmlFor="neural-zoom">Detail level {zoom.toFixed(1)}×</label>
    <input id="neural-zoom" className="neural-zoom" type="range" min="1" max="1.6" step="0.1" value={zoom} onChange={(event) => setZoom(Number(event.target.value))} />
    <svg className="neural-graph" viewBox="0 0 900 360" role="img" aria-label="Layered neural network graph">
      {edges && hidden.map((_, index) => inputs.map((__, inputIndex) => <line key={`i-${inputIndex}-${index}`} x1="190" y1={75 + inputIndex * 72} x2="410" y2={45 + index * 38} className="neural-edge" style={{ opacity: 0.2 + Math.abs(hidden[index] ?? 0) * 0.45 }} />))}
      {edges && hidden.map((_, index) => outputs.map((__, outputIndex) => <line key={`o-${index}-${outputIndex}`} x1="490" y1={45 + index * 38} x2="710" y2={120 + outputIndex * 110} className="neural-edge" style={{ opacity: 0.25 + Math.abs(hidden[index] ?? 0) * 0.45 }} />))}
      <text x="70" y="26" className="neural-layer-label">Inputs</text><text x="390" y="26" className="neural-layer-label">Hidden layer</text><text x="700" y="70" className="neural-layer-label">Outputs</text>
      {inputs.map((label, index) => <g key={label} onClick={() => setSelected(label)} className="neural-node-group"><rect x="35" y={50 + index * 72} width="155" height="42" rx="12" className={`neural-node ${selected === label ? 'neural-node--selected' : ''}`} /><text x="112" y={76 + index * 72} textAnchor="middle" className="neural-node-text">{label}</text></g>)}
      {hidden.map((bias, index) => <g key={index} onClick={() => setSelected(`Hidden neuron ${index + 1}`)} className="neural-node-group"><circle cx="450" cy={55 + index * 38} r="15" className={`neural-node ${selected === `Hidden neuron ${index + 1}` ? 'neural-node--selected' : ''}`} style={{ opacity: 0.65 + Math.min(Math.abs(bias ?? 0), .35) }} /><title>{`Hidden neuron ${index + 1}, bias ${(bias ?? 0).toFixed(3)}`}</title></g>)}
      {outputs.map((label, index) => <g key={label} onClick={() => setSelected(label)} className="neural-node-group"><rect x="710" y={100 + index * 110} width="155" height="48" rx="12" className={`neural-node ${selected === label ? 'neural-node--selected' : ''}`} /><text x="787" y={130 + index * 110} textAnchor="middle" className="neural-node-text">{label}</text></g>)}
    </svg>
    <p className="dashboard-hint"><strong>Selected:</strong> {selected}. {selected.includes('Hidden') ? `Activation proxy (bias): ${(hidden[Number(selected.split(' ')[selected.split(' ').length - 1]) - 1] ?? 0).toFixed(3)}` : 'Feature/output group shown at semantic zoom.'} {replay && ` Replay session ${replay.sessionId ?? 'imported'} loaded.`}</p>
  </section>
}

export function NeuralNet() {
  const { pathname } = useLocation(); const view = viewForPath(pathname)
  const [feedback, setFeedback] = useState<NeuralNetTrainingFeedback[]>([]); const [data, setData] = useState<NeuralNetDataManagement | null>(null); const [visualizer, setVisualizer] = useState<NeuralNetVisualizer | null>(null); const [sessions, setSessions] = useState<NeuralNetTrainingSession[]>([])
  const [loading, setLoading] = useState(true); const [busyId, setBusyId] = useState<string | null>(null); const [error, setError] = useState(''); const [ticketCount, setTicketCount] = useState(3); const [maxPasses, setMaxPasses] = useState(3); const [mode, setMode] = useState<NeuralTrainingMode>('Both'); const [replay, setReplay] = useState<ReplayReport | null>(null)
  useEffect(() => { let cancelled = false; setLoading(true); setError(''); const load = async () => { try { if (view === 'feedback') { const r = await neuralNetApi.listFeedback(); if (!cancelled) setFeedback(r.data) } else if (view === 'training') { const r = await neuralNetApi.listTrainingSessions(); if (!cancelled) setSessions(r.data) } else if (view === 'data') { const r = await neuralNetApi.getDataManagement(); if (!cancelled) setData(r.data) } else { const r = await neuralNetApi.getVisualizer(); if (!cancelled) setVisualizer(r.data) } } catch { if (!cancelled) setError('Could not load neural-network administration data.') } finally { if (!cancelled) setLoading(false) } }; void load(); return () => { cancelled = true } }, [view])
  async function decide(id: string, approve: boolean) { setBusyId(id); try { if (approve) await neuralNetApi.approve(id); else await neuralNetApi.reject(id); setFeedback(items => items.filter(item => item.scoreEventId !== id)) } catch { setError('The feedback decision could not be saved.') } finally { setBusyId(null) } }
  async function startTraining() { setBusyId('training'); try { await neuralNetApi.startTraining({ ticketCount, maxPassesPerTicket: maxPasses, mode }); const r = await neuralNetApi.listTrainingSessions(); setSessions(r.data) } catch { setError('Training could not be queued.') } finally { setBusyId(null) } }
  async function downloadReport(sessionId: string) {
    setBusyId(sessionId)
    try {
      const response = await neuralNetApi.downloadTrainingReport(sessionId)
      const url = URL.createObjectURL(response.data)
      const link = document.createElement('a')
      link.href = url
      link.download = `neural-net-training-${sessionId}.json`
      link.click()
      URL.revokeObjectURL(url)
    } catch {
      setError('The training report could not be downloaded.')
    } finally { setBusyId(null) }
  }

  function importReplay(event: ChangeEvent<HTMLInputElement>) { const file = event.target.files?.[0]; if (!file) return; const reader = new FileReader(); reader.onload = () => { try { const parsed = parseReplayImport(String(reader.result)); setReplay(parsed); setError('') } catch { setError('That file is not a valid supported V2 neural-network replay.') } }; reader.readAsText(file) }
  const nav = useMemo(() => <div className="server-page-card"><p><Link to="/server/NeuralNet/Training">Training</Link>{' | '}<Link to="/server/NeuralNet/TrainingFeedback">Training Feedback</Link>{' | '}<Link to="/server/NeuralNet/DataManagement">Data Management</Link>{' | '}<Link to="/server/NeuralNet/Visualizer">Visualizer & Replay</Link></p></div>, [])
  return <div className="server-page sm-page"><ServerMaintenanceNav title="Server · Neural Network" /><header className="sm-hero"><div className="sm-hero-icon"><FontAwesomeIcon icon={faBrain} /></div><div className="sm-hero-copy"><h2>Neural Network</h2><p className="server-page-subtitle">A server tool for low-memory scoring, review, training, and replay.</p></div></header>{nav}{error && <p className="error">{error}</p>}{loading ? <LoadingBars message="Loading neural-network data…" /> : <div className="sm-layout sm-layout--single">
    {view === 'training' && <section className="sm-panel"><div className="sm-panel-header"><h3><FontAwesomeIcon icon={faPlay} /> Synthetic training</h3></div><p className="dashboard-hint">LLM 1 produces fictional ticket threads, messages, channels, and roles. LLM 2 reviews each message without seeing LLM 1's vote proposal. This never uses opted-out real tickets.</p><div className="sm-form"><label className="sm-label">Training mode <select className="sm-input" value={mode} onChange={e => setMode(e.target.value as NeuralTrainingMode)}><option value="Both">Both</option><option value="Moderation">Moderation</option><option value="Tutoring">Tutoring</option></select></label><label className="sm-label">Tickets <input className="sm-input" type="number" min="1" max="10" value={ticketCount} onChange={e => setTicketCount(Number(e.target.value))} /></label><label className="sm-label">Maximum passes per message <input className="sm-input" type="number" min="1" max="6" value={maxPasses} onChange={e => setMaxPasses(Number(e.target.value))} /></label><div className="sm-form-actions"><button type="button" className="btn-primary" disabled={busyId === 'training'} onClick={() => void startTraining()}><FontAwesomeIcon icon={faPlay} /> Start training</button></div></div><ul className="ticket-watches-list">{sessions.map(s => <li key={s.sessionId} className="ticket-watch-chip"><strong>{s.status} · {s.mode} · {s.requestedTicketCount} tickets</strong><span>Up to {s.maxPassesPerTicket} passes per message</span>{s.hasReport && <button type="button" className="btn-secondary" disabled={busyId === s.sessionId} onClick={() => void downloadReport(s.sessionId)}>Download report</button>}{s.failureReason && <small>{s.failureReason}</small>}</li>)}</ul></section>}
    {view === 'feedback' && <section className="sm-panel"><div className="sm-panel-header"><h3>Training Feedback</h3></div>{feedback.length === 0 ? <p className="dashboard-hint">No reviewer feedback is awaiting approval.</p> : <ul className="ticket-watches-list">{feedback.map(item => <li key={item.scoreEventId} className="ticket-watch-chip"><strong>{item.category} · student {item.studentScore.toFixed(3)} → reviewer {item.reviewerScore.toFixed(3)}</strong><span>{item.messagePreview}</span><small>{item.explanation ?? 'No reviewer explanation supplied.'}</small><div className="sm-form-actions"><button type="button" className="btn-primary" disabled={busyId === item.scoreEventId} onClick={() => void decide(item.scoreEventId, true)}><FontAwesomeIcon icon={faCheck} /> Approve</button><button type="button" className="btn-secondary" disabled={busyId === item.scoreEventId} onClick={() => void decide(item.scoreEventId, false)}><FontAwesomeIcon icon={faXmark} /> Reject</button></div></li>)}</ul>}</section>}
    {view === 'data' && data && <section className="sm-panel"><div className="sm-panel-header"><h3><FontAwesomeIcon icon={faDatabase} /> Data Management</h3></div><p className="dashboard-hint">PostgreSQL is authoritative; the vector store is a retrieval mirror.</p><ul className="ticket-watches-list"><li className="ticket-watch-chip"><strong>{data.trainingExamples}</strong><span>Approved examples</span></li><li className="ticket-watch-chip"><strong>{data.vectorExamples}</strong><span>Vector examples</span></li><li className="ticket-watch-chip"><strong>{data.pendingFeedback}</strong><span>Pending feedback</span></li></ul></section>}
    {view === 'visualizer' && visualizer && <><section className="sm-panel"><div className="sm-panel-header"><h3><FontAwesomeIcon icon={faFileImport} /> Import a replay file</h3></div><p className="dashboard-hint">Load a downloaded V2 report to replay its recorded topology and canonical frames locally. Importing does not train the model.</p><input className="sm-input" type="file" accept="application/json,.json" onChange={importReplay} /></section>{replay?.schemaVersion ? <ReplayViewer replay={replay as NeuralNetReplay} /> : <NetworkGraph visualizer={visualizer} replay={replay} />}</>}
  </div>}</div>
}
