import { ChangeEvent, useEffect, useMemo, useState } from 'react'
import { Link, useLocation } from 'react-router-dom'
import { FontAwesomeIcon } from '@fortawesome/react-fontawesome'
import { faBrain, faCheck, faDatabase, faDiagramProject, faFileImport, faPlay, faStop, faXmark } from '@fortawesome/free-solid-svg-icons'
import { neuralNetApi } from '../api/neuralNetApi'
import { ServerMaintenanceNav } from '../components/layout/ServerMaintenanceNav'
import { LoadingBars } from '../components/LoadingBars'
import { NeuralNetMesh3D, edgeKeysFromDenseParameterIndexes, type MeshPathTone, type NeuralMeshFrame } from '../components/neuralNet/NeuralNetMesh3D'
import { NeuralNetGraph2D } from '../components/neuralNet/NeuralNetGraph2D'
import { ReplayViewer } from '../components/neuralNet/ReplayViewer'
import type { NeuralNetReplay } from '../types/neuralNetReplay'
import { assertDownloadableJsonBlob, triggerBrowserDownload } from '../utils/downloadBlob'
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
  // Training-LLM self-critique / revision stays amber so the pause reads as active, not stalled.
  if (
    lower.includes('llm2')
    || lower.includes('self-critique')
    || lower.includes('self critique')
    || lower.includes('revising from')
    || lower.includes('audit')
    || lower.includes('feedback')
    || lower.includes('considering')
  ) {
    return 'neural-live-tone--reeval'
  }
  if (
    lower.includes('forward')
    || lower.includes('llm1')
    || lower.includes('training llm')
    || lower.includes('generat')
    || lower.includes('accepted')
  ) {
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

function auditFeedLineClass(line: string): string | undefined {
  const upper = line.toUpperCase()
  if (upper.includes('REVISE') || upper.includes('REINTERPRET')) return 'neural-feed-item--reeval'
  if (upper.includes('LGTM')) return 'neural-feed-item--accepted'
  return undefined
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
  const [detail, setDetail] = useState(2)
  const [renderSurface, setRenderSurface] = useState<'3d' | '2d'>('3d')
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
      <p className="neural-path-legend" aria-label="Thought path color legend">
        <span>
          <i className="neural-path-swatch neural-path-swatch--forward" aria-hidden /> Forward
        </span>
        <span>
          <i className="neural-path-swatch neural-path-swatch--reeval" aria-hidden /> REVISE / reinterpret
        </span>
        <span>
          <i className="neural-path-swatch neural-path-swatch--backprop" aria-hidden /> Backprop
        </span>
      </p>
      <div className="sm-form-actions">
        <button type="button" className="btn-secondary" onClick={() => setDetail((value) => Math.max(0, value - 1))}>
          − Detail
        </button>
        <button type="button" className="btn-secondary" onClick={() => setDetail((value) => Math.min(2, value + 1))}>
          + Detail
        </button>
        <div className="neural-mesh3d-view-controls" role="group" aria-label="Render surface">
          <button
            type="button"
            className={renderSurface === '3d' ? 'btn-secondary neural-mesh3d-control--active' : 'btn-secondary'}
            onClick={() => setRenderSurface('3d')}
          >
            3D
          </button>
          <button
            type="button"
            className={renderSurface === '2d' ? 'btn-secondary neural-mesh3d-control--active' : 'btn-secondary'}
            onClick={() => setRenderSurface('2d')}
          >
            2D
          </button>
        </div>
      </div>
      <div className="neural-render-stack" data-surface={renderSurface}>
        <div
          className={`neural-render-stack-layer neural-render-stack-layer--3d${renderSurface === '3d' ? ' is-active' : ''}`}
          aria-hidden={renderSurface !== '3d'}
        >
          <NeuralNetMesh3D
            className="neural-mesh3d--live"
            title={
              typeof progress.activeLayerIndex === 'number'
                ? `Live training · layer ${layerLabels[progress.activeLayerIndex - 1] ?? 'input'} → ${layerLabels[progress.activeLayerIndex] ?? 'output'}`
                : 'Live training · 3D neural mesh'
            }
            layerWidths={layerWidths}
            layerLabels={layerLabels}
            frame={frame}
            detail={detail}
          />
        </div>
        <div
          className={`neural-render-stack-layer neural-render-stack-layer--2d${renderSurface === '2d' ? ' is-active' : ''}`}
          aria-hidden={renderSurface !== '2d'}
        >
          <p className="dashboard-hint">
            2D topology detail {detail} · {layerWidths.join(' → ')} · path tone {frame.pathTone}
            {frame.activeNodeIndexes.length ? ` · ${frame.activeNodeIndexes.length} active nodes` : ''}
          </p>
          <NeuralNetGraph2D
            layerWidths={layerWidths}
            layerLabels={layerLabels}
            detail={detail}
            pathTone={frame.pathTone}
            activeNodeIndexes={progress.activeNodeIndexes ?? []}
            activeEdgeParameterIndexes={progress.activeEdgeParameterIndexes ?? []}
            ariaLabel="Live training neural network topology"
          />
        </div>
      </div>
      <div className="neural-replay-panels neural-replay-panels--live">
        <section className="neural-replay-panel">
          <h4>Training LLM</h4>
          <p className="dashboard-hint">
            {progress.latestTrainingLlmSummary
              ?? progress.latestLlm1Summary
              ?? 'Waiting for scenario generation…'}
          </p>
          <p className="dashboard-hint">
            Processed {progress.ticketsProcessed} tickets · {progress.messagesProcessed} messages ·{' '}
            {progress.examplesPersisted} examples
          </p>
          <h5 className="neural-feed-subhead">Currently evaluating</h5>
          <p className="dashboard-hint neural-feed-pre">
            {progress.currentEvaluationData?.trim()
              ? progress.currentEvaluationData
              : 'No ticket/message under evaluation yet.'}
          </p>
        </section>
        <section className="neural-replay-panel">
          <h4>Audit feedback</h4>
          <p className="dashboard-hint">
            {progress.latestAuditFeedback
              ?? progress.latestLlm2Feedback
              ?? 'No audit notes yet.'}
          </p>
          <p className="dashboard-hint">Audits {progress.auditsCompleted}</p>
          {(progress.auditFeedbackFeed?.length ?? 0) > 0 ? (
            <ul className="neural-feed-list neural-feed-list--scroll">
              {progress.auditFeedbackFeed!.map((line, index) => (
                <li key={`${index}-${line}`} className={auditFeedLineClass(line)}>
                  {line}
                </li>
              ))}
            </ul>
          ) : (progress.generatorHints?.length ?? 0) > 0 ? (
            <ul className="neural-feed-list">
              {progress.generatorHints.slice(-8).map((hint) => (
                <li key={hint}>{hint}</li>
              ))}
            </ul>
          ) : null}
        </section>
        <section className="neural-replay-panel neural-replay-panel--wide">
          <h4>Weight update feed · all nodes</h4>
          {(progress.weightUpdateFeed?.length ?? 0) > 0 ? (
            <ul className="neural-feed-list neural-feed-list--mono neural-feed-list--scroll">
              {progress.weightUpdateFeed.map((line, index) => (
                <li key={`${index}-${line}`}>{line}</li>
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
  const [feedbackHasMore, setFeedbackHasMore] = useState(false)
  const [feedbackNextBeforeUtc, setFeedbackNextBeforeUtc] = useState<string | null>(null)
  const [sessionsHasMore, setSessionsHasMore] = useState(false)
  const [sessionsNextBeforeUtc, setSessionsNextBeforeUtc] = useState<string | null>(null)
  const [loading, setLoading] = useState(true)
  const [busyId, setBusyId] = useState<string | null>(null)
  const [error, setError] = useState('')
  const [ticketCount, setTicketCount] = useState(2)
  const [maxPasses, setMaxPasses] = useState(1)
  const [mode, setMode] = useState<NeuralTrainingMode>('Moderation')
  const [continuous, setContinuous] = useState(false)
  const [replay, setReplay] = useState<ReplayReport | null>(null)
  const [downloadReadyIds, setDownloadReadyIds] = useState<string[]>([])
  const sessionStatusRef = useMemo(() => new Map<string, string>(), [])
  useEffect(() => {
    let cancelled = false
    setLoading(true)
    setError('')
    const load = async () => {
      try {
        if (view === 'feedback') {
          const r = await neuralNetApi.listFeedback()
          if (!cancelled) {
            setFeedback(r.data.items)
            setFeedbackHasMore(r.data.hasMore)
            setFeedbackNextBeforeUtc(r.data.nextBeforeUtc)
          }
        } else if (view === 'training') {
          const r = await neuralNetApi.listTrainingSessions()
          if (!cancelled) {
            setSessions(r.data.items)
            setSessionsHasMore(r.data.hasMore)
            setSessionsNextBeforeUtc(r.data.nextBeforeUtc)
          }
        } else if (view === 'data') {
          const r = await neuralNetApi.getDataManagement()
          if (!cancelled) setData(r.data)
        } else {
          const r = await neuralNetApi.getVisualizer()
          if (!cancelled) setVisualizer(r.data)
        }
      } catch {
        if (!cancelled) setError('Could not load neural-network administration data.')
      } finally {
        if (!cancelled) setLoading(false)
      }
    }
    void load()
    return () => { cancelled = true }
  }, [view])

  const hasActiveTraining = sessions.some((session) => session.status === 'Running' || session.status === 'Queued')
  const hasReevalTone = sessions.some((session) => session.liveProgress?.pathTone === 'reeval')
  useEffect(() => {
    if (view !== 'training' || !hasActiveTraining) return
    // Poll faster during REVISE / reinterpret so yellow mesh + audit lines are visible.
    const intervalMs = hasReevalTone ? 750 : 2000
    const timer = window.setInterval(() => {
      void neuralNetApi.listTrainingSessions()
        .then((response) => {
          setSessions(response.data.items)
          setSessionsHasMore(response.data.hasMore)
          setSessionsNextBeforeUtc(response.data.nextBeforeUtc)
        })
        .catch(() => undefined)
    }, intervalMs)
    return () => window.clearInterval(timer)
  }, [view, hasActiveTraining, hasReevalTone])

  useEffect(() => {
    // Auto browser downloads are often blocked; surface an explicit download panel instead.
    const ready: string[] = []
    for (const session of sessions) {
      const previous = sessionStatusRef.get(session.sessionId)
      sessionStatusRef.set(session.sessionId, session.status)
      if (previous === undefined) continue
      if (!(previous === 'Running' || previous === 'Queued')) continue
      if (session.status !== 'Completed' && session.status !== 'Cancelled') continue
      const hasReplay =
        session.hasReport
        || (session.chatMonitoringRuns ?? []).some((run) => run.hasWorkerReplay)
      if (hasReplay) ready.push(session.sessionId)
    }
    if (ready.length === 0) return
    setDownloadReadyIds((current) => Array.from(new Set([...ready, ...current])))
  }, [sessions, sessionStatusRef])

  async function decide(id: string, approve: boolean) { setBusyId(id); try { if (approve) await neuralNetApi.approve(id); else await neuralNetApi.reject(id); setFeedback(items => items.filter(item => item.scoreEventId !== id)) } catch { setError('The feedback decision could not be saved.') } finally { setBusyId(null) } }
  async function startTraining() {
    setBusyId('training')
    try {
      await neuralNetApi.startTraining({
        // ticketCount 0 reinforces continuous even if the boolean is dropped in transit.
        ticketCount: continuous ? 0 : ticketCount,
        maxPassesPerTicket: continuous ? 1 : maxPasses,
        mode,
        continuous,
      })
      const r = await neuralNetApi.listTrainingSessions()
      setSessions(r.data.items)
      setSessionsHasMore(r.data.hasMore)
      setSessionsNextBeforeUtc(r.data.nextBeforeUtc)
    } catch {
      setError('Training could not be queued.')
    } finally {
      setBusyId(null)
    }
  }
  async function stopSession(sessionId: string) {
    const busyKey = `stop-${sessionId}`
    setBusyId(busyKey)
    try {
      await neuralNetApi.stopTrainingSession(sessionId)
      const r = await neuralNetApi.listTrainingSessions()
      setSessions(r.data.items)
      setSessionsHasMore(r.data.hasMore)
      setSessionsNextBeforeUtc(r.data.nextBeforeUtc)
    } catch {
      setError('That training session could not be stopped.')
    } finally {
      setBusyId(null)
    }
  }
  async function loadMoreSessions() {
    if (!sessionsNextBeforeUtc || !sessionsHasMore) return
    setBusyId('load-more-sessions')
    try {
      const r = await neuralNetApi.listTrainingSessions({ beforeUtc: sessionsNextBeforeUtc })
      setSessions((current) => {
        const seen = new Set(current.map((session) => session.sessionId))
        const appended = r.data.items.filter((session) => !seen.has(session.sessionId))
        return [...current, ...appended]
      })
      setSessionsHasMore(r.data.hasMore)
      setSessionsNextBeforeUtc(r.data.nextBeforeUtc)
    } catch {
      setError('Could not load more training sessions.')
    } finally {
      setBusyId(null)
    }
  }
  async function loadMoreFeedback() {
    if (!feedbackNextBeforeUtc || !feedbackHasMore) return
    setBusyId('load-more-feedback')
    try {
      const r = await neuralNetApi.listFeedback({ beforeUtc: feedbackNextBeforeUtc })
      setFeedback((current) => {
        const seen = new Set(current.map((item) => item.scoreEventId))
        const appended = r.data.items.filter((item) => !seen.has(item.scoreEventId))
        return [...current, ...appended]
      })
      setFeedbackHasMore(r.data.hasMore)
      setFeedbackNextBeforeUtc(r.data.nextBeforeUtc)
    } catch {
      setError('Could not load more feedback.')
    } finally {
      setBusyId(null)
    }
  }
  async function removeSession(sessionId: string) {
    const busyKey = `remove-${sessionId}`
    setBusyId(busyKey)
    try {
      await neuralNetApi.removeTrainingSession(sessionId)
      setSessions((items) => items.filter((item) => item.sessionId !== sessionId))
      setDownloadReadyIds((ids) => ids.filter((id) => id !== sessionId))
    } catch {
      setError('That training request could not be removed. Cancel a running session first.')
    } finally {
      setBusyId(null)
    }
  }
  async function downloadReport(sessionId: string, chatMonitoringKind?: NeuralModelKindChatMonitoring) {
    const downloadId = `${sessionId}-${chatMonitoringKind ?? 'legacy'}`
    setBusyId(downloadId)
    try {
      const response = await neuralNetApi.downloadTrainingReport(sessionId, chatMonitoringKind)
      const blob = await assertDownloadableJsonBlob(response.data)
      const kindSuffix = chatMonitoringKind === 'Moderation'
        ? '-moderation'
        : chatMonitoringKind === 'Tutoring'
          ? '-tutoring'
          : ''
      triggerBrowserDownload(blob, `neural-net-training-${sessionId}${kindSuffix}.json`)
      setDownloadReadyIds((ids) => ids.filter((id) => id !== sessionId))
    } catch (downloadError) {
      setError(downloadError instanceof Error ? downloadError.message : 'The training report could not be downloaded.')
    } finally {
      setBusyId(null)
    }
  }

  async function downloadCascadeReports(sessionId: string, kinds: NeuralModelKindChatMonitoring[]) {
    setBusyId(`${sessionId}-both`)
    try {
      for (const kind of kinds) {
        const response = await neuralNetApi.downloadTrainingReport(sessionId, kind)
        const blob = await assertDownloadableJsonBlob(response.data)
        triggerBrowserDownload(blob, `neural-net-training-${sessionId}-${kind.toLowerCase()}.json`)
        await new Promise((resolve) => window.setTimeout(resolve, 350))
      }
      setDownloadReadyIds((ids) => ids.filter((id) => id !== sessionId))
    } catch (downloadError) {
      setError(
        downloadError instanceof Error
          ? downloadError.message
          : 'The Moderation and Tutoring replay files could not both be downloaded.',
      )
    } finally {
      setBusyId(null)
    }
  }

  function importReplay(event: ChangeEvent<HTMLInputElement>) { const file = event.target.files?.[0]; if (!file) return; const reader = new FileReader(); reader.onload = () => { try { const parsed = parseReplayImport(String(reader.result)); setReplay(parsed); setError('') } catch { setError('That file is not a valid supported V2 neural-network replay.') } }; reader.readAsText(file) }
  const nav = useMemo(() => <div className="server-page-card"><p><Link to="/server/NeuralNet/Training">Training</Link>{' | '}<Link to="/server/NeuralNet/TrainingFeedback">Training Feedback</Link>{' | '}<Link to="/server/NeuralNet/DataManagement">Data Management</Link>{' | '}<Link to="/server/NeuralNet/Visualizer">Visualizer & Replay</Link></p></div>, [])
  return <div className="server-page sm-page"><ServerMaintenanceNav title="Server · Neural Network" /><header className="sm-hero"><div className="sm-hero-icon"><FontAwesomeIcon icon={faBrain} /></div><div className="sm-hero-copy"><h2>Neural Network</h2><p className="server-page-subtitle">Cascade monitors g(f(x)) for moderation and tutoring — chain-rule training, low-memory CPU scoring, review, and replay.</p></div></header>{nav}{error && <p className="error">{error}</p>}{loading ? <LoadingBars message="Loading neural-network data…" /> : <div className="sm-layout sm-layout--single">
    {view === 'training' && <section className="sm-panel"><div className="sm-panel-header"><h3><FontAwesomeIcon icon={faPlay} /> Synthetic cascade training</h3></div><p className="dashboard-hint">One training LLM builds fictional ticket threads and embeds self-critique in the same call; REVISE reworks the next prompt. Continuous mode trains one ticket and one message at a time until you stop it. Only Stop ends a session — self-critique and generator failures never terminate training. Browser auto-downloads are blocked often — use the Download buttons when a session finishes.</p><div className="sm-form"><label className="sm-label">Training mode <select className="sm-input" value={mode} onChange={e => setMode(e.target.value as NeuralTrainingMode)}><option value="Both">Both cascades</option><option value="Moderation">Moderation cascade</option><option value="Tutoring">Tutoring cascade</option></select></label><label className="sm-label"><input type="checkbox" checked={continuous} onChange={(e) => setContinuous(e.target.checked)} /> Continuous (train until cancelled · 1 ticket / 1 message)</label>{!continuous && <><label className="sm-label">Tickets <input className="sm-input" type="number" min="1" max="10" value={ticketCount} onChange={e => setTicketCount(Number(e.target.value))} /></label><label className="sm-label">Maximum passes per message <input className="sm-input" type="number" min="1" max="6" value={maxPasses} onChange={e => setMaxPasses(Number(e.target.value))} /></label></>}<div className="sm-form-actions"><button type="button" className="btn-primary" disabled={busyId === 'training'} onClick={() => void startTraining()}><FontAwesomeIcon icon={faPlay} /> {continuous ? 'Start continuous training' : 'Start training'}</button></div></div><ul className="ticket-watches-list">{sessions.map(s => {
      const replayRuns = (s.chatMonitoringRuns ?? []).filter((run) => run.hasWorkerReplay)
      const canDownloadBoth = s.mode === 'Both' && replayRuns.length >= 2
      const canStop = s.status === 'Running' || s.status === 'Queued'
      const showDownloadPanel = downloadReadyIds.includes(s.sessionId) || s.status === 'Completed' || s.status === 'Cancelled'
      const ticketLabel = s.continuous || s.requestedTicketCount === 0
        ? 'continuous'
        : `${s.requestedTicketCount} tickets`
      return <li key={s.sessionId} className="ticket-watch-chip"><div className="ticket-watch-chip-header"><strong>{s.status} · {s.mode} · {ticketLabel}</strong><div className="sm-form-actions">{canStop && <button type="button" className="btn-secondary" aria-label="Stop training session" title="Stop this training session" disabled={busyId === `stop-${s.sessionId}`} onClick={() => void stopSession(s.sessionId)}><FontAwesomeIcon icon={faStop} /> Stop</button>}<button type="button" className="ticket-watch-chip-remove" aria-label="Remove training request" title={s.status === 'Running' ? 'Stop the running session before removing it' : 'Remove training request'} disabled={s.status === 'Running' || busyId === `remove-${s.sessionId}`} onClick={() => void removeSession(s.sessionId)}><FontAwesomeIcon icon={faXmark} /></button></div></div><span>{s.continuous || s.requestedTicketCount === 0 ? 'Continuous · 1 message per ticket until cancelled' : `Up to ${s.maxPassesPerTicket} passes per message`} · cascade chain-rule SGD</span>{s.liveProgress && <LiveTrainingProgress progress={s.liveProgress} status={s.status} /> }{(s.chatMonitoringRuns ?? []).map(run => <div key={run.chatMonitoringKind} className="sm-form-actions"><span>{run.chatMonitoringKind} cascade · {run.status}{run.canonicalGeneration !== undefined ? ` · canonical generation ${run.canonicalGeneration}` : ''}</span>{run.hasWorkerReplay && <button type="button" className="btn-secondary" disabled={busyId === `${s.sessionId}-${run.chatMonitoringKind}` || busyId === `${s.sessionId}-both`} onClick={() => void downloadReport(s.sessionId, run.chatMonitoringKind)}>Download {run.chatMonitoringKind} replay</button>}</div>)}{showDownloadPanel && (replayRuns.length > 0 || s.hasReport) && <div className="neural-download-ready" role="status"><strong>Downloads ready</strong><div className="sm-form-actions">{replayRuns.map((run) => <button key={run.chatMonitoringKind} type="button" className="btn-primary" disabled={busyId === `${s.sessionId}-${run.chatMonitoringKind}` || busyId === `${s.sessionId}-both`} onClick={() => void downloadReport(s.sessionId, run.chatMonitoringKind)}>Download {run.chatMonitoringKind} JSON</button>)}{canDownloadBoth && <button type="button" className="btn-primary" disabled={busyId === `${s.sessionId}-both`} onClick={() => void downloadCascadeReports(s.sessionId, replayRuns.map((run) => run.chatMonitoringKind))}>Download Mod + Tutor JSON</button>}{s.hasReport && <button type="button" className="btn-secondary" disabled={busyId === `${s.sessionId}-legacy`} onClick={() => void downloadReport(s.sessionId)}>Download legacy report</button>}</div></div>}{s.failureReason && <small>{s.failureReason}</small>}</li>
    })}</ul>{sessionsHasMore && <div className="sm-form-actions"><button type="button" className="btn-secondary" disabled={busyId === 'load-more-sessions'} onClick={() => void loadMoreSessions()}>Load older sessions</button></div>}</section>}
    {view === 'feedback' && <section className="sm-panel"><div className="sm-panel-header"><h3>Training Feedback</h3></div>{feedback.length === 0 ? <p className="dashboard-hint">No reviewer feedback is awaiting approval.</p> : <ul className="ticket-watches-list">{feedback.map(item => <li key={item.scoreEventId} className="ticket-watch-chip"><strong>{item.category} · student {item.studentScore.toFixed(3)} → reviewer {item.reviewerScore.toFixed(3)}</strong><span>{item.messagePreview}</span><small>{item.explanation ?? 'No reviewer explanation supplied.'}</small><div className="sm-form-actions"><button type="button" className="btn-primary" disabled={busyId === item.scoreEventId} onClick={() => void decide(item.scoreEventId, true)}><FontAwesomeIcon icon={faCheck} /> Approve</button><button type="button" className="btn-secondary" disabled={busyId === item.scoreEventId} onClick={() => void decide(item.scoreEventId, false)}><FontAwesomeIcon icon={faXmark} /> Reject</button></div></li>)}</ul>}{feedbackHasMore && <div className="sm-form-actions"><button type="button" className="btn-secondary" disabled={busyId === 'load-more-feedback'} onClick={() => void loadMoreFeedback()}>Load older feedback</button></div>}</section>}
    {view === 'data' && data && <section className="sm-panel"><div className="sm-panel-header"><h3><FontAwesomeIcon icon={faDatabase} /> Data Management</h3></div><p className="dashboard-hint">PostgreSQL is authoritative; the vector store is a retrieval mirror. Category counts include fine moderation concepts and tutoring subject slugs.</p><ul className="ticket-watches-list"><li className="ticket-watch-chip"><strong>{data.trainingExamples}</strong><span>Approved examples</span></li><li className="ticket-watch-chip"><strong>{data.vectorExamples}</strong><span>Vector examples</span></li><li className="ticket-watch-chip"><strong>{data.pendingFeedback}</strong><span>Pending feedback</span></li></ul>{Object.keys(data.categoryCounts ?? {}).length > 0 && <div className="neural-category-cloud" aria-label="Training category distribution">{Object.entries(data.categoryCounts).sort((a, b) => b[1] - a[1]).slice(0, 24).map(([category, count]) => <span key={category} className="neural-category-chip">{category} · {count}</span>)}</div>}</section>}
    {view === 'visualizer' && visualizer && <><section className="sm-panel"><div className="sm-panel-header"><h3><FontAwesomeIcon icon={faFileImport} /> Import a replay file</h3></div><p className="dashboard-hint">Load a downloaded V2 report to replay recorded stage-2 topology and frames. Cascade stage-1 routers relearn online and are not stored in checkpoints.</p><input className="sm-input" type="file" accept="application/json,.json" onChange={importReplay} /></section>{replay?.schemaVersion ? <ReplayViewer replay={replay as NeuralNetReplay} /> : <NetworkGraph visualizer={visualizer} replay={replay} />}</>}
  </div>}</div>
}
