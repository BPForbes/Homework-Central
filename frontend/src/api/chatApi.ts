import api from './authApi'
import type { ChatMessage, ChatNav } from '../types/chat'

export const chatApi = {
  getNav: () => api.get<ChatNav>('/chat/nav'),

  getMessages: (roomId: string, params?: { beforeUtc?: string; limit?: number }) =>
    api.get<ChatMessage[]>(`/chat/rooms/${encodeURIComponent(roomId)}/messages`, { params }),

  sendMessage: (roomId: string, content: string) =>
    api.post<ChatMessage>(`/chat/rooms/${encodeURIComponent(roomId)}/messages`, { content }),
}
