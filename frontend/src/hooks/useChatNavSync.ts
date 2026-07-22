import { useEffect, useRef } from 'react'
import * as signalR from '@microsoft/signalr'
import { getAccessToken, getFreshAccessToken } from '../api/tokenManager'

/** Listens for server-side chat nav changes and invokes the callback (e.g. refetch sidebar). */
export function useChatNavSync(onRefresh: () => void, sessionKey?: string | null) {
  const onRefreshRef = useRef(onRefresh)
  onRefreshRef.current = onRefresh

  useEffect(() => {
    if (!sessionKey || !getAccessToken())
      return

    const connection = new signalR.HubConnectionBuilder()
      .withUrl('/hubs/chat', {
        accessTokenFactory: () => getFreshAccessToken(),
      })
      .withAutomaticReconnect()
      .build()

    connection.on('ChatNavChanged', () => {
      onRefreshRef.current()
    })

    void connection.start().catch(() => undefined)

    return () => {
      void connection.stop()
    }
  }, [sessionKey])
}
