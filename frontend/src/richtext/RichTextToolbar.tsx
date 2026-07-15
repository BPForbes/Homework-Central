import type { RefObject } from 'react'
import { FontAwesomeIcon } from '@fortawesome/react-fontawesome'
import {
  faBold,
  faCode,
  faFileCode,
  faHeading,
  faHighlighter,
  faImage,
  faItalic,
  faLink,
  faListCheck,
  faListOl,
  faListUl,
  faQuoteLeft,
  faSquareRootVariable,
  faStrikethrough,
  faSubscript,
  faSuperscript,
  faTable,
  faUnderline,
} from '@fortawesome/free-solid-svg-icons'
import { applyToolbarAction, type TextSelection, type ToolbarActionId } from './toolbarActions'

const TEXT_COLOR_SWATCHES = ['#e63946', '#f3722c', '#f9c74f', '#43aa8b', '#277da1', '#8338ec', '#2b2118']
const FONT_SIZE_OPTIONS: { label: string; value: string }[] = [
  { label: 'Small', value: '0.85em' },
  { label: 'Normal', value: '1em' },
  { label: 'Large', value: '1.25em' },
  { label: 'Huge', value: '1.75em' },
]
const FONT_FAMILY_OPTIONS: { label: string; value: string }[] = [
  { label: 'Default', value: 'inherit' },
  { label: 'Serif', value: 'Georgia, "Times New Roman", serif' },
  { label: 'Monospace', value: '"Fira Code", ui-monospace, monospace' },
  { label: 'Rounded', value: '"Comic Sans MS", "Comic Sans", cursive' },
]

interface RichTextToolbarProps {
  textareaRef: RefObject<HTMLTextAreaElement | null>
  value: string
  onChange: (next: string) => void
  compact?: boolean
}

/**
 * Formatting toolbar shared by the chat composer and the info-entry editor. Inserts Markdown (or
 * a small inline-HTML span for color/size/family, since CommonMark has no native syntax for
 * those) at the caller's textarea cursor/selection — it never renders anything itself.
 */
