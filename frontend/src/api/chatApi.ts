import api from './authApi'
import type { ChatMessage, ChatNav, ChatRoomDetail, MentionRoleOption, MessageVoteUpdate } from '../types/chat'

export const chatApi = {
  getNav: () => api.get<ChatNav>('/chat/nav'),

  getRoom: (roomId: string) =>
    api.get<ChatRoomDetail>(`/chat/rooms/${encodeURIComponent(roomId)}`),

  getMessages: (roomId: string, params?: { beforeUtc?: string; limit?: number }) =>
    api.get<ChatMessage[]>(`/chat/rooms/${encodeURIComponent(roomId)}/messages`, { params }),

  sendMessage: (
    roomId: string,
    content: string,
    replyToMessageId?: string | null,
    extras?: {
      attachmentIds?: string[]
      forwardedFrom?: ChatMessage['forwardedFrom']
    },
  ) =>
    api.post<ChatMessage>(`/chat/rooms/${encodeURIComponent(roomId)}/messages`, {
      content,
      replyToMessageId: replyToMessageId ?? undefined,
      attachmentIds: extras?.attachmentIds,
      forwardedFrom: extras?.forwardedFrom ?? undefined,
    }),

  castVote: (messageId: string, value: 1 | -1) =>
    api.post<MessageVoteUpdate>(`/chat/messages/${messageId}/vote`, { value }),

  uploadAttachment: (file: File) => {
    const form = new FormData()
    form.append('file', file)
    return api.post<{
      attachmentId: string
      fileName: string
      contentType: string
      sizeBytes: number
      downloadUrl: string
      isHazard: boolean
      inlinePreviewKind?: string | null
      scanStatus: 'Clean' | 'Infected' | 'ScanFailed' | 'NotScanned' | 'Unknown'
    }>('/chat/attachments', form, {
      headers: { 'Content-Type': 'multipart/form-data' },
    })
  },

  downloadAttachmentBlob: (attachmentId: string, riskAcknowledged = false) =>
    api.get<Blob>(`/chat/attachments/${attachmentId}`, {
      params: riskAcknowledged ? { riskAcknowledged: true } : undefined,
      responseType: 'blob',
    }),

  deleteAttachment: (attachmentId: string) =>
    api.delete(`/chat/attachments/${attachmentId}`),

  getMentionRoles: () => api.get<MentionRoleOption[]>('/chat/mention-roles'),

  /** Prefix user lookup available to any authenticated user (mentions / ticket intake). */
  searchUsers: (q: string) =>
    api.get<Array<{ userId: string; username: string; email: string }>>('/chat/users/search', {
      params: { q },
    }),
}
