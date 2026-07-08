---
version: alpha
name: Homework Central
description: Collaborative homework help platform — chat-first, trustworthy, and calm.
colors:
  primary: "#3b5bdb"
  on-primary: "#ffffff"
  secondary: "#4b5563"
  on-secondary: "#1a1d2e"
  tertiary: "#3b5bdb"
  on-tertiary: "#ffffff"
  neutral: "#eef0f6"
  surface: "#ffffff"
  on-surface: "#1a1d2e"
  surface-muted: "#f0f2f8"
  border: "rgba(59, 91, 219, 0.12)"
  error: "#e03131"
  on-error: "#ffffff"
  mention: "#3b5bdb"
  success: "#2f9e44"
typography:
  headline-lg:
    fontFamily: Inter
    fontSize: 24px
    fontWeight: 600
    lineHeight: 1.3
  headline-md:
    fontFamily: Inter
    fontSize: 18px
    fontWeight: 600
    lineHeight: 1.35
  body-md:
    fontFamily: Inter
    fontSize: 15px
    fontWeight: 400
    lineHeight: 1.5
  body-sm:
    fontFamily: Inter
    fontSize: 13px
    fontWeight: 400
    lineHeight: 1.45
  label-md:
    fontFamily: Inter
    fontSize: 13px
    fontWeight: 500
    lineHeight: 1.4
  label-mono:
    fontFamily: "JetBrains Mono"
    fontSize: 10px
    fontWeight: 400
    lineHeight: 1.2
rounded:
  sm: 8px
  md: 12px
  lg: 16px
  xl: 20px
  full: 9999px
spacing:
  xs: 4px
  sm: 8px
  md: 16px
  lg: 24px
  xl: 32px
  gutter: 24px
components:
  button-primary:
    backgroundColor: "{colors.primary}"
    textColor: "{colors.on-primary}"
    rounded: "{rounded.md}"
    padding: 12px
    typography: "{typography.label-md}"
  button-primary-hover:
    backgroundColor: "#324cc0"
    textColor: "{colors.on-primary}"
  button-secondary:
    backgroundColor: "{colors.surface}"
    textColor: "{colors.on-surface}"
    rounded: "{rounded.md}"
    padding: 10px
  button-ghost:
    backgroundColor: transparent
    textColor: "{colors.secondary}"
    rounded: "{rounded.md}"
    padding: 8px
  input-field:
    backgroundColor: "{colors.surface}"
    textColor: "{colors.on-surface}"
    rounded: "{rounded.md}"
    padding: 12px
    typography: "{typography.body-md}"
  card:
    backgroundColor: "{colors.surface}"
    rounded: "{rounded.lg}"
    padding: 24px
  chat-bubble-own:
    backgroundColor: "{colors.primary}"
    textColor: "{colors.on-primary}"
    rounded: "{rounded.xl}"
    padding: 12px
  chat-bubble-other:
    backgroundColor: "{colors.surface}"
    textColor: "{colors.on-surface}"
    rounded: "{rounded.xl}"
    padding: 12px
  nav-tab-active:
    backgroundColor: "#e8ecf8"
    textColor: "{colors.primary}"
    rounded: "{rounded.md}"
    padding: 8px
  nav-tab-inactive:
    backgroundColor: transparent
    textColor: "{colors.secondary}"
    rounded: "{rounded.md}"
    padding: 8px
  sidebar-channel-active:
    backgroundColor: "rgba(232, 236, 248, 0.7)"
    textColor: "{colors.primary}"
  role-tile:
    backgroundColor: "{colors.surface}"
    textColor: "{colors.on-surface}"
    rounded: "{rounded.lg}"
    padding: 24px
  role-tile-active:
    backgroundColor: "rgba(59, 91, 219, 0.08)"
    textColor: "{colors.primary}"
---

## Overview

Homework Central is a collaborative learning platform centered on real-time chat. The visual identity should feel **calm, focused, and trustworthy** — like a well-lit study lounge, not a noisy social feed. Density is moderate: enough information to support multi-channel navigation without crowding the conversation.

The UI is **chat-first**: a persistent channel sidebar, a slim top navigation bar for cross-cutting areas (Inbox, admin tools), and a message canvas that dominates the viewport. Auth screens are minimal and centered. Administrative surfaces use cards on the neutral canvas.

