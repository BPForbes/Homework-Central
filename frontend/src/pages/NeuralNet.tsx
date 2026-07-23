import { ChangeEvent, useEffect, useMemo, useState } from 'react'
import { Link, useLocation } from 'react-router-dom'
import { FontAwesomeIcon } from '@fortawesome/react-fontawesome'
import { faBrain, faCheck, faDatabase, faDiagramProject, faFileImport, faPlay, faXmark } from '@fortawesome/free-solid-svg-icons'
import { neuralNetApi } from '../api/neuralNetApi'
import { ServerMaintenanceNav } from '../components/layout/ServerMaintenanceNav'
import { LoadingBars } from '../components/LoadingBars'
import { NeuralNetMesh3D, edgeKeysFromDenseParameterIndexes, type MeshPathTone, type NeuralMeshFrame } from '../components/neuralNet/NeuralNetMesh3D'
import { ReplayViewer } from '../components/neuralNet/ReplayViewer'
import type { NeuralNetReplay } from '../types/neuralNetReplay'
import { parseReplayImport } from '../utils/neuralNetReplay'
import type { NeuralModelKindChatMonitoring, NeuralNetDataManagement, NeuralNetTrainingFeedback, NeuralNetTrainingLiveProgress, NeuralNetTrainingSession, NeuralNetVisualizer, NeuralNetVisualizerModel, NeuralTrainingMode } from '../types/neuralNet'

type NeuralView = 'training' | 'feedback' | 'data' | 'visualizer'
type ReplayReport = { schemaVersion?: string; initialParameters?: unknown; finalParameters?: unknown; topology?: { nodes?: unknown[]; edges?: unknown[] }; initialState?: { hiddenBias?: number[]; outputBias?: number[] }; finalState?: { hiddenBias?: number[]; outputBias?: number[] }; tickets?: unknown[]; sessionId?: string }

function viewForPath(pathname: string): NeuralView {
  if (pathname.endsWith('/Training')) return 'training'
  if (pathname.endsWith('/DataManagement')) return 'data'
  if (pathname.endsWith('/Visualizer')) return 'visualizer'
  return 'feedback'
}

function cascadeMeta(model: NeuralNetVisualizerModel) {
  const stage1 = model.stage1LayerWidths?.length
    ? model.stage1LayerWidths
    : model.chatMonitoringKind === 'Tutoring'
      ? [62, 32, 8]
      : [30, 24, 8]
  const stage2 = model.layerWidths.length ? model.layerWidths : [86, 48, 72, 64, 56, 103]
  const role = model.stage1Role ?? (model.chatMonitoringKind === 'Tutoring' ? 'subject-context router' : 'concept-context router')
  const categories = model.categoryCount ?? Math.max(0, (stage2[stage2.length - 1] ?? 2) - 2)
  return {
    stage1,
    stage2,
    role,
    categories,
    composition: model.cascadeComposition ?? 'g(f(x))',
    chainRule: model.chainRuleSummary ?? '∂C/∂θ_f = (∂C/∂f)(∂f/∂θ_f)',
    runtime: model.runtimeKind ?? 'HashedMlpV8',
  }
}

function curvedCascadeEdge(
  x1: number,
  y1: number,
  x2: number,
  y2: number,
  edgeIndex: number,
): string {
  const dx = Math.abs(x2 - x1)
  const control = Math.max(24, dx * 0.42)
  const bend = (y2 - y1) * 0.12 + ((edgeIndex % 7) - 3) * 2.2
  return `M ${x1} ${y1} C ${x1 + control} ${y1 + bend}, ${x2 - control} ${y2 - bend}, ${x2} ${y2}`
}

