import type { CustomChannelAccessRule, CustomChannelAccessRuleInput } from './infrastructure'

export type TicketStatus = 'Open' | 'Closed'
export type TicketTrackingMode = 'Opener' | 'FromIntakeField' | 'None'
export type TicketWatchSource = 'Intake' | 'Staff' | 'Model'

export type TicketIntakeQuestionType =
  | 'shortText'
  | 'longText'
  | 'multipleChoice'
  | 'trueFalse'
  | 'checkbox'
  | 'date'
  | 'multiSelect'
  | 'dropdown'
  | 'fileUpload'
  | 'link'
  | 'messageForward'
  | 'mixed'

export type IntakeResponseKind = 'text' | 'file' | 'link' | 'forward'

export interface TicketIntakeQuestion {
  id: string
  type: TicketIntakeQuestionType
  prompt: string
  required: boolean
  tracksUser: boolean
  aiOptOut?: boolean
  allowedResponseKinds?: IntakeResponseKind[]
  options?: string[]
}

export interface TicketPortalConfig {
  channelId: string
  roomId: string
  ctaLabel: string
  description: string
  purpose: string
  filterName: string
  nextDisplayNumber: number
  trackingMode: TicketTrackingMode
  trackingInstructions: string | null
  decisionLabels: string[]
  mentionRoleRules: CustomChannelAccessRule[]
  staffAccessRules: CustomChannelAccessRule[]
  intakeQuestions: TicketIntakeQuestion[]
}

export interface UpdateTicketPortalConfigRequest {
  ctaLabel: string
  description: string
  purpose: string
  filterName: string
  trackingMode: TicketTrackingMode
  trackingInstructions: string | null
  decisionLabels: string[]
  mentionRoleRules: CustomChannelAccessRuleInput[]
  staffAccessRules: CustomChannelAccessRuleInput[]
  intakeQuestions: TicketIntakeQuestion[]
}

export interface TicketIntakeAnswer {
  questionId: string
  prompt: string
  type: TicketIntakeQuestionType
  valueDisplay: string
}

export interface TicketUserWatch {
  watchId: string
  trackedUserId: string
  trackedUsername: string
  contextLabel: string
  isActive: boolean
  source: TicketWatchSource
  updatedAtUtc: string
}

export interface Ticket {
  ticketId: string
  portalChannelId: string
  portalRoomId: string
  chatChannelId: string
  roomId: string
  displayName: string
  purpose: string
  filterName: string
  displayNumber: number
  status: TicketStatus
  openedByUserId: string
  openedByUsername: string
  canManage: boolean
  aiTrackingOptOut: boolean
  approvedDecision: string | null
  createdAtUtc: string
  closedAtUtc: string | null
  closedByUserId: string | null
  intakeAnswers: TicketIntakeAnswer[]
  watches: TicketUserWatch[]
}

export type TicketAnswers = Record<string, string | string[] | boolean | null | unknown>

export interface UpsertTicketWatchRequest {
  trackedUserId: string
  isActive: boolean
  contextLabel?: string | null
}

export interface TicketAnalyzeResult {
  available: boolean
  decision: string | null
  summary: string | null
  currentScore: number | null
  messageScores: TicketMessageScore[]
  watches: TicketUserWatch[]
  inboxRecipientsNotified: number
}

export interface TicketMessageScore {
  scoreEventId: string
  messageId: string
  trackedUserId: string
  previousScore: number
  scoreDelta: number
  currentScore: number
  evidenceConfidence: number
  relevance: number
  reason: string
  studentScore: number
  studentConfidence: number
  studentRelevance: number
  studentCategory: string
  studentReasoning: string
  reviewerInvoked: boolean
  reviewerScore: number | null
  reviewerConfidence: number | null
  correctionNeeded: boolean
  reviewerExplanation: string | null
  reviewerGuidance: string | null
  trainingApprovedAtUtc: string | null
  createdAtUtc: string
}

export const TICKET_INTAKE_QUESTION_TYPES: { value: TicketIntakeQuestionType; label: string }[] = [
  { value: 'shortText', label: 'Short text' },
  { value: 'longText', label: 'Long text' },
  { value: 'multipleChoice', label: 'Multiple choice' },
  { value: 'trueFalse', label: 'True / false' },
  { value: 'checkbox', label: 'Checkbox' },
  { value: 'date', label: 'Date' },
  { value: 'multiSelect', label: 'Multi-select' },
  { value: 'dropdown', label: 'Dropdown' },
  { value: 'fileUpload', label: 'File upload' },
  { value: 'link', label: 'Link' },
  { value: 'messageForward', label: 'Forwarded message' },
  { value: 'mixed', label: 'Mixed (multi-kind)' },
]

export const TICKET_TRACKING_MODES: { value: TicketTrackingMode; label: string; hint: string }[] = [
  { value: 'None', label: 'None', hint: 'No automated user tracking' },
  { value: 'Opener', label: 'Opener', hint: 'Track the user who opened the ticket' },
  { value: 'FromIntakeField', label: 'From intake', hint: 'Track a user named in an intake answer' },
]
