/** True when a shortcut must not steal typing from a text field or editor. */
export function isEditableKeyboardTarget(target: EventTarget | null): boolean {
  if (!(target instanceof HTMLElement))
    return false
  if (target.isContentEditable)
    return true
  const tagName = target.tagName
  if (tagName === 'TEXTAREA' || tagName === 'SELECT')
    return true
  if (tagName !== 'INPUT' || !(target instanceof HTMLInputElement))
    return false
  const inputType = (target.type || 'text').toLowerCase()
  return inputType !== 'checkbox'
    && inputType !== 'radio'
    && inputType !== 'button'
    && inputType !== 'submit'
    && inputType !== 'reset'
}

/** Ctrl+S on Windows/Linux or Cmd+S on macOS. */
export function isSaveChord(event: KeyboardEvent): boolean {
  if (event.key !== 's' && event.key !== 'S')
    return false
  return event.ctrlKey || event.metaKey
}

export function isPrimaryModifier(event: KeyboardEvent): boolean {
  return event.ctrlKey || event.metaKey
}
