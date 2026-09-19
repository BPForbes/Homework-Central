import type { PagedResult } from './neuralNet'

export interface AIModelLineage {
  lineageId: number
  slug: string
  displayName: string
  isBuiltIn: boolean
  portalChannelId: string | null
  categoryCount: number
  sessionCount: number
  createdAtUtc: string
}

export interface AICategory {
  categoryId: number
  slug: string
  displayName: string
  sortOrder: number
  isCatchAll: boolean
}

export interface AITrackingCategoryWeight {
  categoryWeightId: number
  categorySlug: string
  weight: number
  isHumanCorrected: boolean
  humanOverrideCategorySlug: string | null
  humanCorrectionAtUtc: string | null
}

export interface AITrackingPrediction {
  predictionId: number
  predictedCategorySlug: string
  predictedScore: number
  actualCategorySlug: string | null
  createdAtUtc: string
}

export interface AITrackingSession {
  sessionId: number
  lineageSlug: string
  ticketId: string | null
  sourceKind: string
  messageIndex: number
  modelVersion: string
  createdByUserId: string | null
  createdAtUtc: string
  categoryWeights: AITrackingCategoryWeight[]
  predictions: AITrackingPrediction[]
}

export type AITrackingSessionPage = PagedResult<AITrackingSession>

export interface RegisterAIModelLineageRequest {
  slug: string
  displayName: string
  portalChannelId?: string | null
  categories: { slug: string; displayName: string; isCatchAll?: boolean }[]
}
