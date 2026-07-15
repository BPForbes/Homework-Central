export const INBOX_UPDATED_EVENT = 'homework-central:inbox-updated'

export function notifyInboxUpdated(): void {
  window.dispatchEvent(new Event(INBOX_UPDATED_EVENT))
}