function StageMiniGraph({ widths, title, accentClass }: { widths: number[]; title: string; accentClass: string }) {
  const layerX = (index: number) => 36 + index * (320 / Math.max(1, widths.length - 1))
  const nodeY = (layerSize: number, nodeIndex: number) => 36 + nodeIndex * (120 / Math.max(1, Math.min(layerSize, 6) - 1))
  return (
    <div className={`neural-cascade-stage ${accentClass}`}>
      <p className="neural-cascade-stage-title">{title}</p>
      <p className="neural-cascade-stage-path">{widths.join(' → ')}</p>
      <svg className="neural-cascade-mini" viewBox="0 0 360 180" role="img" aria-label={title}>
        {widths.slice(0, -1).flatMap((width, layerIndex) => {
          const shownSource = Math.min(width, 6)
          const shownTarget = Math.min(widths[layerIndex + 1], 6)
          return Array.from({ length: shownSource }, (_, source) =>
            Array.from({ length: shownTarget }, (_, target) => (
              <path
                key={`e-${layerIndex}-${source}-${target}`}
                d={curvedCascadeEdge(
                  layerX(layerIndex),
                  nodeY(shownSource, source),
                  layerX(layerIndex + 1),
                  nodeY(shownTarget, target),
                  source * shownTarget + target,
                )}
                fill="none"
                className="neural-edge neural-edge--cascade"
              />
            )),
          )
        })}
        {widths.flatMap((width, layerIndex) => {
          const shown = Math.min(width, 6)
          return Array.from({ length: shown }, (_, nodeIndex) => (
            <circle
              key={`n-${layerIndex}-${nodeIndex}`}
              cx={layerX(layerIndex)}
              cy={nodeY(shown, nodeIndex)}
              r={layerIndex === 0 ? 7 : 9}
              className="neural-node neural-node--cascade"
            />
          ))
        })}
      </svg>
    </div>
  )
}

function NetworkGraph({ visualizer, replay }: { visualizer: NeuralNetVisualizer; replay: ReplayReport | null }) {
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
  const model = models.find(item => item.chatMonitoringKind === selectedKind) ?? models[0]
  const meta = cascadeMeta(model)
  const layerWidths = meta.stage2
  const layerLabels = model.layerLabels.length === layerWidths.length
    ? model.layerLabels
    : layerWidths.map((_, index) => (index === 0 ? 'input' : index === layerWidths.length - 1 ? 'output' : `hidden-${index}`))

  return (
    <section className="sm-panel neural-graph-panel">
      <div className="sm-panel-header">
        <h3><FontAwesomeIcon icon={faDiagramProject} /> Cascade map · {meta.composition}</h3>
      </div>
      <p className="dashboard-hint">
        Each monitor is a two-stage cascade. Stage 1 embeds context; stage 2 scores evidence, relevance, and category
        with categorical cross-entropy on the class head. Training applies the chain rule so stage-1 weights move when
        stage-2 loss needs a different embedding.
      </p>

      <div className="sm-form-actions neural-cascade-tabs">
        {models.map(item => {
          const itemMeta = cascadeMeta(item)
          return (
            <button
              key={item.chatMonitoringKind}
              type="button"
              className={item.chatMonitoringKind === model.chatMonitoringKind ? 'btn-primary' : 'btn-secondary'}
              onClick={() => setSelectedKind(item.chatMonitoringKind)}
            >
              {item.chatMonitoringKind} cascade · {itemMeta.categories} classes
            </button>
          )
        })}
      </div>

      <div className="neural-cascade-overview" aria-live="polite">
        <StageMiniGraph
          widths={meta.stage1}
          title={`Stage 1 · f(x) · ${meta.role}`}
          accentClass="neural-cascade-stage--f"
        />
        <div className="neural-cascade-bridge" aria-hidden="true">
          <span className="neural-cascade-bridge-formula">{meta.composition}</span>
          <span className="neural-cascade-bridge-arrow" />
          <span className="neural-cascade-bridge-rule">{meta.chainRule}</span>
        </div>
        <StageMiniGraph
          widths={meta.stage2}
          title={`Stage 2 · g · ${model.modelVersion}`}
          accentClass="neural-cascade-stage--g"
        />
      </div>

      <dl className="neural-cascade-meta">
        <div><dt>Runtime</dt><dd>{meta.runtime}</dd></div>
        <div><dt>Parameters</dt><dd>{model.parameterCount.toLocaleString()}</dd></div>
        <div><dt>Support</dt><dd>{model.supportExamples.toLocaleString()}</dd></div>
        <div><dt>Category head</dt><dd>{meta.categories} softmax classes</dd></div>
        <div><dt>Cascade slots</dt><dd>features 78–85 ← f(x)</dd></div>
      </dl>

      <p className="dashboard-hint neural-cascade-detail-label">
        Stage-2 detail ({model.chatMonitoringKind}): {layerWidths.join(' → ')}. Explore the full architecture in the
        scaled 3D mesh (drag to orbit, scroll to zoom).
      </p>
      <NeuralNetMesh3D
        className="neural-mesh3d--replay"
        title={`${model.chatMonitoringKind} stage-2 · 3D mesh`}
        layerWidths={layerWidths}
        layerLabels={layerLabels}
        frame={{
          pathTone: 'idle',
          activeNodeIndexes: [],
          activeEdgeKeys: [],
        }}
      />
      <p className="dashboard-hint">
        {model.chatMonitoringKind} stage-2 · {model.nodeCount} nodes · cascade {meta.composition} with chain-rule
        updates into {meta.role}.
        {replay ? ' A local replay import is also loaded below when schema V2 is present.' : ''}
      </p>
    </section>
  )
}

