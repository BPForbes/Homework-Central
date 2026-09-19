# Homework Central — Design System

This document is the single source of truth for the frontend's visual language: color,
type, spacing, motion, and component conventions. If you're changing anything visual —
a color, an animation, a shadow — it should trace back to a token defined here, not a
one-off hex code in a component.

Implementation lives in `frontend/src/index.css` (`:root` and `:root[data-theme='dark']`
at the top of the file). Everything below the token block styles by existing class name,
not by rewriting component structure — this is a CSS-level design system, not a component
library.

## Why "living water"

Homework Central is one continuous environment: a calm lake by day and the same lake by
moonlight at night. The background, glass surfaces, lighting, interaction feedback, and
theme transition all reinforce that model. The effect must remain subtle enough for daily
school use and inexpensive enough for low-end Chromebooks.

Three principles guide additions:

1. **Content stays above decoration.** Water motion is slow and low contrast. Dense screens
   still rely on typography and spacing rather than adding another card.
2. **One material system.** Sidebars, cards, dialogs, toolbars, bubbles, and inputs are
   translucent surfaces above the same water field, not unrelated visual treatments.
3. **Light reflects; darkness emits.** Light-mode interaction uses reflective elevation.
   Dark-mode interaction uses cyan bloom. Subject colors remain semantic rather than
   decorative.

## Color

All colors are CSS custom properties, themed via `:root` (light) and `:root[data-theme='dark']`.
Never hardcode a hex value in a component; add or reuse a token instead.

### Surfaces
| Token | Light | Dark | Use |
|---|---|---|---|
| `--color-bg` | `#eaf8ff` | `#081521` | Day sky / night sky fallback |
| `--color-bg-elevated` | translucent white-blue | translucent navy | Sticky header, sidebar, composer |
| `--color-surface` | 68% white glass | 72% deep-water glass | Cards, panels, modals |
| `--color-surface-alt` | pale-water glass | lifted navy glass | Inputs, toolbars, secondary surfaces |
| `--color-surface-sunken` | submerged pale blue | deep-water navy | Chat panel and code surfaces |
| `--color-border` | reflective white | moon-cyan at 18% | Default glass edge |
| `--color-border-strong` | deep-water at 32% | moon-cyan at 30% | Inputs and selected edges |

`--glass-blur` and `--glass-saturate` define the shared material. Blur is applied to major
surfaces, not every nested element, to avoid stacking expensive GPU filters.

Interactive rows use `--color-interactive-fill`; chat messages use
`--color-bubble-own` / `--color-interactive-fill`. Menu shells remain translucent so the
water field is visible, while controls inside them remain filled and readable.

### Text
| Token | Light | Dark | Use |
|---|---|---|---|
| `--color-ink` | `#173848` | `#eaf9ff` | Primary text |
| `--color-ink-secondary` | `#456879` | `#afd5e5` | Secondary text, labels |
| `--color-ink-tertiary` | `#688899` | `#7fa5b6` | Placeholder, metadata, timestamps |

### Brand
| Token | Light | Dark | Use |
|---|---|---|---|
| `--color-primary` | `#176f91` | `#1682ad` | Buttons, links, active states — deep water |
| `--color-primary-hover` | `#105a77` | `#49c5ff` | Reflected / emissive interaction color |
| `--color-primary-soft` / `-soft-strong` | tints | tints | Badge backgrounds, own-message bubble fill, focus backgrounds |
| `--color-accent` | `#2e8fb8` | `#63d3ff` | Water accent / moon reflection |
| `--color-on-accent` | white | deep navy | Contrast text on accent fills |

Solid action fills use the deeper primary token for text contrast. Bright cyan is reserved
for dark-mode text accents and bloom rather than large button fills.

### Semantic
`--color-danger` / `--color-danger-hover` / `--color-danger-soft`, `--color-success` /
`--color-success-soft`, `--color-warning` / `--color-warning-soft` — same light/dark
pairing pattern as brand colors. Warning (amber) is reserved for mid-pass reevaluation /
blocking highlights on neural replay paths — not for primary actions.

