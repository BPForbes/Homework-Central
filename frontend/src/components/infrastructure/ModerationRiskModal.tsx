import { PasswordConfirmModal } from './PasswordConfirmModal'

interface ModerationRiskModalProps {
  open: boolean
  riskyPermissions: string[]
  onConfirm: (password: string) => void | Promise<void>
  onCancel: () => void
}

export function ModerationRiskModal({
  open,
  riskyPermissions,
  onConfirm,
  onCancel,
}: ModerationRiskModalProps) {
  return (
    <PasswordConfirmModal
      open={open}
      title="High moderation risk"
      message={`This role grants powerful moderation permissions (${riskyPermissions.join(', ')}) on a public room. Enter your password to confirm.`}
      onConfirm={onConfirm}
      onCancel={onCancel}
    />
  )
}
