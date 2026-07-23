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
  latestLlm1Summary?: string | null
  latestLlm2Feedback?: string | null
  latestLossSummary?: string | null
  generatorHints: string[]
  weightUpdateFeed: string[]
  pathTone?: string | null
  layerWidths?: number[]
  layerLabels?: string[]
  activeNodeIndexes?: number[]
  activeEdgeParameterIndexes?: number[]
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
  ticketCount: number
  maxPassesPerTicket: number
  mode: NeuralTrainingMode
  continuous?: boolean
}
