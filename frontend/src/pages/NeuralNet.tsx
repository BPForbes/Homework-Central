import { useEffect, useState } from 'react'
import { Link, useLocation } from 'react-router-dom'
import { FontAwesomeIcon } from '@fortawesome/react-fontawesome'
import { faBrain, faCheck, faDatabase, faDiagramProject, faXmark } from '@fortawesome/free-solid-svg-icons'
import { neuralNetApi } from '../api/neuralNetApi'
import { ServerMaintenanceNav } from '../components/layout/ServerMaintenanceNav'
import { LoadingBars } from '../components/LoadingBars'
import type { NeuralNetDataManagement, NeuralNetTrainingFeedback, NeuralNetVisualizer } from '../types/neuralNet'

type NeuralView = 'feedback' | 'data' | 'visualizer'

function viewForPath(pathname: string): NeuralView {
  if (pathname.endsWith('/DataManagement')) return 'data'
  if (pathname.endsWith('/Visualizer')) return 'visualizer'
  return 'feedback'
}

export function NeuralNet() {
  const { pathname } = useLocation()
  const view = viewForPath(pathname)
  const [feedback, setFeedback] = useState<NeuralNetTrainingFeedback[]>([])
  const [data, setData] = useState<NeuralNetDataManagement | null>(null)
  const [visualizer, setVisualizer] = useState<NeuralNetVisualizer | null>(null)
  const [loading, setLoading] = useState(true)
  const [busyId, setBusyId] = useState<string | null>(null)
  const [error, setError] = useState('')

  useEffect(() => {
    let cancelled = false
    setLoading(true)
    setError('')
    const load = async () => {
      try {
        if (view === 'feedback') {
          const { data: result } = await neuralNetApi.listFeedback()
          if (!cancelled) setFeedback(result)
        } else if (view === 'data') {
          const { data: result } = await neuralNetApi.getDataManagement()
          if (!cancelled) setData(result)
        } else {
          const { data: result } = await neuralNetApi.getVisualizer()
          if (!cancelled) setVisualizer(result)
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

  async function decide(scoreEventId: string, approve: boolean) {
    setBusyId(scoreEventId)
    setError('')
    try {
      if (approve) await neuralNetApi.approve(scoreEventId)
      else await neuralNetApi.reject(scoreEventId)
      setFeedback((items) => items.filter((item) => item.scoreEventId !== scoreEventId))
    } catch {
      setError('The feedback decision could not be saved.')
    } finally {
      setBusyId(null)
    }
  }

  return (
    <div className="server-page sm-page">
      <ServerMaintenanceNav title="Neural Network" />
      <header className="sm-hero">
        <div className="sm-hero-icon"><FontAwesomeIcon icon={faBrain} /></div>
        <div className="sm-hero-copy">
          <h2>Neural Network</h2>
          <p className="server-page-subtitle">Review tutor feedback, inspect training data, and view the bounded student model.</p>
        </div>
      </header>

      <div className="server-page-card">
        <p>
          <Link to="/server/NeuralNet/TrainingFeedback">Training Feedback</Link>{' | '}
          <Link to="/server/NeuralNet/DataManagement">Data Management</Link>{' | '}
          <Link to="/server/NeuralNet/Visualizer">Visualizer</Link>
        </p>
      </div>

      {error && <p className="error">{error}</p>}
      {loading ? <LoadingBars message="Loading neural-network data…" /> : (
        <div className="sm-layout sm-layout--single">
          {view === 'feedback' && (
            <section className="sm-panel">
              <div className="sm-panel-header"><h3>Training Feedback</h3></div>
              {feedback.length === 0 ? <p className="dashboard-hint">No reviewer feedback is awaiting approval.</p> : (
                <ul className="ticket-watches-list">
                  {feedback.map((item) => (
                    <li key={item.scoreEventId} className="ticket-watch-chip">
                      <strong>{item.category} · student {item.studentScore.toFixed(3)} → reviewer {item.reviewerScore.toFixed(3)}</strong>
                      <span>{item.messagePreview}</span>
                      <small>{item.explanation ?? 'No reviewer explanation supplied.'}</small>
                      {item.guidance && <small>Tutor: {item.guidance}</small>}
                      <div className="sm-form-actions">
                        <button type="button" className="btn-primary" disabled={busyId === item.scoreEventId} onClick={() => void decide(item.scoreEventId, true)}>
                          <FontAwesomeIcon icon={faCheck} /> Approve
                        </button>
                        <button type="button" className="btn-secondary" disabled={busyId === item.scoreEventId} onClick={() => void decide(item.scoreEventId, false)}>
                          <FontAwesomeIcon icon={faXmark} /> Reject
                        </button>
                      </div>
                    </li>
                  ))}
                </ul>
              )}
            </section>
          )}
          {view === 'data' && data && (
            <section className="sm-panel">
              <div className="sm-panel-header"><h3><FontAwesomeIcon icon={faDatabase} /> Data Management</h3></div>
              <p className="dashboard-hint">PostgreSQL is authoritative; the vector store is a retrieval mirror.</p>
              <ul className="ticket-watches-list">
                <li className="ticket-watch-chip"><strong>{data.trainingExamples}</strong><span>Approved training examples</span></li>
                <li className="ticket-watch-chip"><strong>{data.vectorExamples}</strong><span>Vector retrieval examples</span></li>
                <li className="ticket-watch-chip"><strong>{data.pendingFeedback}</strong><span>Pending feedback</span></li>
                <li className="ticket-watch-chip"><strong>{data.approvedFeedback} approved · {data.rejectedFeedback} rejected</strong><span>Staff feedback decisions</span></li>
                {Object.entries(data.categoryCounts).map(([category, count]) => <li key={category} className="ticket-watch-chip"><strong>{count}</strong><span>{category}</span></li>)}
              </ul>
            </section>
          )}
          {view === 'visualizer' && visualizer && (
            <section className="sm-panel">
              <div className="sm-panel-header"><h3><FontAwesomeIcon icon={faDiagramProject} /> Visualizer</h3></div>
              <p className="dashboard-hint">{visualizer.modelVersion} is fixed-size so memory and CPU requirements stay predictable.</p>
              <ul className="ticket-watches-list">
                <li className="ticket-watch-chip"><strong>{visualizer.inputNodes}</strong><span>Hashed input nodes</span></li>
                <li className="ticket-watch-chip"><strong>{visualizer.hiddenNodes}</strong><span>Hidden neurons</span></li>
                <li className="ticket-watch-chip"><strong>{visualizer.outputNodes.join(' · ')}</strong><span>Bounded outputs</span></li>
                <li className="ticket-watch-chip"><strong>{visualizer.trainingExamples}</strong><span>Approved examples loaded at startup</span></li>
              </ul>
            </section>
          )}
        </div>
      )}
    </div>
  )
}
