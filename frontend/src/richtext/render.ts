import DOMPurify from 'dompurify'
import { markdownRenderer, type MentionStyleLookup } from './markdown'

// DOMPurify decides whether to keep the `style` attribute at all, but does NOT parse or filter
// the CSS *inside* it — `style="url(javascript:...)"` or `style="position:fixed;..."` survives
// untouched by default. Since the formatting toolbar (and any hand-typed <span style="...">)
// relies on the style attribute for text color/size/family, that's the only door available for
// smuggling other CSS, so it needs its own allowlist rather than trusting DOMPurify to cover it.
const ALLOWED_STYLE_PROPERTIES = new Set([
  'color',
  'background-color',
  'font-size',
  'font-family',
  'font-weight',
  'font-style',
  'text-decoration',
  'text-align',
])
const DANGEROUS_STYLE_VALUE = /url\s*\(|expression\s*\(|javascript:|vbscript:|-moz-binding|behavior\s*:|@import|attr\s*\(/i

function sanitizeStyleAttribute(rawStyle: string): string {
  return rawStyle
    .split(';')
    .map((declaration) => declaration.trim())
    .filter((declaration) => {
      if (!declaration) return false
      const [prop] = declaration.split(':')
      return prop !== undefined && ALLOWED_STYLE_PROPERTIES.has(prop.trim().toLowerCase())
    })
    .filter((declaration) => !DANGEROUS_STYLE_VALUE.test(declaration))
    .join('; ')
}

DOMPurify.addHook('uponSanitizeAttribute', (_node, data) => {
  if (data.attrName === 'style')
    data.attrValue = sanitizeStyleAttribute(data.attrValue)
})

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
