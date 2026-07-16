import axios from 'axios'
import { configureApiClient } from './configureApiClient'
import type {
  Ticket,
  TicketAnswers,
  TicketAnalyzeResult,
  TicketPortalConfig,
  UpdateTicketPortalConfigRequest,
  UpsertTicketWatchRequest,
} from '../types/tickets'

const infrastructureApi = axios.create({ baseURL: '/api/infrastructure', withCredentials: true })
const ticketsApiClient = axios.create({ baseURL: '/api/tickets', withCredentials: true })
const channelsApiClient = axios.create({ baseURL: '/api/channels', withCredentials: true })
configureApiClient(infrastructureApi)
configureApiClient(ticketsApiClient)
configureApiClient(channelsApiClient)

export const ticketsApi = {
  getPortalConfig: (channelId: string) =>
    infrastructureApi.get<TicketPortalConfig>(`/channels/${channelId}/ticket-config`),

  updatePortalConfig: (channelId: string, body: UpdateTicketPortalConfigRequest) =>
    infrastructureApi.put<TicketPortalConfig>(`/channels/${channelId}/ticket-config`, body),

  open: (portalRoomId: string, answers: TicketAnswers) =>
    channelsApiClient.post<Ticket>(
      `/by-room/${encodeURIComponent(portalRoomId)}/tickets`,
      { answers },
    ),

  getByRoom: (roomId: string) =>
    ticketsApiClient.get<Ticket>(`/by-room/${encodeURIComponent(roomId)}`),

  close: (ticketId: string) => ticketsApiClient.post<Ticket>(`/${ticketId}/close`),

  reopen: (ticketId: string) => ticketsApiClient.post<Ticket>(`/${ticketId}/reopen`),

  remove: (ticketId: string) => ticketsApiClient.delete(`/${ticketId}`),

  upsertWatch: (ticketId: string, body: UpsertTicketWatchRequest) =>
    ticketsApiClient.post(`/${ticketId}/watches`, body),

  analyze: (ticketId: string) =>
    ticketsApiClient.post<TicketAnalyzeResult>(`/${ticketId}/analyze`),
}
