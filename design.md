# Homework Central — Design System

This document is the single source of truth for the frontend's visual language: color,
type, spacing, motion, and component conventions. If you're changing anything visual —
a color, an animation, a shadow — it should trace back to a token defined here, not a
one-off hex code in a component.

Implementation lives in `frontend/src/index.css` (`:root` and `:root[data-theme='dark']`
at the top of the file). Everything below the token block styles by existing class name,
not by rewriting component structure — this is a CSS-level design system, not a component
library.

## Why "warm academic"

The previous design was a generic blue-on-slate SaaS look — competent, but indistinguishable
from thousands of other admin dashboards. It's also the wrong emotional register for a tool
students and teachers open every day: cold and corporate where it should feel more like a
well-made notebook.

The direction here — cream/paper neutrals, a deep teal primary, a warm amber accent, warm
(not blue-tinted) dark mode — was chosen and pressure-tested with Fable 5 across two rounds
of critique before implementation. Two principles came directly out of that process and
should guide any future additions:

1. **Density over decoration.** A considered app is tighter than the instinct suggests —
   AI-generated redesigns default to 16px padding and a card around everything. Prefer
   typographic hierarchy and whitespace discipline over adding another bordered box.
2. **Subject color should be semantic, not decorative.** The hue tokens below exist so
   subjects/categories can carry a consistent identity (a room's accent color, an icon
   tint) — not to be sprinkled in for variety.

## Color

All colors are CSS custom properties, themed via `:root` (light) and `:root[data-theme='dark']`.
Never hardcode a hex value in a component; add or reuse a token instead.

### Surfaces
| Token | Light | Dark | Use |
|---|---|---|---|
| `--color-bg` | `#faf8f4` | `#1b1916` | Page background |
| `--color-bg-elevated` | `#fffdf9` | `#211e1a` | Sticky header, composer bar |
| `--color-surface` | `#ffffff` | `#262320` | Cards, panels, modals |
| `--color-surface-alt` | `#f4f1e9` | `#2f2b25` | Secondary surface, input fills, hover backgrounds |
| `--color-surface-sunken` | `#f1ece1` | `#17150f` | Chat message panel background |
| `--color-border` | `#e5dfd0` | `#3c362d` | Default hairline border |
| `--color-border-strong` | `#d3cab5` | `#4c443a` | Input borders, dividers that need more definition |

### Text
| Token | Light | Dark | Use |
|---|---|---|---|
| `--color-ink` | `#2b2620` | `#f2ede4` | Primary text |
| `--color-ink-secondary` | `#6b6255` | `#b7ad9c` | Secondary text, labels |
| `--color-ink-tertiary` | `#948a7a` | `#8a8071` | Placeholder, metadata, timestamps |

### Brand
| Token | Light | Dark | Use |
|---|---|---|---|
| `--color-primary` | `#0d7a6a` | `#1f8f79` | Buttons, links, active states — deep teal |
| `--color-primary-hover` | `#0a6357` | `#24a68d` | Hover/active state of the above |
| `--color-primary-soft` / `-soft-strong` | tints | tints | Badge backgrounds, own-message bubble fill, focus backgrounds |
| `--color-accent` | `#e0942e` | `#d99a3d` | Amber — badges, secondary CTAs, unread indicators |
| `--color-on-accent` | `#4a2f0d` | `#2b1c05` | Text color used **on top of** accent/accent-soft — amber is a light/mid color, it always pairs with dark text, never white |

The dark-mode primary is deliberately *not* a bright lifted teal — it's tuned dark enough
that white button text stays legible. A separate "bright teal for text-on-dark-background"
token was considered and rejected as unnecessary complexity; if you need a brighter teal for
an icon/accent on a dark surface, that's a sign the surface should probably use
`--color-primary-soft` instead.

### Semantic
`--color-danger` / `--color-danger-hover` / `--color-danger-soft`, `--color-success` /
`--color-success-soft` — same light/dark pairing pattern as brand colors.

### Subject/category hues
Four extra hues exist so subjects or room categories can carry a distinct identity beyond
primary/accent: `--hue-violet`, `--hue-coral`, `--hue-sky`, `--hue-plum` (each with a
`-soft` tint). Use these for room icon backgrounds or category accents — never as the
primary action color for something unrelated to a subject.

(Note: an earlier pass used `--hue-olive`, a low-chroma yellow-green — it read murky next
to the other three hues and was replaced with `--hue-plum`.)

### Shadows
Shadows are tinted with the ink color's RGB, not neutral black/slate — `rgba(43, 38, 32, x)`
in light mode. `--shadow-sm` and `--shadow-md` also carry a 1px inset white hairline
(`inset 0 1px 0 rgba(255,255,255,0.5)`) on top of cream surfaces — a cheap way to make a
card read like a cut paper edge rather than a flat rectangle. Dark mode shadows drop the
inset and use plain black at higher opacity, since the highlight trick only works against
a light surface.

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
  clicking through.
- **`prefers-reduced-motion: reduce` collapses, it doesn't remove.** All durations drop to
  ~0 globally (see the media query at the top of `index.css`) so state changes are instant
  rather than animated, but nothing gets stuck mid-transition or invisible.
- **Skeletons only for loads that might exceed ~300ms.** A shimmer on something that
  resolves in 80ms just adds a flash of unnecessary motion.

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

## Extending this system

- New colors go in the token block, in both the light `:root` and the dark
  `:root[data-theme='dark']` block, following the existing naming pattern
  (`--color-<role>`, `--color-<role>-hover`, `--color-<role>-soft`).
- New components should reach for existing tokens before introducing a new one. If a genuinely
  new semantic role is needed (not just a repaint of an existing role), add it here first.
- Don't hardcode transition timing — reuse a `--duration-*` / `--ease-*` pair so motion stays
  consistent across the app.
