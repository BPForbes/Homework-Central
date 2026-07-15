import api from './authApi'
import type { ChatInboxItem, ChatInboxSummary } from '../types/inbox'

export const inboxApi = {
  getInbox: (categoryKey?: string | null) =>
    api.get<ChatInboxItem[]>('/chat/inbox', {
      params: categoryKey ? { categoryKey } : undefined,
    }),

  getSummary: () => api.get<ChatInboxSummary>('/chat/inbox/summary'),

  markRead: (notificationId: string) =>
    api.post(`/chat/inbox/${notificationId}/read`),

  markAllRead: () => api.post('/chat/inbox/read-all'),

  deleteItems: (notificationIds: string[]) =>
    api.post('/chat/inbox/delete', { notificationIds }),

  deleteAll: (categoryKey?: string | null) =>
    api.delete('/chat/inbox', {
      params: categoryKey ? { categoryKey } : undefined,
    }),
}
