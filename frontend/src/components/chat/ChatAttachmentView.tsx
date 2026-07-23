import { useEffect, useState } from 'react'
import { FontAwesomeIcon } from '@fortawesome/react-fontawesome'
import { faDownload } from '@fortawesome/free-solid-svg-icons'
import { byPrefixAndName } from '../../icons/byPrefixAndName'
import type { ChatAttachmentInfo } from '../../types/chat'
import { useAuthenticatedAttachment } from '../../hooks/useAuthenticatedAttachment'
import { classifyAttachment } from '../../utils/attachmentClassification'
import { highlightCode } from '../../utils/highlightCode'
import { ConfirmModal } from '../infrastructure/ConfirmModal'

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
  const [riskAcknowledged, setRiskAcknowledged] = useState(false)
  const [showSafetyWarning, setShowSafetyWarning] = useState(false)
  const requiresCaution = attachment.scanStatus === 'Infected'

  const needsBlob = (
    useBlobFallback
    || classification.mode === 'hazard'
    || classification.mode === 'link'
    || (classification.mode === 'inline' && classification.inlineKind === 'text')
    || (requiresCaution && riskAcknowledged)
  ) && (!requiresCaution || riskAcknowledged)

  const { blobUrl, loading, error, download } = useAuthenticatedAttachment(
    attachment.attachmentId,
    needsBlob,
    riskAcknowledged,
  )

  const downloadUrl = riskAcknowledged
    ? `${attachment.downloadUrl}${attachment.downloadUrl.includes('?') ? '&' : '?'}riskAcknowledged=true`
    : attachment.downloadUrl
  const inlineSrc = blobUrl ?? downloadUrl
  const showTextInline =
    classification.mode === 'inline'
    && classification.inlineKind === 'text'
    && textPreview !== null

  useEffect(() => {
    if (requiresCaution && !riskAcknowledged)
      return
    if (classification.mode !== 'inline' || classification.inlineKind !== 'text')
      return

    let cancelled = false
    setTextLoading(true)
    setTextTooLarge(false)
    setTextPreview(null)

    void (async () => {
      try {
        const response = await fetch(downloadUrl)
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
  }, [classification.inlineKind, classification.mode, downloadUrl, requiresCaution, riskAcknowledged])

  useEffect(() => {
    if (requiresCaution && !riskAcknowledged)
      return
    if (!expanded || attachment.inlinePreviewKind !== 'text' || textPreview !== null)
      return

    let cancelled = false
    setTextLoading(true)

    void (async () => {
      try {
        const source = blobUrl ?? downloadUrl
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
  }, [attachment.inlinePreviewKind, blobUrl, downloadUrl, expanded, requiresCaution, riskAcknowledged, textPreview])

  if (requiresCaution && !riskAcknowledged) {
    const warningBody = attachment.scanStatus === 'Infected'
      ? 'Warning! This file may be malicious and could steal your data, corrupt your device, or similar. Proceed with caution…'
      : 'Warning! This file could not be verified as safe and may be malicious. It could steal your data, corrupt your device, or similar. Proceed with caution…'

    return (
      <div className="chat-attachment-hazard">
        <FontAwesomeIcon icon={byPrefixAndName.fas['file-circle-exclamation']} />
        <span>{attachment.fileName}</span>
        <button
          type="button"
          className="chat-attachment-download-link"
          onClick={() => setShowSafetyWarning(true)}
        >
          View or download
        </button>
        {showSafetyWarning && (
          <ConfirmModal
            title="Warning — potentially malicious file"
            onClose={() => setShowSafetyWarning(false)}
            actions={[
              { label: 'Cancel', variant: 'secondary', onClick: () => setShowSafetyWarning(false) },
              {
                label: 'Proceed anyway',
                variant: 'primary',
                onClick: () => {
                  setRiskAcknowledged(true)
                  setShowSafetyWarning(false)
                  if (classification.mode !== 'inline')
                    void download(attachment.fileName, true)
                },
              },
            ]}
          >
            <p className="chat-attachment-hazard-banner">{warningBody}</p>
            <p>You can still download or open this file after acknowledging the risk.</p>
          </ConfirmModal>
        )}
      </div>
    )
  }

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
              window.open(downloadUrl, '_blank', 'noopener,noreferrer')
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