### Subject/category hues
Four extra hues exist so subjects or room categories can carry a distinct identity beyond
primary/accent: `--hue-violet`, `--hue-coral`, `--hue-sky`, `--hue-plum` (each with a
`-soft` tint). Use these for room icon backgrounds or category accents — never as the
primary action color for something unrelated to a subject.

(Note: an earlier pass used `--hue-olive`, a low-chroma yellow-green — it read murky next
to the other three hues and was replaced with `--hue-plum`.)

### Shadows
Light shadows combine soft blue-gray depth with a bright inset top edge, making glass appear
to reflect daylight and lift toward the viewer. Dark shadows combine normal depth with a
low-opacity cyan outer bloom. `--shadow-hover` and `--shadow-selected` are the canonical
interaction treatments; do not add component-local glow colors.

## Water background

Every page — auth and logged-in alike — sits on an animated "pond" made of fixed,
pointer-transparent layers behind all content:

1. **Day/night bases** (`html::before` / `html::after`, z-index −2): oversized linear
   gradients over the static `--water-day-1…4` / `--water-night-1…4` tokens whose
   compositor-only `transform` drifts slowly (`water-base-drift`), so the water's
   color gradually shifts without repainting the full viewport. Theme changes cross-fade via
   `--water-day-opacity` / `--water-night-opacity` over `--duration-theme`; the hidden
   layer's animation is paused. There are deliberately no repeating ring/line
   overlays — the water stays smooth, and all texture comes from the scene canvas.
2. **Scene canvas** (`.water-scene`, z-index −1):
   `frontend/src/components/background/WaterBackground.tsx` draws the living elements.
   Because every layer is below z-index 0, nothing here can ever cover form inputs
   or content.

Scene elements are **event-based** on a fixed 30 FPS simulation. Blue-noise intervals
(minimum gap plus an exponential tail), density feedback, and bounded burst queues keep
arrivals natural without clustering or exceeding each kind's cap. Spawn positions use
spatial weighting to favor deeper outer water and avoid interactive foreground surfaces.
Lifespans are randomized in frames; expiry tags an entity for a kind-specific
despawn (fish dive, lily pads sink, fog and fireflies fade). A retiring entity is removed
immediately if its center reaches a foreground interaction surface or viewport edge.
Live entities that leave the viewport wrap to the antipodal point. Element inventory:

| Element | Themes | Behavior |
|---|---|---|
| Reflections | both | broad soft light patches, slow drift; additive blend in dark mode |
| Lily pads | both | green (`--water-lily*`), float above the water, slight wobble; shadow carries the same notched silhouette |
| Fish | both | top-down, multi-colored (`--water-fish-a…d`) with spine-highlight shading, pectoral fins, seeded mottling, and a slim swishing caudal blade; each tail stroke thrusts and yaws them (burst-glide) |
| Droplets | both | ripple rings; spawned randomly **and** whenever the API layer sends data (`api/apiActivity.ts`); API ripples avoid the center band where forms live |
| Fog | dark only | large drifting mist banks (`--water-fog`) |
| Fireflies | dark only | sporadic random-walk flight with darts; gold bloom (`--water-firefly`) that flares with speed, additively mixed into the water |

All canvas colors are read at runtime from the `--water-*` tokens (light + dark
variants in `index.css`), so the scene follows the theme with no hardcoded hexes.
`prefers-reduced-motion: reduce` disables the canvas scene entirely and freezes the
CSS layers via the global media query.

Performance rules: the two full-viewport base layers animate only compositor transforms.
The canvas uses a fixed 30 FPS step, an adaptive DPR/pixel budget with entity-count
hysteresis, lookup-table trigonometry, approximate decorative-vector magnitudes, cached
interaction bounds, gradient/fish sprites and geometry, coalesced resize work, and pauses
while the document is hidden. Never place `backdrop-filter` on repeated children such as
chat bubbles, room categories, or list rows — glass blur belongs on major chrome only.

