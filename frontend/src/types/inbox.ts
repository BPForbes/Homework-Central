import type { TicketIntakeAnswer } from './tickets'

export interface ChatInboxItem {
  notificationId: string
  messageId: string
  senderId: string
  senderUsername: string
  roomId: string
  roomDisplayName: string
  categoryKey: string
  categoryDisplayName: string
  messageContent: string
  mentionKind: string
  ticketId?: string | null
  ticketStatus?: 'Open' | 'Closed' | null
  ticketIntakeAnswers?: TicketIntakeAnswer[] | null
  ticketDecision?: string | null
  ticketDecisionSummary?: string | null
  createdAtUtc: string
  readAtUtc: string | null
  isRead: boolean
}

export interface ChatInboxSummaryItem {
  categoryKey: string
  categoryDisplayName: string
  unreadCount: number
}

export interface ChatInboxSummary {
  categories: ChatInboxSummaryItem[]
}

export interface SendChatMessageError {
  message: string
  code?: string
  retryAfterSeconds?: number
}