export function RichTextToolbar({ textareaRef, value, onChange, compact = false }: RichTextToolbarProps) {
  function currentSelection(): TextSelection {
    const el = textareaRef.current
    if (!el) return { start: value.length, end: value.length }
    return { start: el.selectionStart ?? value.length, end: el.selectionEnd ?? value.length }
  }

  function run(id: ToolbarActionId, param?: string) {
    const sel = currentSelection()
    const result = applyToolbarAction(id, value, sel, param)
    onChange(result.value)
    window.requestAnimationFrame(() => {
      const el = textareaRef.current
      if (el?.getAttribute('aria-hidden') !== 'true')
        el?.focus()
      el?.setSelectionRange(result.selectionStart, result.selectionEnd)
    })
  }

  return (
    <div className={`rich-toolbar ${compact ? 'rich-toolbar--compact' : ''}`} role="toolbar" aria-label="Formatting">
      <button type="button" className="rich-toolbar-btn" title="Bold" onClick={() => run('bold')}>
        <FontAwesomeIcon icon={faBold} />
      </button>
      <button type="button" className="rich-toolbar-btn" title="Italic" onClick={() => run('italic')}>
        <FontAwesomeIcon icon={faItalic} />
      </button>
      <button type="button" className="rich-toolbar-btn" title="Underline" onClick={() => run('underline')}>
        <FontAwesomeIcon icon={faUnderline} />
      </button>
      <button type="button" className="rich-toolbar-btn" title="Strikethrough" onClick={() => run('strikethrough')}>
        <FontAwesomeIcon icon={faStrikethrough} />
      </button>
      <button type="button" className="rich-toolbar-btn" title="Highlight" onClick={() => run('highlight')}>
        <FontAwesomeIcon icon={faHighlighter} />
      </button>
      <button type="button" className="rich-toolbar-btn" title="Subscript" onClick={() => run('sub')}>
        <FontAwesomeIcon icon={faSubscript} />
      </button>
      <button type="button" className="rich-toolbar-btn" title="Superscript" onClick={() => run('sup')}>
        <FontAwesomeIcon icon={faSuperscript} />
      </button>

      {!compact && (
        <>
          <span className="rich-toolbar-sep" aria-hidden="true" />
          <button type="button" className="rich-toolbar-btn" title="Heading" onClick={() => run('h2')}>
            <FontAwesomeIcon icon={faHeading} />
          </button>
          <button type="button" className="rich-toolbar-btn" title="Bulleted list" onClick={() => run('ul')}>
            <FontAwesomeIcon icon={faListUl} />
          </button>
          <button type="button" className="rich-toolbar-btn" title="Numbered list" onClick={() => run('ol')}>
            <FontAwesomeIcon icon={faListOl} />
          </button>
          <button type="button" className="rich-toolbar-btn" title="Checklist" onClick={() => run('checklist')}>
            <FontAwesomeIcon icon={faListCheck} />
          </button>
          <button type="button" className="rich-toolbar-btn" title="Quote" onClick={() => run('quote')}>
            <FontAwesomeIcon icon={faQuoteLeft} />
          </button>
          <button type="button" className="rich-toolbar-btn" title="Table" onClick={() => run('table')}>
            <FontAwesomeIcon icon={faTable} />
          </button>
        </>
      )}

      <span className="rich-toolbar-sep" aria-hidden="true" />
      <button type="button" className="rich-toolbar-btn" title="Link" onClick={() => run('link')}>
        <FontAwesomeIcon icon={faLink} />
      </button>
      {!compact && (
        <button type="button" className="rich-toolbar-btn" title="Image" onClick={() => run('image')}>
          <FontAwesomeIcon icon={faImage} />
        </button>
      )}
      <button type="button" className="rich-toolbar-btn" title="Inline code" onClick={() => run('inlineCode')}>
        <FontAwesomeIcon icon={faCode} />
      </button>
      <button type="button" className="rich-toolbar-btn" title="Code block" onClick={() => run('codeBlock')}>
        <FontAwesomeIcon icon={faFileCode} />
      </button>
      <button type="button" className="rich-toolbar-btn" title="Inline math ($…$)" onClick={() => run('inlineMath')}>
        <FontAwesomeIcon icon={faSquareRootVariable} />
      </button>
      <button type="button" className="rich-toolbar-btn" title="Display math ($$…$$)" onClick={() => run('displayMath')}>
        <span className="rich-toolbar-text-btn">Σ</span>
      </button>

      <span className="rich-toolbar-sep" aria-hidden="true" />
      <label className="rich-toolbar-select-wrap" title="Font size">
        <select
          className="rich-toolbar-select"
          defaultValue=""
          onChange={(e) => {
            if (!e.target.value) return
            run('fontSize', e.target.value)
            e.target.value = ''
          }}
        >
          <option value="" disabled>Size</option>
          {FONT_SIZE_OPTIONS.map((opt) => (
            <option key={opt.value} value={opt.value}>{opt.label}</option>
          ))}
        </select>
      </label>
      {!compact && (
        <label className="rich-toolbar-select-wrap" title="Font family">
          <select
            className="rich-toolbar-select"
            defaultValue=""
            onChange={(e) => {
              if (!e.target.value) return
              run('fontFamily', e.target.value)
              e.target.value = ''
            }}
          >
            <option value="" disabled>Font</option>
            {FONT_FAMILY_OPTIONS.map((opt) => (
              <option key={opt.value} value={opt.value}>{opt.label}</option>
            ))}
          </select>
        </label>
      )}
      <div className="rich-toolbar-colors" role="group" aria-label="Text color">
        {TEXT_COLOR_SWATCHES.map((color) => (
          <button
            key={color}
            type="button"
            className="rich-toolbar-color"
            style={{ backgroundColor: color }}
            title={`Color text ${color}`}
            onClick={() => run('textColor', color)}
          />
        ))}
      </div>
    </div>
  )
}
