import MarkdownIt from 'markdown-it'
import type { RuleInline } from 'markdown-it/lib/parser_inline.mjs'
import type { RenderRule } from 'markdown-it/lib/renderer.mjs'
import texmath from 'markdown-it-texmath'
import sub from 'markdown-it-sub'
import sup from 'markdown-it-sup'
import mark from 'markdown-it-mark'
import taskLists from 'markdown-it-task-lists'
import katex from 'katex'
import hljs from 'highlight.js/lib/core'
import bash from 'highlight.js/lib/languages/bash'
import c from 'highlight.js/lib/languages/c'
import cpp from 'highlight.js/lib/languages/cpp'
import csharp from 'highlight.js/lib/languages/csharp'
import css from 'highlight.js/lib/languages/css'
import go from 'highlight.js/lib/languages/go'
import java from 'highlight.js/lib/languages/java'
import javascript from 'highlight.js/lib/languages/javascript'
import json from 'highlight.js/lib/languages/json'
import markdownLang from 'highlight.js/lib/languages/markdown'
import plaintext from 'highlight.js/lib/languages/plaintext'
import python from 'highlight.js/lib/languages/python'
import rust from 'highlight.js/lib/languages/rust'
import sql from 'highlight.js/lib/languages/sql'
import typescript from 'highlight.js/lib/languages/typescript'
import xml from 'highlight.js/lib/languages/xml'
import yaml from 'highlight.js/lib/languages/yaml'

hljs.registerLanguage('bash', bash)
hljs.registerLanguage('shell', bash)
hljs.registerLanguage('c', c)
hljs.registerLanguage('cpp', cpp)
hljs.registerLanguage('c++', cpp)
hljs.registerLanguage('csharp', csharp)
hljs.registerLanguage('cs', csharp)
hljs.registerLanguage('css', css)
hljs.registerLanguage('go', go)
hljs.registerLanguage('java', java)
hljs.registerLanguage('javascript', javascript)
hljs.registerLanguage('js', javascript)
hljs.registerLanguage('json', json)
hljs.registerLanguage('markdown', markdownLang)
hljs.registerLanguage('md', markdownLang)
hljs.registerLanguage('plaintext', plaintext)
hljs.registerLanguage('python', python)
hljs.registerLanguage('py', python)
hljs.registerLanguage('rust', rust)
hljs.registerLanguage('rs', rust)
hljs.registerLanguage('sql', sql)
hljs.registerLanguage('typescript', typescript)
hljs.registerLanguage('ts', typescript)
hljs.registerLanguage('xml', xml)
hljs.registerLanguage('html', xml)
hljs.registerLanguage('yaml', yaml)
hljs.registerLanguage('yml', yaml)

function escapeHtml(value: string): string {
  return value
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;')
}

/** Colors looked up by lowercase name — matches MentionStyleLookup used by the legacy plain-text renderer. */
export interface MentionStyleLookup {
  userColors: Record<string, string>
  roleColors: Record<string, string>
}

/**
 * Render-time state threaded through markdownRenderer.render(source, env). `mathFragments`
 * collects KaTeX HTML out-of-band: math tokens render to U+E000-delimited index placeholders and
 * push their real markup here, so render.ts can sanitize user HTML strictly while giving KaTeX's
 * layout styles (top/height/vertical-align/…) a wider style allowlist in a second pass.
 */
export interface RichContentEnv {
  mentionStyles?: MentionStyleLookup
  mathFragments?: string[]
}

const BROADCAST_TOKENS = new Set(['everyone', 'here'])
const NULL_MENTION_TOKEN = 'null'
const MENTION_START_CHAR_CODE = 0x40 // '@'
const MENTION_PATTERN = /^@([\p{L}\p{N}_][\p{L}\p{N}_.-]*)/u

const mentionRule: RuleInline = (state, silent) => {
  if (state.src.charCodeAt(state.pos) !== MENTION_START_CHAR_CODE)
    return false

  const match = MENTION_PATTERN.exec(state.src.slice(state.pos))
  if (!match)
    return false

  if (!silent) {
    const token = state.push('mention', '', 0)
    token.content = match[1]
  }

  state.pos += match[0].length
  return true
}

export const markdownRenderer: MarkdownIt = new MarkdownIt({
  html: true,
  linkify: true,
  breaks: true,
  typographer: false,
  highlight(code, lang) {
    const language = lang && hljs.getLanguage(lang) ? lang : null
    const highlighted = language
      ? hljs.highlight(code, { language }).value
      : escapeHtml(code)
    return `<pre class="hc-code-block"><code class="hljs${language ? ` language-${language}` : ''}">${highlighted}</code></pre>`
  },
})
  .use(texmath, {
    engine: katex,
    delimiters: 'dollars',
    katexOptions: { throwOnError: false, strict: false },
  })
  .use(sub)
  .use(sup)
  .use(mark)
  .use(taskLists, { enabled: true, label: true })

markdownRenderer.linkify.set({ fuzzyEmail: false })
markdownRenderer.inline.ruler.before('text', 'mention', mentionRule)
const renderMention: RenderRule = (tokens, idx, _options, env: { mentionStyles?: MentionStyleLookup }) => {
  const name = tokens[idx].content
  const lower = name.toLowerCase()

  if (lower === NULL_MENTION_TOKEN)
    return `@${escapeHtml(name)}`

  const styles = env?.mentionStyles
  const color = styles?.userColors[lower] ?? styles?.roleColors[lower]
  const isBroadcast = BROADCAST_TOKENS.has(lower)
  const style = color ? ` style="color:${color};background-color:${color}22"` : ''
  return `<span class="chat-mention${isBroadcast ? ' chat-mention--broadcast' : ''}"${style}>@${escapeHtml(name)}</span>`
}

markdownRenderer.renderer.rules.mention = renderMention

const MATH_PLACEHOLDER_CHAR = '\uE000'

function renderMathToken(tex: string, displayMode: boolean, env: RichContentEnv): string {
  let html: string
  try {
    html = katex.renderToString(tex, { throwOnError: false, strict: false, displayMode })
  } catch {
    html = escapeHtml(tex)
  }
  // Without a fragment stash (someone calling markdownRenderer.render directly) fall back to
  // inlining the markup — it still goes through the strict sanitize pass, so it's safe, just laid
  // out poorly.
  if (!env?.mathFragments)
    return html
  const index = env.mathFragments.push(html) - 1
  return `${MATH_PLACEHOLDER_CHAR}${index}${MATH_PLACEHOLDER_CHAR}`
}

// Replace markdown-it-texmath's renderer rules (registered by .use(texmath, …) above) so KaTeX
// markup is stashed on env.mathFragments instead of being inlined — see RichContentEnv. Parsing
// (delimiter recognition) still belongs to texmath; only the final HTML emission changes.
markdownRenderer.renderer.rules.math_inline = (tokens, idx, _options, env: RichContentEnv) =>
  renderMathToken(tokens[idx].content, false, env)
markdownRenderer.renderer.rules.math_inline_double = (tokens, idx, _options, env: RichContentEnv) =>
  renderMathToken(tokens[idx].content, true, env)
markdownRenderer.renderer.rules.math_block = (tokens, idx, _options, env: RichContentEnv) =>
  `<section>${renderMathToken(tokens[idx].content, true, env)}</section>\n`
markdownRenderer.renderer.rules.math_block_eqno = (tokens, idx, _options, env: RichContentEnv) =>
  `<section class="eqno">${renderMathToken(tokens[idx].content, true, env)}<span>(${escapeHtml(tokens[idx].info)})</span></section>\n`
