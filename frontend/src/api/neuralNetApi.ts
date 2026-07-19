import axios from 'axios'
import { configureApiClient } from './configureApiClient'
import type { NeuralNetDataManagement, NeuralNetTrainingFeedback, NeuralNetVisualizer } from '../types/neuralNet'

const api = axios.create({ baseURL: '/api/neural-net', withCredentials: true })
configureApiClient(api)

export const neuralNetApi = {
  listFeedback: () => api.get<NeuralNetTrainingFeedback[]>('/training-feedback'),
  approve: (scoreEventId: string) => api.post<NeuralNetTrainingFeedback>(`/training-feedback/${scoreEventId}/approve`),
  reject: (scoreEventId: string) => api.post(`/training-feedback/${scoreEventId}/reject`),
  getDataManagement: () => api.get<NeuralNetDataManagement>('/data-management'),
  getVisualizer: () => api.get<NeuralNetVisualizer>('/visualizer'),
}
