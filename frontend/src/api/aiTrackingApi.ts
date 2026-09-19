import axios from 'axios'
import { configureApiClient } from './configureApiClient'
import type {
  AICategory,
  AIModelLineage,
  AITrackingSession,
  AITrackingSessionPage,
  RegisterAIModelLineageRequest,
} from '../types/aiTracking'

const api = axios.create({ baseURL: '/api/ai-tracking', withCredentials: true })
configureApiClient(api)

export const aiTrackingApi = {
  listLineages: () => api.get<AIModelLineage[]>('/lineages'),
  listCategories: (lineageSlug: string) =>
    api.get<AICategory[]>(`/lineages/${encodeURIComponent(lineageSlug)}/categories`),
  registerLineage: (request: RegisterAIModelLineageRequest) =>
    api.post<AIModelLineage>('/lineages', request),
  deleteLineage: (lineageSlug: string) =>
    api.delete(`/lineages/${encodeURIComponent(lineageSlug)}`),
  querySessions: (params?: {
    lineageSlug?: string
    ticketId?: string
    createdByUserId?: string
    beforeUtc?: string
    limit?: number
  }) => api.get<AITrackingSessionPage>('/sessions', { params }),
  getSession: (sessionId: number) => api.get<AITrackingSession>(`/sessions/${sessionId}`),
  deleteSession: (sessionId: number) => api.delete(`/sessions/${sessionId}`),
  deleteTicketSessions: (ticketId: string) =>
    api.delete<{ deletedSessionCount: number }>(`/tickets/${ticketId}/sessions`),
  deleteLineageSessions: (lineageSlug: string) =>
    api.delete<{ deletedSessionCount: number }>(`/lineages/${encodeURIComponent(lineageSlug)}/sessions`),
}