## Typography

`--font-sans` is **Plus Jakarta Sans**, bundled locally via `@fontsource/plus-jakarta-sans`
(weights 400/500/600/700) — no CDN font loading, since this is a self-hosted app. System
font stack (`-apple-system, Segoe UI, Roboto…`) is the fallback. One bundled family only;
resist the urge to add a separate display font for headings.

Headings at ~20px and above (`h2`s across auth, dashboard, chat room, get-roles, server
maintenance) carry `letter-spacing: -0.01em` for a tighter, more considered feel. Do not
apply negative tracking to body text — it hurts readability below ~18px.

## Motion

| Token | Value | Use |
|---|---|---|
| `--duration-instant` | 100ms | Icon rotations, tiny state flips |
| `--duration-fast` | 150ms | Hover/focus states, color transitions |
| `--duration-base` | 220ms | Page-section entrances, button presses |
| `--duration-slow` | 320ms | Sidebar slide, backdrop fades |
| `--duration-slower` | 450ms | Modal/overlay entrances only |
| `--duration-theme` | 700ms | Day/night water and material cross-fade |

| Easing | Curve | Use |
|---|---|---|
| `--ease-out` | `cubic-bezier(0.16, 1, 0.3, 1)` | Default entrance easing — snappy decelerate |
| `--ease-in-out` | `cubic-bezier(0.4, 0, 0.2, 1)` | Symmetric transitions (loaders, color fades) |
| `--ease-spring` | `cubic-bezier(0.34, 1.56, 0.64, 1)` | Button presses, hover lifts — slight overshoot for a tactile "pop" |

Rules of thumb, learned from the Fable 5 review and worth preserving:

- **Animate only what's new.** Chat message history never re-plays an entrance animation on
  room switch — only a message that just arrived gets `.chat-bubble-row--entering`
  (`message-in` keyframe). Entrance-animating a whole scrollback feels like molasses by the
  second time you open the app.
- **Animate `transform`/`opacity` only.** Never animate `width`/`height`/`top`/`left` — it's
  the difference between 60fps and jank on low-end school Chromebooks.
- **Micro vs. macro durations.** 120–220ms for anything the user directly triggers (button
  press, hover). Reserve 300ms+ for backdrops/overlays the user is *watching* appear, not
  clicking through. The 700ms theme transition is the sole exception because it represents
  an environmental lighting change rather than direct control feedback.
- **`prefers-reduced-motion: reduce` collapses, it doesn't remove.** All durations drop to
  ~0 globally (see the media query at the top of `index.css`) so state changes are instant
  rather than animated, but nothing gets stuck mid-transition or invisible.
- **Skeletons only for loads that might exceed ~300ms.** A shimmer on something that
  resolves in 80ms just adds a flash of unnecessary motion.
- **Backend mutation droplet.** User-triggered POST/PUT/PATCH/DELETE requests emit one
  decorative droplet event, coalesced to avoid bursts (`api/apiActivity.ts`). GET polling,
  refresh-token rotation, and SignalR keepalives never trigger it. The ripple is drawn by
  the water scene canvas, in the outer band of the viewport away from form inputs.

## Loading and bottleneck states

The canonical wait indicator is `<LoadingBars />`, implemented at
`frontend/src/components/LoadingBars.tsx:9`. Its bar geometry begins at
`frontend/src/index.css:651` and its shared `backend-bar-loader` keyframes begin at
`frontend/src/index.css:685`. Full-viewport waits compose that same component through
`frontend/src/components/BackendConnectingLoader.tsx:11`; the corresponding wrapper at
`frontend/src/index.css:626` must remain transparent.

- Use the bar indicator whenever a route or page is loading, a content region is waiting
  for data, authentication or permission checks block rendering, a send/save/delete action
  blocks a region, or another visible bottleneck prevents the next interaction. Compact
  navigation sidebars are the exception: use the static `<SidebarSkeleton />` placeholder
  rather than a tall loading animation.
