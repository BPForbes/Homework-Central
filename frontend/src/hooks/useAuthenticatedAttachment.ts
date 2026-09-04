import { useEffect, useRef, useState } from 'react'
import { chatApi } from '../api/chatApi'

interface UseAuthenticatedAttachmentResult {
  blobUrl: string | null
  loading: boolean
  error: string | null
  download: (fileName: string, acknowledgeRisk?: boolean) => Promise<void>
}

function triggerBrowserDownload(objectUrl: string, fileName: string) {
  const anchor = document.createElement('a')
  anchor.href = objectUrl
  anchor.download = fileName
  anchor.click()
}

export function useAuthenticatedAttachment(
  attachmentId: string,
  enabled: boolean,
  riskAcknowledged = false,
): UseAuthenticatedAttachmentResult {
  const [blobUrl, setBlobUrl] = useState<string | null>(null)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const blobUrlRef = useRef<string | null>(null)
  const riskAcknowledgedRef = useRef(riskAcknowledged)

  useEffect(() => {
    blobUrlRef.current = blobUrl
  }, [blobUrl])

  useEffect(() => {
    riskAcknowledgedRef.current = riskAcknowledged
  }, [riskAcknowledged])

  useEffect(() => {
    if (!enabled)
      return

    let revoked = false
    setLoading(true)
    setError(null)

    void (async () => {
      try {
        const { data } = await chatApi.downloadAttachmentBlob(attachmentId, riskAcknowledged)
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
  }, [attachmentId, enabled, riskAcknowledged])

  async function download(fileName: string, acknowledgeRisk?: boolean) {
    const allowRisky = acknowledgeRisk ?? riskAcknowledgedRef.current
    if (blobUrlRef.current) {
      triggerBrowserDownload(blobUrlRef.current, fileName)
      return
    }

    const { data } = await chatApi.downloadAttachmentBlob(attachmentId, allowRisky)
    const url = URL.createObjectURL(data)
    try {
      triggerBrowserDownload(url, fileName)
    } finally {
      window.setTimeout(() => URL.revokeObjectURL(url), 60_000)
    }
  }

  return { blobUrl, loading, error, download }
}
