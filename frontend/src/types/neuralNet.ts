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

export interface NeuralNetVisualizer {
  inputNodes: number
  hiddenNodes: number
  outputNodes: string[]
  modelVersion: string
  trainingExamples: number
}

export interface NeuralNetTrainingSession {
  sessionId: string
  requestedTicketCount: number
  maxPassesPerTicket: number
  mode: NeuralTrainingMode
  status: string
  createdAtUtc: string
  startedAtUtc?: string
  completedAtUtc?: string
  failureReason?: string
  hasReport: boolean
}

export type NeuralTrainingMode = 'Both' | 'Moderation' | 'Tutoring'
export interface StartNeuralNetTrainingRequest { ticketCount: number; maxPassesPerTicket: number; mode: NeuralTrainingMode }
