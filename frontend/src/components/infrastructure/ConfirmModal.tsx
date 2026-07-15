import { useEffect, useId, useRef, type ReactNode } from 'react'

export interface ConfirmModalAction {
  label: string
  variant: 'primary' | 'secondary'
  onClick: () => void
  disabled?: boolean
}

interface ConfirmModalProps {
  title: string
  children: ReactNode
  actions: ConfirmModalAction[]
  onClose: () => void
  wide?: boolean
}

function getFocusableElements(container: HTMLElement): HTMLElement[] {
  return Array.from(
    container.querySelectorAll<HTMLElement>(
      'button:not([disabled]), [href], input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])',
    ),
  )
}

export function ConfirmModal({
  title,
  children,
  actions,
  onClose,
  wide = false,
}: ConfirmModalProps) {
  const titleId = useId()
  const panelRef = useRef<HTMLDivElement>(null)
  const previousFocusRef = useRef<HTMLElement | null>(null)
  const onCloseRef = useRef(onClose)

  useEffect(() => {
    onCloseRef.current = onClose
  }, [onClose])

  useEffect(() => {
    previousFocusRef.current = document.activeElement as HTMLElement | null
    const focusable = panelRef.current ? getFocusableElements(panelRef.current) : []
    focusable[0]?.focus()

    function handleKeyDown(event: KeyboardEvent) {
      if (event.key === 'Escape') {
        event.preventDefault()
        onCloseRef.current()
        return
      }

      if (event.key !== 'Tab' || !panelRef.current)
        return

      const items = getFocusableElements(panelRef.current)
      if (items.length === 0)
        return

      const first = items[0]
      const last = items[items.length - 1]
      if (event.shiftKey && document.activeElement === first) {
        event.preventDefault()
        last.focus()
      } else if (!event.shiftKey && document.activeElement === last) {
        event.preventDefault()
        first.focus()
      }
    }

    document.addEventListener('keydown', handleKeyDown)
    return () => {
      document.removeEventListener('keydown', handleKeyDown)
      previousFocusRef.current?.focus()
    }
  }, [])

  return (
    <div className="confirm-modal-backdrop" role="presentation" onClick={onClose}>
      <div
        ref={panelRef}
        className={`confirm-modal ${wide ? 'confirm-modal-wide' : ''}`}
        role="dialog"
        aria-modal="true"
        aria-labelledby={titleId}
        onClick={(event) => event.stopPropagation()}
      >
        <h4 id={titleId} className="confirm-modal-title">{title}</h4>
        {children}
        <div className={`confirm-modal-actions ${actions.length > 1 ? '' : 'confirm-modal-actions-single'}`}>
          {actions.map((action) => (
            <button
              key={action.label}
              type="button"
              className={`${action.variant === 'primary' ? 'btn-primary' : 'btn-secondary'} confirm-modal-button`}
              disabled={action.disabled}
              onClick={action.onClick}
            >
              {action.label}
            </button>
          ))}
        </div>
      </div>
    </div>
  )
}
