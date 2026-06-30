import api from './authApi'
import type { ChatNav } from '../types/chat'

export const chatApi = {
  getNav: () => api.get<ChatNav>('/chat/nav'),
}
