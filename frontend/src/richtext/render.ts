import DOMPurify from 'dompurify'
import { markdownRenderer, type MentionStyleLookup, type RichContentEnv } from './markdown'

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
// KaTeX's own HTML output leans on inline styles for layout (fraction bars, sub/superscript
// offsets, matrix columns) that fall well outside that allowlist. Those fragments never come from
// arbitrary user styling, so they get the wider set below — see the placeholder dance in
// renderRichContent for how a fragment earns this treatment.
const MATH_LAYOUT_STYLE_PROPERTIES = new Set([
  'border-bottom-width',
  'border-style',
  'border-width',
  'height',
  'left',
  'margin-left',
  'margin-right',
  'min-width',
  'padding-left',
  'position',
  'top',
  'vertical-align',
  'width',
])
const DANGEROUS_STYLE_VALUE =
  /url\s*\(|expression\s*\(|javascript:|vbscript:|-moz-binding|behavior\s*:|@import|attr\s*\(|^\s*position\s*:\s*(fixed|sticky)\b|\d\s*(vw|vh|vmin|vmax)\b/i

function sanitizeStyleAttribute(rawStyle: string, allowMathLayout: boolean): string {
  return rawStyle
    .split(';')
    .map((declaration) => declaration.trim())
    .filter((declaration) => {
      if (!declaration) return false
      const [prop] = declaration.split(':')
      const name = prop?.trim().toLowerCase()
      if (!name) return false
      return ALLOWED_STYLE_PROPERTIES.has(name) || (allowMathLayout && MATH_LAYOUT_STYLE_PROPERTIES.has(name))
    })
    .filter((declaration) => !DANGEROUS_STYLE_VALUE.test(declaration))
    .join('; ')
}

DOMPurify.addHook('uponSanitizeAttribute', (_node, data, config) => {
  if (data.attrName === 'style')
    data.attrValue = sanitizeStyleAttribute(data.attrValue, Boolean((config as Record<string, unknown>).ALLOW_MATH_LAYOUT_STYLE))
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
const MATH_FRAGMENT_SANITIZE_CONFIG = { ...SANITIZE_CONFIG, ALLOW_MATH_LAYOUT_STYLE: true }

// markdownRenderer never inlines KaTeX's HTML directly — it stashes each rendered fragment on
// env.mathFragments and drops one of these markers in its place instead (see markdown.ts). U+E000
// is a Private Use Area code point real messages don't contain, and any pre-existing occurrence in
// the source is stripped below, so a pasted-in marker can never redirect this substitution to
// smuggle a fragment past the strict, user-content sanitize pass above.
const MATH_PLACEHOLDER_CHAR = '\uE000'
const MATH_PLACEHOLDER_STRIP_RE = new RegExp(MATH_PLACEHOLDER_CHAR, 'g')
const MATH_PLACEHOLDER_RE = new RegExp(`${MATH_PLACEHOLDER_CHAR}(\\d+)${MATH_PLACEHOLDER_CHAR}`, 'g')

/**
 * The single Markdown+LaTeX rendering pipeline shared by chat (real and mock preview) and info
 * pages: raw Markdown source (with embedded LaTeX and, where needed, plain inline HTML) in,
 * sanitized HTML out. Never trust the output of markdownRenderer.render on its own — it allows
 * raw HTML pass-through (needed for e.g. <u>underline</u>), so DOMPurify is the actual security
 * boundary here.
 */
export function renderRichContent(source: string, mentionStyles?: MentionStyleLookup): string {
  const env: RichContentEnv = { mentionStyles, mathFragments: [] }
  const rendered = markdownRenderer.render(source.replace(MATH_PLACEHOLDER_STRIP_RE, ''), env)
  const sanitized = DOMPurify.sanitize(rendered, SANITIZE_CONFIG)
  return sanitized.replace(MATH_PLACEHOLDER_RE, (_match, indexStr: string) => {
    const fragment = env.mathFragments?.[Number(indexStr)]
    return fragment === undefined ? '' : DOMPurify.sanitize(fragment, MATH_FRAGMENT_SANITIZE_CONFIG)
  })
}

export type { MentionStyleLookup }
