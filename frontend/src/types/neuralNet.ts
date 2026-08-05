export interface PagedResult<T> {
  items: T[]
  hasMore: boolean
  nextBeforeUtc: string | null
  limit: number
}

export interface NeuralNetTrainingFeedback {
  scoreEventId: string
  ticketId: string
  messageId: string
  messagePreview: string
  category: string
  studentScore: number
  studentConfidence: number
  reviewerScore: number
  reviewerConfidence: number
  correctionNeeded: boolean
  explanation: string | null
  guidance: string | null
  createdAtUtc: string
}

export interface NeuralNetDataManagement {
  pendingFeedback: number
  approvedFeedback: number
  rejectedFeedback: number
  trainingExamples: number
  vectorExamples: number
  categoryCounts: Record<string, number>
}

export interface NeuralNetVisualizerModel {
  chatMonitoringKind: NeuralModelKindChatMonitoring
  modelVersion: string
  layerWidths: number[]
  layerLabels: string[]
  parameterCount: number
  supportExamples: number
  nodeCount: number
  stage1LayerWidths?: number[]
  stage1Role?: string
  categoryCount?: number
  cascadeComposition?: string
  chainRuleSummary?: string
  runtimeKind?: string
}

export interface NeuralNetVisualizer {
  models?: NeuralNetVisualizerModel[]
  outputNodes: string[]
  trainingExamples: number
  inputNodes: number
  hiddenNodes: number
  modelVersion: string
}

export interface NeuralNetTrainingLiveProgress {
  phase: string
  ticketsRequested: number
  ticketsGenerated: number
  ticketsProcessed: number
  messagesProcessed: number
  examplesPersisted: number
  auditsCompleted: number
  activeChatMonitoringKind?: string | null
  /** Latest training-LLM scenario summary (generation / revision). */
  latestTrainingLlmSummary?: string | null
  /** Latest self-critique / audit line from the same training LLM. */
  latestAuditFeedback?: string | null
  /** Compatibility alias of latestTrainingLlmSummary. */
  latestLlm1Summary?: string | null
  /** Compatibility alias of latestAuditFeedback. */
  latestLlm2Feedback?: string | null
  latestLossSummary?: string | null
  generatorHints: string[]
  /** Full audit feed for the current session instance (newest last). */
  auditFeedbackFeed?: string[]
  /** Ticket/message currently under evaluation / training. */
  currentEvaluationData?: string | null
  /** Per-node weight-update lines for the active mini-batch step. */
  weightUpdateFeed: string[]
  pathTone?: string | null
  layerWidths?: number[]
  layerLabels?: string[]
  activeNodeIndexes?: number[]
  activeEdgeParameterIndexes?: number[]
  /** Destination layer of the current one-layer step; absent when the whole net is shown. */
  activeLayerIndex?: number | null
  updatedAtUtc: string
}

export interface NeuralNetTrainingSession {
  sessionId: string
  requestedTicketCount: number
  maxPassesPerTicket: number
  continuous?: boolean
  mode: NeuralTrainingMode
  status: string
  createdAtUtc: string
  startedAtUtc?: string
  completedAtUtc?: string
  failureReason?: string
  hasReport: boolean
  chatMonitoringRuns: ChatMonitoringNeuralModelRun[]
  liveProgress?: NeuralNetTrainingLiveProgress | null
}

export type NeuralTrainingMode = 'Both' | 'Moderation' | 'Tutoring'
export type NeuralModelKindChatMonitoring = 'Moderation' | 'Tutoring'
export interface ChatMonitoringNeuralModelRun {
  chatMonitoringKind: NeuralModelKindChatMonitoring
  status: string
  canonicalGeneration?: number
  hasWorkerReplay: boolean
  hasPromotionReplay: boolean
  failureReason?: string
}
export interface StartNeuralNetTrainingRequest {
  /** Use 0 with continuous=true (train until Stop). Finite runs use 1–10. */
  ticketCount: number
  maxPassesPerTicket: number
  mode: NeuralTrainingMode
  /** When true, trains until Stop; ticketCount is ignored server-side (stored as 0). */
  continuous?: boolean
}
