import api from './authApi'
import type { ChatInboxItem, ChatInboxSummary } from '../types/inbox'

export const inboxApi = {
  getInbox: () => api.get<ChatInboxItem[]>('/chat/inbox'),

  getSummary: () => api.get<ChatInboxSummary>('/chat/inbox/summary'),

  markRead: (notificationId: string) =>
    api.post(`/chat/inbox/${notificationId}/read`),

  markAllRead: () => api.post('/chat/inbox/read-all'),
}
