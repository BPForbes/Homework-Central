export interface TextSelection {
  start: number
  end: number
}

export interface ToolbarActionResult {
  value: string
  selectionStart: number
  selectionEnd: number
}

export type ToolbarActionId =
  | 'bold'
  | 'italic'
  | 'underline'
  | 'strikethrough'
  | 'highlight'
  | 'sub'
  | 'sup'
  | 'h1'
  | 'h2'
  | 'h3'
  | 'ul'
  | 'ol'
  | 'checklist'
  | 'quote'
  | 'link'
  | 'image'
  | 'inlineCode'
  | 'codeBlock'
  | 'hr'
  | 'table'
  | 'inlineMath'
  | 'displayMath'
  | 'textColor'
  | 'fontSize'
  | 'fontFamily'
  | 'align'

function wrapSelection(
  value: string,
  sel: TextSelection,
  before: string,
  after: string,
  placeholder: string,
): ToolbarActionResult {
  const hasSelection = sel.end > sel.start
  const selected = hasSelection ? value.slice(sel.start, sel.end) : placeholder
  const nextValue = value.slice(0, sel.start) + before + selected + after + value.slice(sel.end)
  const selectionStart = sel.start + before.length
  const selectionEnd = selectionStart + selected.length
  return { value: nextValue, selectionStart, selectionEnd }
}

function prefixLines(value: string, sel: TextSelection, prefix: string | ((lineIndex: number) => string)): ToolbarActionResult {
  const lineStart = value.lastIndexOf('\n', Math.max(sel.start - 1, 0)) + 1
  const nextNewline = value.indexOf('\n', sel.end)
  const lineEnd = nextNewline === -1 ? value.length : nextNewline
  const block = value.slice(lineStart, lineEnd)
  const lines = block.length > 0 ? block.split('\n') : ['']
  const prefixed = lines
    .map((line, index) => `${typeof prefix === 'function' ? prefix(index) : prefix}${line}`)
    .join('\n')
  const nextValue = value.slice(0, lineStart) + prefixed + value.slice(lineEnd)
  return { value: nextValue, selectionStart: lineStart, selectionEnd: lineStart + prefixed.length }
}

function insertBlock(value: string, sel: TextSelection, block: string): ToolbarActionResult {
  const needsLeadingNewline = sel.start > 0 && value[sel.start - 1] !== '\n'
  const needsTrailingNewline = sel.start < value.length && value[sel.start] !== '\n'
  const text = `${needsLeadingNewline ? '\n' : ''}${block}${needsTrailingNewline ? '\n' : ''}`
  const nextValue = value.slice(0, sel.start) + text + value.slice(sel.start)
  const cursor = sel.start + text.length
  return { value: nextValue, selectionStart: cursor, selectionEnd: cursor }
}

/** Applies one toolbar action, inserting Markdown (or a minimal inline-HTML span) at the cursor/selection. */
export function applyToolbarAction(
  id: ToolbarActionId,
  value: string,
  sel: TextSelection,
  param?: string,
): ToolbarActionResult {
  switch (id) {
    case 'bold':
      return wrapSelection(value, sel, '**', '**', 'bold text')
    case 'italic':
      return wrapSelection(value, sel, '*', '*', 'italic text')
    case 'underline':
      return wrapSelection(value, sel, '<u>', '</u>', 'underlined text')
    case 'strikethrough':
      return wrapSelection(value, sel, '~~', '~~', 'strikethrough text')
    case 'highlight':
      return wrapSelection(value, sel, '==', '==', 'highlighted text')
    case 'sub':
      return wrapSelection(value, sel, '~', '~', 'sub')
    case 'sup':
      return wrapSelection(value, sel, '^', '^', 'sup')
    case 'h1':
      return prefixLines(value, sel, '# ')
    case 'h2':
      return prefixLines(value, sel, '## ')
    case 'h3':
      return prefixLines(value, sel, '### ')
    case 'ul':
      return prefixLines(value, sel, '- ')
    case 'ol':
      return prefixLines(value, sel, (index) => `${index + 1}. `)
    case 'checklist':
      return prefixLines(value, sel, '- [ ] ')
    case 'quote':
      return prefixLines(value, sel, '> ')
    case 'link':
      return wrapSelection(value, sel, '[', '](https://example.com)', 'link text')
    case 'image':
      return wrapSelection(value, sel, '![', '](https://example.com/image.png)', 'alt text')
    case 'inlineCode':
      return wrapSelection(value, sel, '`', '`', 'code')
    case 'codeBlock':
      return insertBlock(value, sel, '```\ncode\n```')
    case 'hr':
      return insertBlock(value, sel, '---')
    case 'table':
      return insertBlock(value, sel, '| Column 1 | Column 2 |\n| --- | --- |\n| Cell | Cell |')
    case 'inlineMath':
      return wrapSelection(value, sel, '$', '$', 'x^2')
    case 'displayMath':
      return insertBlock(value, sel, '$$\nx^2\n$$')
    case 'textColor':
      return wrapSelection(value, sel, `<span style="color:${param ?? '#e63946'}">`, '</span>', 'colored text')
    case 'fontSize':
      return wrapSelection(value, sel, `<span style="font-size:${param ?? '1.25em'}">`, '</span>', 'resized text')
    case 'fontFamily':
      return wrapSelection(value, sel, `<span style="font-family:${param ?? 'inherit'}">`, '</span>', 'text')
    case 'align':
      return insertBlock(value, sel, `<div style="text-align:${param ?? 'center'}">\n\ncentered text\n\n</div>`)
    default:
      return { value, selectionStart: sel.start, selectionEnd: sel.end }
  }
}
