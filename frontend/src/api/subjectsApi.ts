import api from './authApi'
import type { ClaimableSubject } from '../types/subjects'

export const subjectsApi = {
  getGeneral: () => api.get<ClaimableSubject[]>('/subjects/general'),
  claim: (subjectName: string) => api.post('/subjects/claim', { subjectName }),
  unclaim: (subjectName: string) => api.post('/subjects/unclaim', { subjectName }),
}
