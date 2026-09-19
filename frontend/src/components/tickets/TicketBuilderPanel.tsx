import { useCallback, useEffect, useState } from 'react'
import { FontAwesomeIcon } from '@fortawesome/react-fontawesome'
import {
  faArrowDown,
  faArrowUp,
  faFloppyDisk,
  faPlus,
  faTrashCan,
  faXmark,
} from '@fortawesome/free-solid-svg-icons'
import { infrastructureApi } from '../../api/infrastructureApi'
import { ticketsApi } from '../../api/ticketsApi'
import { RoomAccessRulesField } from '../infrastructure/RoomEditorFields'
import type { CustomChannelAccessRuleInput, CustomRole } from '../../types/infrastructure'
import type {
  TicketIntakeQuestion,
  TicketIntakeQuestionType,
  TicketPortalConfig,
  TicketTrackingMode,
  UpdateTicketPortalConfigRequest,
} from '../../types/tickets'
import {
  TICKET_INTAKE_QUESTION_TYPES,
  TICKET_TRACKING_MODES,
} from '../../types/tickets'
import { LoadingBars } from '../LoadingBars'

type TicketBuilderPanelProps = {
  channelId: string
  mode: 'edit' | 'preview'
}

function newQuestionId(): string {
  return `q-${crypto.randomUUID()}`
}

function emptyQuestion(type: TicketIntakeQuestionType = 'shortText'): TicketIntakeQuestion {
  return {
    id: newQuestionId(),
    type,
    prompt: '',
    required: false,
    tracksUser: false,
    options: type === 'trueFalse' ? undefined : ['Option 1'],
  }
}

function configToDraft(config: TicketPortalConfig): UpdateTicketPortalConfigRequest {
  return {
    ctaLabel: config.ctaLabel,
    description: config.description,
    purpose: config.purpose,
    filterName: config.filterName || config.purpose,
    trackingMode: config.trackingMode,
    trackingInstructions: config.trackingInstructions,
    decisionLabels: [...config.decisionLabels],
    mentionRoleRules: config.mentionRoleRules.map((rule) => ({
      customRoleId: rule.customRoleId ?? undefined,
      platformRoleBit: rule.platformRoleBit ?? undefined,
      allowedUserId: rule.allowedUserId ?? undefined,
    })),
    staffAccessRules: config.staffAccessRules.map((rule) => ({
      customRoleId: rule.customRoleId ?? undefined,
      platformRoleBit: rule.platformRoleBit ?? undefined,
      allowedUserId: rule.allowedUserId ?? undefined,
    })),
    intakeQuestions: config.intakeQuestions.map((question) => ({
      ...question,
      options: question.options ? [...question.options] : undefined,
    })),
  }
}

function AccessRulesEditor({
  accessRules,
  customRoles,
  onChange,
}: {
  accessRules: CustomChannelAccessRuleInput[]
  customRoles: CustomRole[]
  onChange: (rules: CustomChannelAccessRuleInput[]) => void
}) {
  const [selectedCustomRoleId, setSelectedCustomRoleId] = useState('')
  const [selectedPlatformBit, setSelectedPlatformBit] = useState('')

  return (
    <RoomAccessRulesField
      accessRules={accessRules}
      customRoles={customRoles}
      selectedCustomRoleId={selectedCustomRoleId}
      selectedPlatformBit={selectedPlatformBit}
      onCustomRoleChange={(roleId) => {
        setSelectedCustomRoleId(roleId)
        setSelectedPlatformBit('')
      }}
      onPlatformRoleChange={(roleBit) => {
        setSelectedPlatformBit(roleBit)
        setSelectedCustomRoleId('')
      }}
      onAdd={() => {
        if (selectedCustomRoleId) {
          onChange([...accessRules, { customRoleId: selectedCustomRoleId }])
          setSelectedCustomRoleId('')
          return
        }
        if (selectedPlatformBit) {
          onChange([...accessRules, { platformRoleBit: Number(selectedPlatformBit) }])
          setSelectedPlatformBit('')
        }
      }}
      onRemove={(index) => onChange(accessRules.filter((_, itemIndex) => itemIndex !== index))}
    />
  )
}

