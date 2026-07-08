import api from './authApi'
import type { ChatMessage, ChatNav, ChatRoomDetail } from '../types/chat'

export const chatApi = {
  getNav: () => api.get<ChatNav>('/chat/nav'),

  getRoom: (roomId: string) =>
    api.get<ChatRoomDetail>(`/chat/rooms/${encodeURIComponent(roomId)}`),

  getMessages: (roomId: string, params?: { beforeUtc?: string; limit?: number }) =>
    api.get<ChatMessage[]>(`/chat/rooms/${encodeURIComponent(roomId)}/messages`, { params }),

  sendMessage: (roomId: string, content: string, replyToMessageId?: string | null) =>
    api.post<ChatMessage>(`/chat/rooms/${encodeURIComponent(roomId)}/messages`, {
      content,
      replyToMessageId: replyToMessageId ?? undefined,
    }),
}