function liveToneClass(phase: string): string {
  const lower = phase.toLowerCase()
  if (lower.includes('backprop') || lower.includes('loss') || lower.includes('ccel')) return 'neural-live-tone--backprop'
  if (lower.includes('llm2') || lower.includes('audit') || lower.includes('feedback')) return 'neural-live-tone--reeval'
  if (lower.includes('forward') || lower.includes('llm1') || lower.includes('generat') || lower.includes('accepted')) {
    return 'neural-live-tone--forward'
  }
  return 'neural-live-tone--idle'
}

function meshToneFromProgress(progress: NeuralNetTrainingLiveProgress): MeshPathTone {
  const tone = (progress.pathTone ?? '').toLowerCase()
  if (tone === 'forward' || tone === 'reeval' || tone === 'backprop' || tone === 'accepted' || tone === 'revision' || tone === 'idle') {
    return tone
  }
  const phaseClass = liveToneClass(progress.phase)
  if (phaseClass.endsWith('backprop')) return 'backprop'
  if (phaseClass.endsWith('reeval')) return 'reeval'
  if (phaseClass.endsWith('forward')) return 'forward'
  return 'idle'
}

function liveMeshFrame(progress: NeuralNetTrainingLiveProgress, layerWidths: number[]): NeuralMeshFrame {
  const pathTone = meshToneFromProgress(progress)
  const activeNodes = progress.activeNodeIndexes ?? []
  const edgeKeys = edgeKeysFromDenseParameterIndexes(
    layerWidths,
    progress.activeEdgeParameterIndexes ?? [],
  )
  return {
    pathTone,
    activeNodeIndexes: activeNodes,
    activeEdgeKeys: edgeKeys,
  }
}

function LiveTrainingProgress({
  progress,
  status,
}: {
  progress: NeuralNetTrainingLiveProgress
  status: string
}) {
  const tone = liveToneClass(progress.phase)
  const layerWidths = progress.layerWidths?.length
    ? progress.layerWidths
    : progress.activeChatMonitoringKind === 'Tutoring'
      ? [86, 40, 56, 48, 40, 16]
      : [86, 48, 72, 64, 56, 103]
  const layerLabels = progress.layerLabels?.length
    ? progress.layerLabels
    : layerWidths.map((_, index) => (index === 0 ? 'input' : index === layerWidths.length - 1 ? 'output' : `hidden-${index}`))
  const frame = liveMeshFrame(progress, layerWidths)
  return (
    <div className={`neural-live-progress ${tone}`} aria-live="polite">
      <div className="neural-live-progress-header">
        <strong>{progress.phase || status}</strong>
        <span>
          tickets {progress.ticketsGenerated}/{progress.ticketsRequested}
          {progress.activeChatMonitoringKind ? ` · ${progress.activeChatMonitoringKind}` : ''}
        </span>
      </div>
      <p className="neural-ops-strip">
        Ops · Leaky ReLU · BCE + categorical CE (CCEL) · backprop · momentum SGD
        {progress.latestLossSummary ? ` · ${progress.latestLossSummary}` : ''}
      </p>
      <NeuralNetMesh3D
        className="neural-mesh3d--live"
        title="Live training · 3D neural mesh"
        layerWidths={layerWidths}
        layerLabels={layerLabels}
        frame={frame}
      />
      <div className="neural-replay-panels neural-replay-panels--live">
        <section className="neural-replay-panel">
          <h4>LLM 1 training data</h4>
          <p className="dashboard-hint">{progress.latestLlm1Summary ?? 'Waiting for scenario generation…'}</p>
          <p className="dashboard-hint">
            Processed {progress.ticketsProcessed} tickets · {progress.messagesProcessed} messages ·{' '}
            {progress.examplesPersisted} examples
          </p>
        </section>
        <section className="neural-replay-panel">
          <h4>LLM 2 → LLM 1 feedback</h4>
          <p className="dashboard-hint">{progress.latestLlm2Feedback ?? 'No audit notes yet.'}</p>
          <p className="dashboard-hint">Audits {progress.auditsCompleted}</p>
          {(progress.generatorHints?.length ?? 0) > 0 && (
            <ul className="neural-feed-list">
              {progress.generatorHints.slice(-4).map((hint) => (
                <li key={hint}>{hint}</li>
              ))}
            </ul>
          )}
        </section>
        <section className="neural-replay-panel neural-replay-panel--wide">
          <h4>Weight update feed</h4>
          {(progress.weightUpdateFeed?.length ?? 0) > 0 ? (
            <ul className="neural-feed-list neural-feed-list--mono">
              {progress.weightUpdateFeed.map((line) => (
                <li key={line}>{line}</li>
              ))}
            </ul>
          ) : (
            <p className="dashboard-hint">Weight deltas appear once mini-batch SGD / backprop begins.</p>
          )}
        </section>
      </div>
    </div>
  )
}