export function TicketBuilderPanel({ channelId, mode }: TicketBuilderPanelProps) {
  const [loading, setLoading] = useState(true)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState('')
  const [customRoles, setCustomRoles] = useState<CustomRole[]>([])
  const [draft, setDraft] = useState<UpdateTicketPortalConfigRequest | null>(null)
  const [savedDraft, setSavedDraft] = useState<UpdateTicketPortalConfigRequest | null>(null)
  const [newDecisionLabel, setNewDecisionLabel] = useState('')
  const [previewWizardOpen, setPreviewWizardOpen] = useState(false)

  const load = useCallback(async () => {
    setLoading(true)
    setError('')
    try {
      const [configRes, rolesRes] = await Promise.all([
        ticketsApi.getPortalConfig(channelId),
        infrastructureApi.listRoles(),
      ])
      const nextDraft = configToDraft(configRes.data)
      setDraft(nextDraft)
      setSavedDraft(nextDraft)
      setCustomRoles(rolesRes.data)
    } catch {
      setError('Could not load ticket portal configuration.')
      setDraft(null)
    } finally {
      setLoading(false)
    }
  }, [channelId])

  useEffect(() => {
    void load()
  }, [load])

  function updateDraft(patch: Partial<UpdateTicketPortalConfigRequest>) {
    setDraft((prev) => (prev ? { ...prev, ...patch } : prev))
  }

  function updateQuestion(index: number, patch: Partial<TicketIntakeQuestion>) {
    setDraft((prev) => {
      if (!prev) return prev
      const questions = [...prev.intakeQuestions]
      questions[index] = { ...questions[index], ...patch }
      return { ...prev, intakeQuestions: questions }
    })
  }

  function moveQuestion(index: number, direction: -1 | 1) {
    setDraft((prev) => {
      if (!prev) return prev
      const target = index + direction
      if (target < 0 || target >= prev.intakeQuestions.length) return prev
      const questions = [...prev.intakeQuestions]
      const [moved] = questions.splice(index, 1)
      questions.splice(target, 0, moved)
      return { ...prev, intakeQuestions: questions }
    })
  }

  async function save() {
    if (!draft) return
    setSaving(true)
    setError('')
    try {
      const { data } = await ticketsApi.updatePortalConfig(channelId, draft)
      const nextSaved = configToDraft(data)
      setDraft(nextSaved)
      setSavedDraft(nextSaved)
    } catch {
      setError('Could not save ticket portal configuration.')
    } finally {
      setSaving(false)
    }
  }

  const dirty = JSON.stringify(draft) !== JSON.stringify(savedDraft)

  if (loading) return <LoadingBars message="Loading ticket configuration…" />
  if (error && !draft) return <p className="error">{error}</p>
  if (!draft) return null

  if (mode === 'preview') {
    return (
      <div className="ticket-portal ticket-portal--preview">
        <p className="ticket-portal-description">{draft.description || 'No description yet.'}</p>
        <button
          type="button"
          className="btn-primary ticket-portal-cta"
          onClick={() => setPreviewWizardOpen(true)}
        >
          {draft.ctaLabel}
        </button>
        {previewWizardOpen && (
          <div className="ticket-intake-wizard ticket-intake-wizard--preview" role="dialog" aria-modal="true">
            <div className="ticket-intake-wizard-inner">
              <h3>Preview intake</h3>
              <p className="server-page-subtitle">
                This is a preview only — submitting will not create a ticket from Channel Builder.
              </p>
              {draft.intakeQuestions.map((question) => (
                <div key={question.id} className="sm-field ticket-intake-field">
                  <span className="sm-label">{question.prompt || 'Untitled question'}</span>
                  <span className="ticket-intake-preview-type">{question.type}</span>
                </div>
              ))}
              <div className="ticket-intake-actions">
                <button type="button" className="btn-secondary" onClick={() => setPreviewWizardOpen(false)}>
                  Close preview
                </button>
              </div>
            </div>
          </div>
        )}
      </div>
    )
  }

  return (
    <div className="ticket-builder">
      {error && <p className="error">{error}</p>}

      <div className="sm-field">
        <label htmlFor="ticket-purpose" className="sm-label">Purpose</label>
        <input
          id="ticket-purpose"
          type="text"
          className="sm-input"
          value={draft.purpose}
          onChange={(event) => updateDraft({ purpose: event.target.value })}
          placeholder="e.g. Apply for Tutor Positions"
        />
      </div>

      <div className="sm-field">
        <label htmlFor="ticket-filter-name" className="sm-label">Filter name</label>
        <input
          id="ticket-filter-name"
          type="text"
          className="sm-input"
          value={draft.filterName}
          onChange={(event) => updateDraft({ filterName: event.target.value })}
          placeholder="e.g. Tutor or Mod-Mail"
        />
        <p className="dashboard-hint">Used in ticket room titles: Ticket - {'{Filter}'} - 0001</p>
      </div>

      <div className="sm-field">
        <label htmlFor="ticket-cta" className="sm-label">CTA label</label>
        <input
          id="ticket-cta"
          type="text"
          className="sm-input"
          value={draft.ctaLabel}
          onChange={(event) => updateDraft({ ctaLabel: event.target.value })}
          placeholder="Open Ticket"
        />
      </div>

      <div className="sm-field">
        <label htmlFor="ticket-description" className="sm-label">Description</label>
        <textarea
          id="ticket-description"
          className="sm-input ticket-intake-textarea"
          rows={4}
          value={draft.description}
          onChange={(event) => updateDraft({ description: event.target.value })}
          placeholder="Explain what this portal is for…"
        />
      </div>

      <div className="sm-field">
        <span className="sm-label">Tracking mode</span>
        <div className="sm-segmented ticket-tracking-modes">
          {TICKET_TRACKING_MODES.map((modeOption) => (
            <button
              key={modeOption.value}
              type="button"
              className={`sm-segmented-option ${draft.trackingMode === modeOption.value ? 'active' : ''}`}
              onClick={() => updateDraft({ trackingMode: modeOption.value as TicketTrackingMode })}
            >
              <span>
                {modeOption.label}
                <small>{modeOption.hint}</small>
              </span>
            </button>
          ))}
        </div>
      </div>

      {draft.trackingMode === 'FromIntakeField' && (
        <div className="sm-field">
          <label htmlFor="ticket-tracking-instructions" className="sm-label">Tracking instructions</label>
          <textarea
            id="ticket-tracking-instructions"
            className="sm-input ticket-intake-textarea"
            rows={3}
            value={draft.trackingInstructions ?? ''}
            onChange={(event) => updateDraft({ trackingInstructions: event.target.value || null })}
            placeholder="Instructions for the automated tracker…"
          />
        </div>
      )}

      <div className="sm-field">
        <span className="sm-label">Decision labels</span>
        <div className="sm-rule-add">
          <input
            type="text"
            className="sm-input"
            value={newDecisionLabel}
            onChange={(event) => setNewDecisionLabel(event.target.value)}
            placeholder="Add decision label…"
            aria-label="New decision label"
          />
          <button
            type="button"
            className="sm-icon-btn sm-icon-btn--primary"
            disabled={!newDecisionLabel.trim()}
            onClick={() => {
              const label = newDecisionLabel.trim()
              if (!label) return
              updateDraft({ decisionLabels: [...draft.decisionLabels, label] })
              setNewDecisionLabel('')
            }}
            aria-label="Add decision label"
          >
            <FontAwesomeIcon icon={faPlus} />
          </button>
        </div>
        <div className="sm-rule-chips">
          {draft.decisionLabels.length === 0 && (
            <span className="sm-rule-empty">No decision labels yet.</span>
          )}
          {draft.decisionLabels.map((label, index) => (
            <span key={`${label}-${index}`} className="sm-rule-chip">
              {label}
              <button
                type="button"
                className="sm-rule-chip-remove"
                onClick={() =>
                  updateDraft({
                    decisionLabels: draft.decisionLabels.filter((_, itemIndex) => itemIndex !== index),
                  })
                }
                aria-label={`Remove ${label}`}
              >
                <FontAwesomeIcon icon={faXmark} />
              </button>
            </span>
          ))}
        </div>
      </div>

      <div className="sm-field">
        <span className="sm-label">Staff access roles</span>
        <p className="sm-privacy-hint">Who can view and manage tickets opened from this portal.</p>
      </div>
      <AccessRulesEditor
        accessRules={draft.staffAccessRules}
        customRoles={customRoles}
        onChange={(staffAccessRules) => updateDraft({ staffAccessRules })}
      />

      <div className="sm-field">
        <span className="sm-label">Mention roles</span>
        <p className="sm-privacy-hint">Roles notified when a ticket is opened or receives a decision.</p>
      </div>
      <AccessRulesEditor
        accessRules={draft.mentionRoleRules}
        customRoles={customRoles}
        onChange={(mentionRoleRules) => updateDraft({ mentionRoleRules })}
      />

      <div className="sm-field">
        <div className="sm-panel-header">
          <span className="sm-label">Intake questions</span>
          <button
            type="button"
            className="sm-icon-btn sm-icon-btn--primary"
            onClick={() =>
              updateDraft({ intakeQuestions: [...draft.intakeQuestions, emptyQuestion()] })
            }
            aria-label="Add intake question"
          >
            <FontAwesomeIcon icon={faPlus} />
          </button>
        </div>

        {draft.intakeQuestions.length === 0 && (
          <p className="server-page-subtitle">No intake questions — tickets open immediately.</p>
        )}

        <ul className="ticket-builder-questions">
          {draft.intakeQuestions.map((question, index) => (
            <li key={question.id} className="ticket-builder-question">
              <div className="ticket-builder-question-header">
                <select
                  className="sm-input"
                  value={question.type}
                  onChange={(event) => {
                    const type = event.target.value as TicketIntakeQuestionType
                    updateQuestion(index, {
                      type,
                      options: type === 'trueFalse' ? undefined : question.options ?? ['Option 1'],
                    })
                  }}
                  aria-label="Question type"
                >
                  {TICKET_INTAKE_QUESTION_TYPES.map((option) => (
                    <option key={option.value} value={option.value}>{option.label}</option>
                  ))}
                </select>
                <div className="ticket-builder-question-controls">
                  <button
                    type="button"
                    className="sm-icon-btn"
                    onClick={() => moveQuestion(index, -1)}
                    disabled={index === 0}
                    aria-label="Move question up"
                  >
                    <FontAwesomeIcon icon={faArrowUp} />
                  </button>
                  <button
                    type="button"
                    className="sm-icon-btn"
                    onClick={() => moveQuestion(index, 1)}
                    disabled={index === draft.intakeQuestions.length - 1}
                    aria-label="Move question down"
                  >
                    <FontAwesomeIcon icon={faArrowDown} />
                  </button>
                  <button
                    type="button"
                    className="sm-icon-btn sm-icon-btn--danger"
                    onClick={() =>
                      updateDraft({
                        intakeQuestions: draft.intakeQuestions.filter((_, itemIndex) => itemIndex !== index),
                      })
                    }
                    aria-label="Remove question"
                  >
                    <FontAwesomeIcon icon={faTrashCan} />
                  </button>
                </div>
              </div>

              <input
                type="text"
                className="sm-input"
                value={question.prompt}
                onChange={(event) => updateQuestion(index, { prompt: event.target.value })}
                placeholder="Question prompt"
                aria-label="Question prompt"
              />

              <div className="ticket-builder-question-flags">
                <label>
                  <input
                    type="checkbox"
                    checked={question.required}
                    onChange={(event) => updateQuestion(index, { required: event.target.checked })}
                  />
                  Required
                </label>
                <label>
                  <input
                    type="checkbox"
                    checked={question.tracksUser}
                    onChange={(event) => updateQuestion(index, { tracksUser: event.target.checked })}
                  />
                  Tracks user
                </label>
                <label>
                  <input
                    type="checkbox"
                    checked={Boolean(question.aiOptOut)}
                    onChange={(event) => updateQuestion(index, {
                      aiOptOut: event.target.checked,
                      type: event.target.checked ? 'checkbox' : question.type,
                    })}
                  />
                  AI opt-out
                </label>
              </div>

              {question.type === 'mixed' && (
                <input
                  type="text"
                  className="sm-input"
                  value={(question.allowedResponseKinds ?? []).join(',')}
                  onChange={(event) =>
                    updateQuestion(index, {
                      allowedResponseKinds: event.target.value
                        .split(',')
                        .map((part) => part.trim())
                        .filter(Boolean) as TicketIntakeQuestion['allowedResponseKinds'],
                    })
                  }
                  placeholder="Allowed kinds: file,link,forward,text"
                  aria-label="Allowed response kinds"
                />
              )}

              {(question.type === 'multipleChoice' ||
                question.type === 'multiSelect' ||
                question.type === 'dropdown') && (
                <textarea
                  className="sm-input ticket-intake-textarea"
                  rows={3}
                  value={(question.options ?? []).join('\n')}
                  onChange={(event) =>
                    updateQuestion(index, {
                      options: event.target.value
                        .split('\n')
                        .map((line) => line.trim())
                        .filter(Boolean),
                    })
                  }
                  placeholder="One option per line"
                  aria-label="Question options"
                />
              )}
            </li>
          ))}
        </ul>
      </div>

      <button type="button" className="btn-primary" disabled={!dirty || saving} onClick={() => void save()}>
        <FontAwesomeIcon icon={faFloppyDisk} /> {saving ? 'Saving…' : 'Save configuration'}
      </button>
    </div>
  )
}
