/** Trigger a browser file save from a Blob. Appends the anchor so Chromium/Safari honor download. */

export async function assertDownloadableJsonBlob(blob: Blob): Promise<Blob> {
  if (!(blob instanceof Blob) || blob.size === 0) {
    throw new Error('Empty download payload.')
  }

  const text = await blob.text()
  const trimmed = text.trimStart()
  if (!trimmed) {
    throw new Error('Empty download payload.')
  }

  if (trimmed.startsWith('{')) {
    let parsed: {
      message?: string
      error?: string
      schemaVersion?: unknown
      topology?: unknown
    }
    try {
      parsed = JSON.parse(text) as typeof parsed
    } catch {
      return new Blob([text], { type: 'application/json' })
    }

    const looksLikeApiError =
      (typeof parsed.message === 'string' || typeof parsed.error === 'string')
      && parsed.schemaVersion === undefined
      && parsed.topology === undefined
    if (looksLikeApiError) {
      throw new Error(parsed.message || parsed.error || 'Download failed.')
    }
  }

  return new Blob([text], { type: 'application/json' })
}

export function triggerBrowserDownload(blob: Blob, fileName: string): void {
  const objectUrl = URL.createObjectURL(blob)
  const anchor = document.createElement('a')
  anchor.href = objectUrl
  anchor.download = fileName
  anchor.rel = 'noopener'
  anchor.style.display = 'none'
  document.body.appendChild(anchor)
  anchor.click()
  anchor.remove()
  // Delay revoke so the browser can finish reading the object URL.
  window.setTimeout(() => URL.revokeObjectURL(objectUrl), 60_000)
}
