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
