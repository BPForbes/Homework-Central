import { useEffect, useState } from 'react'
import { FontAwesomeIcon } from '@fortawesome/react-fontawesome'
import { faDownload } from '@fortawesome/free-solid-svg-icons'
import { byPrefixAndName } from '../../icons/byPrefixAndName'
import type { ChatAttachmentInfo } from '../../types/chat'
import { useAuthenticatedAttachment } from '../../hooks/useAuthenticatedAttachment'
import { classifyAttachment } from '../../utils/attachmentClassification'
import { highlightCode } from '../../utils/highlightCode'

const TEXT_PREVIEW_MAX_BYTES = 256 * 1024

interface ChatAttachmentViewProps {
  attachment: ChatAttachmentInfo
}

export function ChatAttachmentView({ attachment }: ChatAttachmentViewProps) {
  const classification = classifyAttachment(
    attachment.contentType,
    attachment.isHazard ?? false,
    attachment.inlinePreviewKind,
  )
  const [useBlobFallback, setUseBlobFallback] = useState(false)
  const [expanded, setExpanded] = useState(false)
  const [textPreview, setTextPreview] = useState<string | null>(null)
  const [textTooLarge, setTextTooLarge] = useState(false)
  const [textLoading, setTextLoading] = useState(false)

  const needsBlob =
    useBlobFallback
    || classification.mode === 'hazard'
    || classification.mode === 'link'
    || (classification.mode === 'inline' && classification.inlineKind === 'text')

  const { blobUrl, loading, error, download } = useAuthenticatedAttachment(
    attachment.attachmentId,
    needsBlob,
  )

  const inlineSrc = useBlobFallback ? blobUrl : attachment.downloadUrl
  const showTextInline =
    classification.mode === 'inline'
    && classification.inlineKind === 'text'
    && textPreview !== null

  useEffect(() => {
    if (classification.mode !== 'inline' || classification.inlineKind !== 'text')
      return

    let cancelled = false
    setTextLoading(true)
    setTextTooLarge(false)
    setTextPreview(null)

    void (async () => {
      try {
        const response = await fetch(attachment.downloadUrl)
        if (!response.ok)
          throw new Error('fetch failed')
        const buffer = await response.arrayBuffer()
        if (cancelled)
          return
        if (buffer.byteLength > TEXT_PREVIEW_MAX_BYTES) {
          setTextTooLarge(true)
          return
        }
        setTextPreview(new TextDecoder().decode(buffer))
      } catch {
        if (!cancelled)
          setUseBlobFallback(true)
      } finally {
        if (!cancelled)
          setTextLoading(false)
      }
    })()

    return () => {
      cancelled = true
    }
  }, [attachment.downloadUrl, classification.inlineKind, classification.mode])

  useEffect(() => {
    if (!expanded || attachment.inlinePreviewKind !== 'text' || textPreview !== null)
      return

    let cancelled = false
    setTextLoading(true)

    void (async () => {
      try {
        const source = blobUrl ?? attachment.downloadUrl
        const response = await fetch(source)
        if (!response.ok)
          throw new Error('fetch failed')
        const buffer = await response.arrayBuffer()
        if (cancelled)
          return
        if (buffer.byteLength > TEXT_PREVIEW_MAX_BYTES) {
          setTextTooLarge(true)
          return
        }
        setTextPreview(new TextDecoder().decode(buffer))
      } catch {
        if (!cancelled)
          setTextTooLarge(true)
      } finally {
        if (!cancelled)
          setTextLoading(false)
      }
    })()

    return () => {
      cancelled = true
    }
  }, [attachment.downloadUrl, attachment.inlinePreviewKind, blobUrl, expanded, textPreview])

  if (loading || textLoading)
    return <span className="chat-attachment-loading">{attachment.fileName}</span>

  if (error)
    return <span className="chat-attachment-error">{attachment.fileName}</span>

  if (classification.mode === 'hazard') {
    const canPreviewText = attachment.inlinePreviewKind === 'text'
    const codeHtml = textPreview ? highlightCode(textPreview) : null

    return (
      <div className="chat-attachment-hazard">
        <FontAwesomeIcon icon={byPrefixAndName.fas['file-circle-exclamation']} />
        <button
          type="button"
          className="chat-attachment-download-link"
          onClick={() => download(attachment.fileName)}
        >
          {attachment.fileName}
        </button>
        {canPreviewText && !expanded && (
          <button
            type="button"
            className="chat-attachment-expand-btn"
            onClick={() => setExpanded(true)}
          >
            Preview code
          </button>
        )}
        {expanded && textTooLarge && (
          <p className="chat-attachment-hazard-banner">File too large to preview.</p>
        )}
        {expanded && codeHtml && (
          <div className="chat-attachment-hazard-preview">
            <p className="chat-attachment-hazard-banner">Code file — review before running.</p>
            <pre className="hc-code-block rich-content">
              <code className="hljs" dangerouslySetInnerHTML={{ __html: codeHtml }} />
            </pre>
          </div>
        )}
      </div>
    )
  }

  if (classification.mode === 'inline') {
    if (showTextInline && textTooLarge)
      return <span className="chat-attachment-error">File too large to preview.</span>

    if (showTextInline && textPreview) {
      const codeHtml = highlightCode(textPreview)
      return (
        <div className="chat-attachment-inline">
          <pre className="hc-code-block rich-content chat-attachment-text-block">
            <code className="hljs" dangerouslySetInnerHTML={{ __html: codeHtml }} />
          </pre>
          <button
            type="button"
            className="chat-attachment-download-btn"
            onClick={() => download(attachment.fileName)}
            aria-label={`Download ${attachment.fileName}`}
          >
            <FontAwesomeIcon icon={faDownload} />
          </button>
        </div>
      )
    }

    if (!inlineSrc)
      return <span className="chat-attachment-loading">{attachment.fileName}</span>

    return (
      <div className="chat-attachment-inline">
        {classification.inlineKind === 'image' && (
          <img
            src={inlineSrc}
            alt={attachment.fileName}
            className="chat-attachment-image"
            onError={() => setUseBlobFallback(true)}
          />
        )}
        {classification.inlineKind === 'pdf' && (
          <iframe
            src={inlineSrc}
            title={attachment.fileName}
            className="chat-attachment-pdf"
            onError={() => setUseBlobFallback(true)}
          />
        )}
        {classification.inlineKind === 'video' && (
          <video
            src={inlineSrc}
            controls
            className="chat-attachment-video"
            onError={() => setUseBlobFallback(true)}
          />
        )}
        {classification.inlineKind === 'audio' && (
          <audio
            src={inlineSrc}
            controls
            className="chat-attachment-audio"
            onError={() => setUseBlobFallback(true)}
          />
        )}
        <button
          type="button"
          className="chat-attachment-download-btn"
          onClick={() => {
            if (blobUrl)
              download(attachment.fileName)
            else
              window.open(attachment.downloadUrl, '_blank', 'noopener,noreferrer')
          }}
          aria-label={`Download ${attachment.fileName}`}
        >
          <FontAwesomeIcon icon={faDownload} />
        </button>
      </div>
    )
  }

  return (
    <button
      type="button"
      className="chat-attachment-download-link"
      onClick={() => download(attachment.fileName)}
    >
      {attachment.fileName}
    </button>
  )
}