- Keep the loading container transparent. It may reserve space or block interaction while
  consistency is required, but it must not paint an opaque page/card background over the
  living-water environment.
- Supply a specific present-progress message such as “Loading messages…” or “Saving room…”.
  Do not introduce a second spinner or a component-local loader.
- Keep controls that could duplicate the operation disabled for the duration of the wait.
  The bar communicates progress; disabled state preserves request integrity.
- The animation uses only `transform` and `opacity`, inherits the global reduced-motion
  collapse, and must continue using the existing primary color tokens.

## Chat bubbles

The chat bubble component (`ChatMessageBubble.tsx`) was **not** restructured into flat
Discord-style rows in this pass — that's a legitimate longer-term idea (flat rows scale
better for dense threads) but touches reply-threading and swipe-to-reply gesture logic, so
it was out of scope for a CSS-level redesign.

What *did* change, specifically to shed the "generic iMessage clone" look:

- **No tail-corner asymmetry.** The old bubbles squared off one corner (bottom-right for
  your own messages, bottom-left for others) — the single biggest visual tell that reads as
  "copied iMessage." Corners are now uniform (`--radius-lg`, 14px) on every bubble.
- **Own messages are tinted, not solid-filled.** Previously your own messages were solid
  `--color-primary` with white text (the classic blue-bubble/gray-bubble pattern). They now
  use `--color-primary-soft-strong` with `--color-ink` text — tinted-vs-white reads more
  editorial and less like a stock messaging SDK.

## Theming

Light/dark is driven by `data-theme="light"|"dark"` on `<html>`, set by:
- An inline script in `index.html` (reads `localStorage['hc-theme']`, falls back to
  `prefers-color-scheme`) that runs before paint, so there's no flash of the wrong theme.
- `ThemeContext` (`frontend/src/context/ThemeContext.tsx`), which persists the user's
  explicit choice back to `localStorage` and exposes `useTheme()`.
- A `<ThemeToggle />` component in the app header and on the login/register pages.

`ThemeContext` briefly adds `.theme-transitioning` to `<html>` when the user toggles. This
lets theme-dependent backgrounds, borders, text, shadows, glass opacity, and water
reflections interpolate over `--duration-theme`. The class must be removed after the
transition and the reduced-motion rule must continue collapsing it to an instant change.

Theme transitions are explicitly scoped to major surfaces, menus, and interactive controls.
Never restore a universal `.theme-transitioning *` rule: it creates hundreds of simultaneous
paint animations and makes the app visibly lag on chat-heavy screens.

### Theme environments

**Day lake (light):** `#eaf8ff` sky, `#cbefff` surface water, `#7ccde8` primary
water, and `#2e8fb8` deep water. Panels are translucent white with reflective borders.

**Night lake (dark):** `#081521` sky, `#0c2536` surface water, `#041019` deep
water, `#63d3ff` moon reflection, and `#49c5ff` interaction bloom. Avoid pure black.

### Hover and selection

- Light mode: `scale(1.02)`, a 3–5% brightness/saturation lift, and
  `--shadow-hover`. The element reflects light and rises.
- Dark mode: `scale(1.02)`, a small saturation lift, and `--shadow-hover`. The
  element emits a soft cyan bloom.
- Both use `--duration-fast` (150ms). Selected items use `--shadow-selected` and
  a primary edge so their state is not communicated by motion alone.
- Never apply light bloom or a large dark drop shadow. The treatments are complementary,
  not identical.

## Extending this system

- New colors go in the token block, in both the light `:root` and the dark
  `:root[data-theme='dark']` block, following the existing naming pattern
  (`--color-<role>`, `--color-<role>-hover`, `--color-<role>-soft`).
- New components should reach for existing tokens before introducing a new one. If a genuinely
  new semantic role is needed (not just a repaint of an existing role), add it here first.
- Don't hardcode transition timing — reuse a `--duration-*` / `--ease-*` pair so motion stays
  consistent across the app.
