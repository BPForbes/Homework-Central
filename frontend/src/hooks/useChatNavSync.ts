import { useEffect, useRef } from 'react'
import * as signalR from '@microsoft/signalr'

/** Listens for server-side chat nav changes and invokes the callback (e.g. refetch sidebar). */
export function useChatNavSync(onRefresh: () => void) {
  const onRefreshRef = useRef(onRefresh)
  onRefreshRef.current = onRefresh

  useEffect(() => {
    const token = sessionStorage.getItem('accessToken')
    if (!token)
      return

    const connection = new signalR.HubConnectionBuilder()
      .withUrl(`/hubs/chat?access_token=${encodeURIComponent(token)}`)
      .withAutomaticReconnect()
      .build()

    connection.on('ChatNavChanged', () => {
      onRefreshRef.current()
    })

    void connection.start().catch(() => undefined)

    return () => {
      void connection.stop()
    }
  }, [])
}