export function NeuralNet() {
  const { pathname } = useLocation(); const view = viewForPath(pathname)
  const [feedback, setFeedback] = useState<NeuralNetTrainingFeedback[]>([]); const [data, setData] = useState<NeuralNetDataManagement | null>(null); const [visualizer, setVisualizer] = useState<NeuralNetVisualizer | null>(null); const [sessions, setSessions] = useState<NeuralNetTrainingSession[]>([])
  const [loading, setLoading] = useState(true); const [busyId, setBusyId] = useState<string | null>(null); const [error, setError] = useState(''); const [ticketCount, setTicketCount] = useState(2); const [maxPasses, setMaxPasses] = useState(1); const [mode, setMode] = useState<NeuralTrainingMode>('Moderation');   const [replay, setReplay] = useState<ReplayReport | null>(null)
  const sessionStatusRef = useMemo(() => new Map<string, string>(), [])
  useEffect(() => { let cancelled = false; setLoading(true); setError(''); const load = async () => { try { if (view === 'feedback') { const r = await neuralNetApi.listFeedback(); if (!cancelled) setFeedback(r.data) } else if (view === 'training') { const r = await neuralNetApi.listTrainingSessions(); if (!cancelled) setSessions(r.data) } else if (view === 'data') { const r = await neuralNetApi.getDataManagement(); if (!cancelled) setData(r.data) } else { const r = await neuralNetApi.getVisualizer(); if (!cancelled) setVisualizer(r.data) } } catch { if (!cancelled) setError('Could not load neural-network administration data.') } finally { if (!cancelled) setLoading(false) } }; void load(); return () => { cancelled = true } }, [view])

  const hasActiveTraining = sessions.some((session) => session.status === 'Running' || session.status === 'Queued')
  useEffect(() => {
    if (view !== 'training' || !hasActiveTraining) return
    const timer = window.setInterval(() => {
      void neuralNetApi.listTrainingSessions().then((response) => setSessions(response.data)).catch(() => undefined)
    }, 2000)
    return () => window.clearInterval(timer)
  }, [view, hasActiveTraining])

  useEffect(() => {
    const completedBoth: { sessionId: string; kinds: NeuralModelKindChatMonitoring[] }[] = []
    for (const session of sessions) {
      const previous = sessionStatusRef.get(session.sessionId)
      sessionStatusRef.set(session.sessionId, session.status)
      if (previous === undefined) continue
      if (!(previous === 'Running' || previous === 'Queued')) continue
      if (session.status !== 'Completed' || session.mode !== 'Both') continue
      const kinds = (session.chatMonitoringRuns ?? [])
        .filter((run) => run.hasWorkerReplay)
        .map((run) => run.chatMonitoringKind)
      if (kinds.length >= 2) completedBoth.push({ sessionId: session.sessionId, kinds })
    }
    for (const item of completedBoth) {
      void downloadCascadeReports(item.sessionId, item.kinds)
    }
  }, [sessions, sessionStatusRef])

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
      const kindSuffix = chatMonitoringKind === 'Moderation'
        ? '-moderation'
        : chatMonitoringKind === 'Tutoring'
          ? '-tutoring'
          : ''
      link.download = `neural-net-training-${sessionId}${kindSuffix}.json`
      link.click()
      URL.revokeObjectURL(url)
    } catch {
      setError('The training report could not be downloaded.')
    } finally { setBusyId(null) }
  }

  async function downloadCascadeReports(sessionId: string, kinds: NeuralModelKindChatMonitoring[]) {
    setBusyId(`${sessionId}-both`)
    try {
      for (const kind of kinds) {
        const response = await neuralNetApi.downloadTrainingReport(sessionId, kind)
        const url = URL.createObjectURL(response.data)
        const link = document.createElement('a')
        link.href = url
        link.download = `neural-net-training-${sessionId}-${kind.toLowerCase()}.json`
        link.click()
        URL.revokeObjectURL(url)
        await new Promise((resolve) => window.setTimeout(resolve, 350))
      }
    } catch {
      setError('The Moderation and Tutoring replay files could not both be downloaded.')
    } finally {
      setBusyId(null)
    }
  }

  function importReplay(event: ChangeEvent<HTMLInputElement>) { const file = event.target.files?.[0]; if (!file) return; const reader = new FileReader(); reader.onload = () => { try { const parsed = parseReplayImport(String(reader.result)); setReplay(parsed); setError('') } catch { setError('That file is not a valid supported V2 neural-network replay.') } }; reader.readAsText(file) }
  const nav = useMemo(() => <div className="server-page-card"><p><Link to="/server/NeuralNet/Training">Training</Link>{' | '}<Link to="/server/NeuralNet/TrainingFeedback">Training Feedback</Link>{' | '}<Link to="/server/NeuralNet/DataManagement">Data Management</Link>{' | '}<Link to="/server/NeuralNet/Visualizer">Visualizer & Replay</Link></p></div>, [])
  return <div className="server-page sm-page"><ServerMaintenanceNav title="Server · Neural Network" /><header className="sm-hero"><div className="sm-hero-icon"><FontAwesomeIcon icon={faBrain} /></div><div className="sm-hero-copy"><h2>Neural Network</h2><p className="server-page-subtitle">Cascade monitors g(f(x)) for moderation and tutoring — chain-rule training, low-memory CPU scoring, review, and replay.</p></div></header>{nav}{error && <p className="error">{error}</p>}{loading ? <LoadingBars message="Loading neural-network data…" /> : <div className="sm-layout sm-layout--single">
    {view === 'training' && <section className="sm-panel"><div className="sm-panel-header"><h3><FontAwesomeIcon icon={faPlay} /> Synthetic cascade training</h3></div><p className="dashboard-hint">LLM 1 builds fictional ticket threads. LLM 2 occasionally steers later scenarios (sampled — not every ticket). Wall-clock is dominated by Ollama calls: prefer one cascade, fewer tickets, and 1 pass for a fast run; use Both / higher counts when you want broader coverage. Each monitor trains as g(f(x)) with ReLU, BCE+CCEL loss, and backprop. Opted-out real tickets are never used.</p><div className="sm-form"><label className="sm-label">Training mode <select className="sm-input" value={mode} onChange={e => setMode(e.target.value as NeuralTrainingMode)}><option value="Both">Both cascades</option><option value="Moderation">Moderation cascade</option><option value="Tutoring">Tutoring cascade</option></select></label><label className="sm-label">Tickets <input className="sm-input" type="number" min="1" max="10" value={ticketCount} onChange={e => setTicketCount(Number(e.target.value))} /></label><label className="sm-label">Maximum passes per message <input className="sm-input" type="number" min="1" max="6" value={maxPasses} onChange={e => setMaxPasses(Number(e.target.value))} /></label><div className="sm-form-actions"><button type="button" className="btn-primary" disabled={busyId === 'training'} onClick={() => void startTraining()}><FontAwesomeIcon icon={faPlay} /> Start training</button></div></div><ul className="ticket-watches-list">{sessions.map(s => {
      const replayRuns = (s.chatMonitoringRuns ?? []).filter((run) => run.hasWorkerReplay)
      const canDownloadBoth = s.mode === 'Both' && replayRuns.length >= 2
      return <li key={s.sessionId} className="ticket-watch-chip"><div className="ticket-watch-chip-header"><strong>{s.status} · {s.mode} · {s.requestedTicketCount} tickets</strong><button type="button" className="ticket-watch-chip-remove" aria-label="Remove training request" title={s.status === 'Running' ? 'Running sessions cannot be removed yet' : 'Remove training request'} disabled={s.status === 'Running' || busyId === `remove-${s.sessionId}`} onClick={() => void removeSession(s.sessionId)}><FontAwesomeIcon icon={faXmark} /></button></div><span>Up to {s.maxPassesPerTicket} passes per message · cascade chain-rule SGD</span>{s.liveProgress && <LiveTrainingProgress progress={s.liveProgress} status={s.status} /> }{(s.chatMonitoringRuns ?? []).map(run => <div key={run.chatMonitoringKind} className="sm-form-actions"><span>{run.chatMonitoringKind} cascade · {run.status}{run.canonicalGeneration !== undefined ? ` · canonical generation ${run.canonicalGeneration}` : ''}</span>{run.hasWorkerReplay && <button type="button" className="btn-secondary" disabled={busyId === `${s.sessionId}-${run.chatMonitoringKind}` || busyId === `${s.sessionId}-both`} onClick={() => void downloadReport(s.sessionId, run.chatMonitoringKind)}>Download {run.chatMonitoringKind} replay</button>}</div>)}{canDownloadBoth && <div className="sm-form-actions"><button type="button" className="btn-primary" disabled={busyId === `${s.sessionId}-both`} onClick={() => void downloadCascadeReports(s.sessionId, replayRuns.map((run) => run.chatMonitoringKind))}>Download Mod + Tutor JSON</button></div>}{s.hasReport && <button type="button" className="btn-secondary" disabled={busyId === `${s.sessionId}-legacy`} onClick={() => void downloadReport(s.sessionId)}>Download legacy report</button>}{s.failureReason && <small>{s.failureReason}</small>}</li>
    })}</ul></section>}
    {view === 'feedback' && <section className="sm-panel"><div className="sm-panel-header"><h3>Training Feedback</h3></div>{feedback.length === 0 ? <p className="dashboard-hint">No reviewer feedback is awaiting approval.</p> : <ul className="ticket-watches-list">{feedback.map(item => <li key={item.scoreEventId} className="ticket-watch-chip"><strong>{item.category} · student {item.studentScore.toFixed(3)} → reviewer {item.reviewerScore.toFixed(3)}</strong><span>{item.messagePreview}</span><small>{item.explanation ?? 'No reviewer explanation supplied.'}</small><div className="sm-form-actions"><button type="button" className="btn-primary" disabled={busyId === item.scoreEventId} onClick={() => void decide(item.scoreEventId, true)}><FontAwesomeIcon icon={faCheck} /> Approve</button><button type="button" className="btn-secondary" disabled={busyId === item.scoreEventId} onClick={() => void decide(item.scoreEventId, false)}><FontAwesomeIcon icon={faXmark} /> Reject</button></div></li>)}</ul>}</section>}
    {view === 'data' && data && <section className="sm-panel"><div className="sm-panel-header"><h3><FontAwesomeIcon icon={faDatabase} /> Data Management</h3></div><p className="dashboard-hint">PostgreSQL is authoritative; the vector store is a retrieval mirror. Category counts include fine moderation concepts and tutoring subject slugs.</p><ul className="ticket-watches-list"><li className="ticket-watch-chip"><strong>{data.trainingExamples}</strong><span>Approved examples</span></li><li className="ticket-watch-chip"><strong>{data.vectorExamples}</strong><span>Vector examples</span></li><li className="ticket-watch-chip"><strong>{data.pendingFeedback}</strong><span>Pending feedback</span></li></ul>{Object.keys(data.categoryCounts ?? {}).length > 0 && <div className="neural-category-cloud" aria-label="Training category distribution">{Object.entries(data.categoryCounts).sort((a, b) => b[1] - a[1]).slice(0, 24).map(([category, count]) => <span key={category} className="neural-category-chip">{category} · {count}</span>)}</div>}</section>}
    {view === 'visualizer' && visualizer && <><section className="sm-panel"><div className="sm-panel-header"><h3><FontAwesomeIcon icon={faFileImport} /> Import a replay file</h3></div><p className="dashboard-hint">Load a downloaded V2 report to replay recorded stage-2 topology and frames. Cascade stage-1 routers relearn online and are not stored in checkpoints.</p><input className="sm-input" type="file" accept="application/json,.json" onChange={importReplay} /></section>{replay?.schemaVersion ? <ReplayViewer replay={replay as NeuralNetReplay} /> : <NetworkGraph visualizer={visualizer} replay={replay} />}</>}
  </div>}</div>
}
