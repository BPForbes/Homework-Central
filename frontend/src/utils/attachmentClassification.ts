export type AttachmentDisplayMode = 'hazard' | 'inline' | 'link'

export interface AttachmentClassification {
  mode: AttachmentDisplayMode
  inlineKind?: 'image' | 'pdf' | 'text' | 'video' | 'audio'
}

function resolveInlineKind(
  contentType: string,
  inlinePreviewKind?: string | null,
): AttachmentClassification['inlineKind'] | undefined {
  const serverInlineKind = inlinePreviewKind as AttachmentClassification['inlineKind'] | undefined
  if (serverInlineKind)
    return serverInlineKind

  if (contentType.startsWith('image/'))
    return 'image'
  if (contentType === 'application/pdf')
    return 'pdf'
  if (contentType.startsWith('text/'))
    return 'text'
  if (contentType.startsWith('video/'))
    return 'video'
  if (contentType.startsWith('audio/'))
    return 'audio'

  return undefined
}

export function classifyAttachment(
  contentType: string,
  isHazard: boolean,
  inlinePreviewKind?: string | null,
): AttachmentClassification {
  if (isHazard)
    return { mode: 'hazard' }

  const inlineKind = resolveInlineKind(contentType, inlinePreviewKind)
  if (inlineKind)
    return { mode: 'inline', inlineKind }

  return { mode: 'link' }
}
