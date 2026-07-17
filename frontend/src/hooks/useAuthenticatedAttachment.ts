import { useEffect, useState } from 'react'
import { chatApi } from '../api/chatApi'

interface UseAuthenticatedAttachmentResult {
  blobUrl: string | null
  loading: boolean
  error: string | null
  download: (fileName: string) => void
}

export function useAuthenticatedAttachment(
  attachmentId: string,
  enabled: boolean,
): UseAuthenticatedAttachmentResult {
  const [blobUrl, setBlobUrl] = useState<string | null>(null)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    if (!enabled)
      return

    let revoked = false
    setLoading(true)
    setError(null)

    void (async () => {
      try {
        const { data } = await chatApi.downloadAttachmentBlob(attachmentId)
        if (revoked)
          return
        setBlobUrl(URL.createObjectURL(data))
      } catch {
        if (!revoked)
          setError('Could not load attachment.')
      } finally {
        if (!revoked)
          setLoading(false)
      }
    })()

    return () => {
      revoked = true
      setBlobUrl((prev) => {
        if (prev)
          URL.revokeObjectURL(prev)
        return null
      })
    }
  }, [attachmentId, enabled])

  function download(fileName: string) {
    if (!blobUrl)
      return
    const anchor = document.createElement('a')
    anchor.href = blobUrl
    anchor.download = fileName
    anchor.click()
  }

  return { blobUrl, loading, error, download }
}