## Colors

The palette is built around **Scholar Blue** as the single interaction driver, with cool neutrals for structure.

- **Primary / Scholar Blue (#3b5bdb):** Primary actions, active navigation, own-message bubbles, links, and mention highlights. Use for one dominant CTA per view.
- **On-primary (#ffffff):** Text and icons on primary-filled surfaces.
- **Secondary (#6b7280):** Metadata, subtitles, inactive nav labels, timestamps on light surfaces.
- **Neutral (#eef0f6):** App background — a soft blue-gray that separates cards without harsh contrast.
- **Surface (#ffffff):** Cards, sidebars, message bubbles from others, input fields.
- **Surface-muted (#f0f2f8):** Sidebar group headers, hover states, reply-quote backgrounds.
- **Border (rgba(59, 91, 219, 0.12)):** Dividers and input outlines; tinted to match primary.
- **Error (#e03131):** Validation errors and destructive confirmations only.

## Typography

**Inter** carries all UI prose. **JetBrains Mono** is reserved for UTC timestamps in chat.

- **Headlines:** Inter Semi-Bold (600) for page titles and channel names.
- **Body:** Inter Regular at 15px base size for forms, messages, and descriptions.
- **Labels:** Inter Medium (500) for field labels, nav tabs, and buttons.
- **Timestamps:** JetBrains Mono 10px in muted secondary color.

## Layout

Follow an **8px spacing scale** with a 4px half-step for tight inline gaps.

- App shell: full viewport height (`100dvh`), no document scroll on chat routes.
- Top nav: fixed 48px (`h-12`), horizontal padding 20px.
- Chat sidebar: fixed 288px (`w-72`), scrollable channel list.
- Message area: flex-1, messages scroll independently; composer pinned to bottom.
- Non-chat pages: max content width ~960px with 24px padding.
- Channel groups in the sidebar use rounded containers with internal padding 12px.

## Elevation & Depth

Depth is conveyed through **tonal layers**, not heavy drop shadows.

1. Neutral background (`#eef0f6`)
2. White cards and sidebar (`#ffffff`)
3. Subtle borders (`border-border`) instead of box-shadow for separation
4. Hover states lighten to `surface-muted`
5. Reserve shadow-sm only for mention autocomplete popovers and modals

## Shapes

Rounded corners signal friendliness while keeping a structured feel.

- **sm (8px):** Icon buttons, small chips
- **md (12px):** Buttons, inputs, nav tabs
- **lg (16px):** Sidebar group containers, cards
- **xl (20px):** Message bubbles, composer container
- **full:** Avatars, send button, pill badges

## Components

### Buttons
Primary buttons use Scholar Blue fill with white label. Secondary buttons are white with a border. Ghost buttons have no fill — for toolbar icon actions and "Sign out".

### Navigation
Top tabs use `nav-tab-active` / `nav-tab-inactive` tokens. Active tab gets secondary-tinted background (`#e8ecf8`) and primary text.

### Chat sidebar
Channel groups collapse with chevron. Active channel row uses `sidebar-channel-active`. Category headers sit on `surface-muted` with a light border.

### Message bubbles
Own messages: primary fill, white text, `rounded-xl` with a slightly sharper bottom-right corner. Others: white fill, border, sender name in role color above content. Reply quotes appear as a separate pill above the bubble.

### Composer
White rounded container with border; primary circular send button. Mention autocomplete is a white floating card with shadow.

### Auth cards
Centered white card, `rounded-lg`, 24px padding, max-width 420px on neutral background.

### Role claim grid
Responsive grid of tappable tiles. Active tile uses primary-tinted background and primary border.

## Do's and Don'ts

- Do use Scholar Blue for exactly one primary action per screen region.
- Do keep chat routes full-height with the sidebar always visible.
- Do use Inter for all UI text; reserve JetBrains Mono for UTC timestamps only.
- Do maintain WCAG AA contrast (4.5:1) for body text on all surfaces.
- Don't use heavy box shadows on cards — prefer borders and tonal contrast.
- Don't mix Font Awesome and Lucide icons in the same view; Lucide is the standard going forward.
- Don't use pure `#000` or pure `#fff` page backgrounds — use `neutral` and `surface` tokens.
- Don't add gradients to buttons except where legacy captcha widgets require them.
