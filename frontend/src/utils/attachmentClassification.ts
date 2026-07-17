export type AttachmentDisplayMode = 'hazard' | 'inline' | 'link'

export interface AttachmentClassification {
  mode: AttachmentDisplayMode
  inlineKind?: 'image' | 'pdf' | 'text' | 'video' | 'audio'
}

export function classifyAttachment(
  contentType: string,
  isHazard: boolean,
  inlinePreviewKind?: string | null,
): AttachmentClassification {
  if (isHazard)
    return { mode: 'hazard' }

  const kind = inlinePreviewKind as AttachmentClassification['inlineKind'] | undefined
  if (kind)
    return { mode: 'inline', inlineKind: kind }

  if (contentType.startsWith('image/'))
    return { mode: 'inline', inlineKind: 'image' }
  if (contentType === 'application/pdf')
    return { mode: 'inline', inlineKind: 'pdf' }
  if (contentType.startsWith('text/'))
    return { mode: 'inline', inlineKind: 'text' }
  if (contentType.startsWith('video/'))
    return { mode: 'inline', inlineKind: 'video' }
  if (contentType.startsWith('audio/'))
    return { mode: 'inline', inlineKind: 'audio' }

  return { mode: 'link' }
}
