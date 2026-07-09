import DOMPurify from 'dompurify'
import { markdownRenderer, type MentionStyleLookup } from './markdown'

DOMPurify.addHook('afterSanitizeAttributes', (node) => {
  if (node.tagName === 'A') {
    node.setAttribute('target', '_blank')
    node.setAttribute('rel', 'noopener noreferrer')
  }
})

const SANITIZE_CONFIG = {
  USE_PROFILES: { html: true, mathMl: true, svg: true },
  ADD_ATTR: ['style', 'class', 'target', 'rel', 'colspan', 'rowspan', 'align', 'checked', 'disabled'],
  FORBID_TAGS: ['script', 'style', 'iframe', 'object', 'embed'],
}

/**
 * The single Markdown+LaTeX rendering pipeline shared by chat (real and mock preview) and info
 * pages: raw Markdown source (with embedded LaTeX and, where needed, plain inline HTML) in,
 * sanitized HTML out. Never trust the output of markdownRenderer.render on its own — it allows
 * raw HTML pass-through (needed for e.g. <u>underline</u>), so DOMPurify is the actual security
 * boundary here.
 */
export function renderRichContent(source: string, mentionStyles?: MentionStyleLookup): string {
  const rendered = markdownRenderer.render(source, { mentionStyles })
  return DOMPurify.sanitize(rendered, SANITIZE_CONFIG)
}

export type { MentionStyleLookup }
