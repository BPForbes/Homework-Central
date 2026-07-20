import axios from 'axios'
import { configureApiClient } from './configureApiClient'
import type { NeuralNetDataManagement, NeuralNetTrainingFeedback, NeuralNetTrainingSession, NeuralNetVisualizer, StartNeuralNetTrainingRequest } from '../types/neuralNet'

const api = axios.create({ baseURL: '/api/neural-net', withCredentials: true })
configureApiClient(api)

export const neuralNetApi = {
  listFeedback: () => api.get<NeuralNetTrainingFeedback[]>('/training-feedback'),
  approve: (scoreEventId: string) => api.post<NeuralNetTrainingFeedback>(`/training-feedback/${scoreEventId}/approve`),
  reject: (scoreEventId: string) => api.post(`/training-feedback/${scoreEventId}/reject`),
  getDataManagement: () => api.get<NeuralNetDataManagement>('/data-management'),
  getVisualizer: () => api.get<NeuralNetVisualizer>('/visualizer'),
  startTraining: (request: StartNeuralNetTrainingRequest) => api.post<NeuralNetTrainingSession>('/training', request),
  listTrainingSessions: () => api.get<NeuralNetTrainingSession[]>('/training'),
  downloadTrainingReport: (sessionId: string) => api.get(`/training/${sessionId}/report`, { responseType: 'blob' }),
}
