import { useCallback, useEffect, useRef, useState } from 'react'
import { FontAwesomeIcon } from '@fortawesome/react-fontawesome'
import { faEye, faPen, faPlus } from '@fortawesome/free-solid-svg-icons'
import { infrastructureApi } from '../../api/infrastructureApi'
import { RichTextToolbar } from '../../richtext/RichTextToolbar'
import { RichContent } from '../../richtext/RichContent'
import { FormattingToggleButton } from '../../richtext/FormattingToggleButton'
import type { InfoEntryFeed } from '../../types/infrastructure'
import { formatUtcTimestamp } from '../../utils/formatUtcTimestamp'
import { LoadingBars } from '../LoadingBars'

interface InfoEntriesFeedProps {
  roomId: string
  /** Forces the non-editor read-only view regardless of what the server says the caller can do — used by the channel builder's Preview mode. */
  readOnly?: boolean
}


interface InfoEntryEditorProps {
  initialContent: string
  saving: boolean
  error: string
  submitLabel: string
  onSave: (content: string) => void
  onCancel: () => void
}

function InfoEntryEditor({ initialContent, saving, error, submitLabel, onSave, onCancel }: InfoEntryEditorProps) {
  const [content, setContent] = useState(initialContent)
  const [showFormatting, setShowFormatting] = useState(false)
  const textareaRef = useRef<HTMLTextAreaElement>(null)

  return (
    <div className="info-entry-editor">
      <div className="chat-composer-toolbar-row">
        <RichTextToolbar textareaRef={textareaRef} value={content} onChange={setContent} />
        <FormattingToggleButton active={showFormatting} onToggle={() => setShowFormatting((prev) => !prev)} />
      </div>
      {showFormatting && (
        <div className="rich-preview-pane">
          {content.trim() ? <RichContent content={content} /> : <p className="chat-messages-empty">Nothing to preview yet.</p>}
        </div>
      )}
      <textarea
        ref={textareaRef}
        className={`sm-textarea info-entry-textarea ${showFormatting ? 'rich-editor-source--preview-hidden' : ''}`}
        rows={8}
        value={content}
        onChange={(e) => setContent(e.target.value)}
        placeholder="Write this entry using Markdown — **bold**, *italic*, lists, `code`, $inline math$, and more."
        readOnly={showFormatting}
        tabIndex={showFormatting ? -1 : 0}
        aria-hidden={showFormatting || undefined}
      />
      {error && <p className="error">{error}</p>}
      <div className="infra-list-actions">
        <button type="button" className="btn-primary" disabled={saving || !content.trim()} onClick={() => onSave(content)}>
          {saving ? 'Saving…' : submitLabel}
        </button>
        <button type="button" className="btn-secondary" onClick={onCancel} disabled={saving}>
          Cancel
        </button>
      </div>
    </div>
  )
}

/**
 * The Info room feed: a list of dated Markdown+LaTeX entries. New entries can always be added by
 * an authorized editor; existing entries lock for editing once their own window expires unless
 * the editor is Owner or System Administrator (enforced server-side — this component just
 * reflects what the API reports via feed.canCreate / entry.canEdit).
 */
export function InfoEntriesFeed({ roomId, readOnly = false }: InfoEntriesFeedProps) {
  const [feed, setFeed] = useState<InfoEntryFeed | null>(null)
  const [loading, setLoading] = useState(true)
  const [loadError, setLoadError] = useState('')
  const [composing, setComposing] = useState(false)
  const [editingEntryId, setEditingEntryId] = useState<string | null>(null)
  const [saving, setSaving] = useState(false)
  const [actionError, setActionError] = useState('')

  const load = useCallback(async () => {
    setLoading(true)
    setLoadError('')
    try {
      const { data } = await infrastructureApi.listInfoEntries(roomId)
      setFeed(data)
    } catch {
      setLoadError('Could not load this info page.')
    } finally {
      setLoading(false)
    }
  }, [roomId])

  useEffect(() => {
    void load()
  }, [load])

  // Preview mode must not leave an open editor/composer from a prior Edit session.
  useEffect(() => {
    if (!readOnly) return
    setEditingEntryId(null)
    setComposing(false)
    setActionError('')
  }, [readOnly])

  async function handleCreate(content: string) {
    setSaving(true)
    setActionError('')
    try {
      await infrastructureApi.createInfoEntry(roomId, content)
      setComposing(false)
      await load()
    } catch {
      setActionError('Could not add that entry.')
    } finally {
      setSaving(false)
    }
  }

  async function handleUpdate(entryId: string, content: string) {
    setSaving(true)
    setActionError('')
    try {
      await infrastructureApi.updateInfoEntry(entryId, content)
      setEditingEntryId(null)
      await load()
    } catch {
      setActionError('Could not save that entry. It may be outside its editable window.')
    } finally {
      setSaving(false)
    }
  }

  if (loading) return <LoadingBars message="Loading entries…" />
  if (loadError) return <p className="error">{loadError}</p>
  if (!feed) return null

  const canCreate = !readOnly && feed.canCreate

  return (
    <div className="info-entries-feed">
      {readOnly && (
        <p className="dashboard-hint">
          <FontAwesomeIcon icon={faEye} /> Previewing as a member who cannot edit this page.
        </p>
      )}

      {canCreate && !composing && (
        <button type="button" className="btn-primary info-entry-new-btn" onClick={() => setComposing(true)}>
          <FontAwesomeIcon icon={faPlus} /> New entry
        </button>
      )}

      {canCreate && composing && (
        <div className="server-page-card info-entry-card">
          <InfoEntryEditor
            initialContent=""
            saving={saving}
            error={actionError}
            submitLabel="Post entry"
            onSave={(content) => void handleCreate(content)}
            onCancel={() => {
              setComposing(false)
              setActionError('')
            }}
          />
        </div>
      )}

      {feed.entries.length === 0 && !composing && <p className="server-page-subtitle">No entries yet.</p>}

      <ul className="info-entries-list">
        {feed.entries.map((entry) => (
          <li key={entry.entryId} className="info-entry-card server-page-card">
            <div className="info-entry-meta">
              <span className="info-entry-author">{entry.authorUsername}</span>
              <time className="info-entry-time" dateTime={entry.createdAtUtc}>
                {formatUtcTimestamp(entry.createdAtUtc)}
              </time>
              {entry.updatedAtUtc !== entry.createdAtUtc && <span className="info-entry-edited">(edited)</span>}
              {!readOnly && entry.canEdit && editingEntryId !== entry.entryId && (
                <button
                  type="button"
                  className="sm-icon-btn info-entry-edit-btn"
                  onClick={() => setEditingEntryId(entry.entryId)}
                  title="Edit entry"
                >
                  <FontAwesomeIcon icon={faPen} />
                </button>
              )}
            </div>
            {!readOnly && editingEntryId === entry.entryId ? (
              <InfoEntryEditor
                initialContent={entry.content}
                saving={saving}
                error={actionError}
                submitLabel="Save changes"
                onSave={(content) => void handleUpdate(entry.entryId, content)}
                onCancel={() => {
                  setEditingEntryId(null)
                  setActionError('')
                }}
              />
            ) : (
              <RichContent content={entry.content} />
            )}
          </li>
        ))}
      </ul>
    </div>
  )
}
