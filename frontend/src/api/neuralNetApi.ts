import axios from 'axios'
import { configureApiClient } from './configureApiClient'
import type {
  NeuralModelKindChatMonitoring,
  NeuralNetDataManagement,
  NeuralNetTrainingFeedback,
  NeuralNetTrainingLiveProgress,
  NeuralNetTrainingSession,
  NeuralNetVisualizer,
  PagedResult,
  StartNeuralNetTrainingRequest,
} from '../types/neuralNet'

const api = axios.create({ baseURL: '/api/neural-net', withCredentials: true })
configureApiClient(api)

export type NeuralNetListParams = {
  beforeUtc?: string | null
  limit?: number
}

export const neuralNetApi = {
  listFeedback: (params?: NeuralNetListParams) =>
    api.get<PagedResult<NeuralNetTrainingFeedback>>('/training-feedback', {
      params: {
        beforeUtc: params?.beforeUtc || undefined,
        limit: params?.limit ?? 50,
      },
    }),
  approve: (scoreEventId: string) => api.post<NeuralNetTrainingFeedback>(`/training-feedback/${scoreEventId}/approve`),
  reject: (scoreEventId: string) => api.post(`/training-feedback/${scoreEventId}/reject`),
  getDataManagement: () => api.get<NeuralNetDataManagement>('/data-management'),
  getVisualizer: () => api.get<NeuralNetVisualizer>('/visualizer'),
  startTraining: (request: StartNeuralNetTrainingRequest) => api.post<NeuralNetTrainingSession>('/training', request),
  listTrainingSessions: (params?: NeuralNetListParams) =>
    api.get<PagedResult<NeuralNetTrainingSession>>('/training', {
      params: {
        beforeUtc: params?.beforeUtc || undefined,
        limit: params?.limit ?? 50,
      },
    }),
  listLiveProgress: () => api.get<NeuralNetTrainingLiveProgress[]>('/training/live'),
  resumeTrainingSession: (sessionId: string) =>
    api.post<NeuralNetTrainingSession>(`/training/${sessionId}/resume`),
  removeTrainingSession: (sessionId: string) => api.delete(`/training/${sessionId}`),
  stopTrainingSession: (sessionId: string) => api.post(`/training/${sessionId}/stop`),
  downloadTrainingReport: (sessionId: string, chatMonitoringKind?: NeuralModelKindChatMonitoring) =>
    api.get(`/training/${sessionId}/report`, {
      params: chatMonitoringKind ? { chatMonitoringKind } : undefined,
      responseType: 'blob',
    }),
}
