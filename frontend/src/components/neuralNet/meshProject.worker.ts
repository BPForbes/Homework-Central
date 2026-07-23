/** Off-main-thread mesh build + projection so slice/volume orbit stays responsive. */

export type MeshViewMode = 'volume' | 'slice'
export type SlicePerspective = 'top' | 'side'
export type MeshPathTone = 'forward' | 'reeval' | 'backprop' | 'accepted' | 'revision' | 'idle'

export type MeshProjectRequest = {
  requestId: number
  layerWidths: number[]
  layerLabels: string[]
  pathTone: MeshPathTone
  activeNodeIndexes: number[]
  activeEdgeKeys: string[]
  viewMode: MeshViewMode
  slicePerspective: SlicePerspective
  sliceIndex: number
  yaw: number
  pitch: number
  zoom: number
  width: number
  height: number
  nodeGap: number
  layerGap: number
  colors: {
    surface: string
    border: string
    borderStrong: string
    inkSecondary: string
    success: string
    warning: string
    danger: string
    primary: string
    surfaceAlt: string
  }
}

export type MeshDrawCommand =
  | {
      kind: 'edge'
      x1: number
      y1: number
      x2: number
      y2: number
      cpx: number
      cpy: number
      color: string
      alpha: number
      width: number
      dashed: boolean
    }
  | {
      kind: 'node'
      x: number
      y: number
      radius: number
      color: string
      alpha: number
      stroke: string
    }
  | {
      kind: 'label'
      x: number
      y: number
      text: string
      color: string
    }
  | {
      kind: 'grid'
      x1: number
      y1: number
      x2: number
      y2: number
      color: string
      alpha: number
    }

export type MeshProjectResponse = {
  requestId: number
  commands: MeshDrawCommand[]
  hud: string
}

type Point3 = { x: number; y: number; z: number }
type Node3 = Point3 & { index: number; layer: number; local: number }
type Edge3 = { key: string; layer: number; source: number; target: number; a: number; b: number }

function buildMesh(layerWidths: number[], nodeGap: number, layerGap: number): { nodes: Node3[]; edges: Edge3[] } {
  const nodes: Node3[] = []
  let global = 0
  for (let layer = 0; layer < layerWidths.length; layer++) {
    const count = Math.max(0, layerWidths[layer] ?? 0)
    const columns = Math.max(1, Math.ceil(Math.sqrt(count)))
    const rows = Math.max(1, Math.ceil(count / columns))
    const x = layer * layerGap
    for (let local = 0; local < count; local++) {
      const col = local % columns
      const row = Math.floor(local / columns)
      nodes.push({
        index: global,
        layer,
        local,
        x,
        y: (col - (columns - 1) / 2) * nodeGap,
        z: (row - (rows - 1) / 2) * nodeGap,
      })
      global += 1
    }
  }

  const edges: Edge3[] = []
  let offset = 0
  for (let layer = 0; layer < layerWidths.length - 1; layer++) {
    const sourceCount = layerWidths[layer]
    const targetCount = layerWidths[layer + 1]
    const sourceOffset = offset
    const targetOffset = offset + sourceCount
    for (let source = 0; source < sourceCount; source++) {
      for (let target = 0; target < targetCount; target++) {
        edges.push({
          key: `${layer}:${source}:${target}`,
          layer,
          source,
          target,
          a: sourceOffset + source,
          b: targetOffset + target,
        })
      }
    }
    offset += sourceCount
  }

  return { nodes, edges }
}

function project(
  point: Point3,
  yaw: number,
  pitch: number,
  zoom: number,
  width: number,
  height: number,
): { x: number; y: number; depth: number } {
  const cosY = Math.cos(yaw)
  const sinY = Math.sin(yaw)
  const cosP = Math.cos(pitch)
  const sinP = Math.sin(pitch)
  const x1 = point.x * cosY - point.z * sinY
  const z1 = point.x * sinY + point.z * cosY
  const y1 = point.y * cosP - z1 * sinP
  const z2 = point.y * sinP + z1 * cosP
  const perspective = 700
  const scale = (perspective / (perspective + z2 + 220)) * zoom
  return {
    x: width / 2 + x1 * scale,
    y: height / 2 + y1 * scale,
    depth: z2,
  }
}

function toneStroke(
  tone: MeshPathTone,
  active: boolean,
  colors: MeshProjectRequest['colors'],
): { color: string; alpha: number; width: number } {
  if (!active || tone === 'idle') {
    return { color: colors.primary, alpha: 0.08, width: 0.8 }
  }
  if (tone === 'forward' || tone === 'accepted') {
    return { color: colors.success, alpha: 0.95, width: 2.2 }
  }
  if (tone === 'reeval') {
    return { color: colors.warning, alpha: 0.95, width: 2.2 }
  }
  if (tone === 'backprop' || tone === 'revision') {
    return { color: colors.danger, alpha: 0.92, width: 2.1 }
  }
  return { color: colors.primary, alpha: 0.2, width: 1 }
}

function toneFill(
  tone: MeshPathTone,
  active: boolean,
  colors: MeshProjectRequest['colors'],
): { color: string; alpha: number } {
  if (!active || tone === 'idle') {
    return { color: colors.surfaceAlt, alpha: 0.55 }
  }
  if (tone === 'forward' || tone === 'accepted') {
    return { color: colors.success, alpha: 0.95 }
  }
  if (tone === 'reeval') {
    return { color: colors.warning, alpha: 0.95 }
  }
  if (tone === 'backprop' || tone === 'revision') {
    return { color: colors.danger, alpha: 0.95 }
  }
  return { color: colors.primary, alpha: 0.7 }
}

function cameraForView(
  viewMode: MeshViewMode,
  slicePerspective: SlicePerspective,
  yaw: number,
  pitch: number,
): { yaw: number; pitch: number } {
  if (viewMode !== 'slice') {
    return { yaw, pitch }
  }
  if (slicePerspective === 'top') {
    // Look straight down the layer lattice (YZ plane).
    return { yaw: 0, pitch: -1.2 }
  }
  // Side: keep user yaw for slight orbit, flatten pitch toward a layer cross-section.
  return { yaw, pitch: Math.max(-0.15, Math.min(0.35, pitch * 0.25)) }
}

self.onmessage = (event: MessageEvent<MeshProjectRequest>) => {
  const request = event.data
  if (!request?.layerWidths?.length) {
    return
  }

  const { nodes, edges } = buildMesh(request.layerWidths, request.nodeGap, request.layerGap)
  const maxX = Math.max(0, (request.layerWidths.length - 1) * request.layerGap)
  const activeNodes = new Set(request.activeNodeIndexes)
  const activeEdges = new Set(request.activeEdgeKeys)
  const hasEdgeSelection = activeEdges.size > 0
  const sliceMode = request.viewMode === 'slice'
  const sliceIndex = Math.max(0, Math.min(request.sliceIndex, request.layerWidths.length - 1))
  const camera = cameraForView(request.viewMode, request.slicePerspective, request.yaw, request.pitch)

  const commands: MeshDrawCommand[] = []
  commands.push({
    kind: 'grid',
    x1: 0,
    y1: 0,
    x2: request.width,
    y2: request.height,
    color: request.colors.surface,
    alpha: 1,
  })

  if (!sliceMode) {
    for (let grid = -4; grid <= 4; grid++) {
      const left = project(
        { x: -maxX / 2 - 40, y: 140, z: grid * request.nodeGap },
        camera.yaw,
        camera.pitch,
        request.zoom,
        request.width,
        request.height,
      )
      const right = project(
        { x: maxX / 2 + 40, y: 140, z: grid * request.nodeGap },
        camera.yaw,
        camera.pitch,
        request.zoom,
        request.width,
        request.height,
      )
      commands.push({
        kind: 'grid',
        x1: left.x,
        y1: left.y,
        x2: right.x,
        y2: right.y,
        color: request.colors.border,
        alpha: 0.25,
      })
    }
  }

  const screenByIndex = new Map<number, { x: number; y: number; depth: number }>()
  const visibleNodes: Array<{ node: Node3; screen: { x: number; y: number; depth: number } }> = []
  for (const node of nodes) {
    if (sliceMode && node.layer !== sliceIndex) {
      continue
    }
    const world = { ...node, x: node.x - maxX / 2 }
    const screen = project(world, camera.yaw, camera.pitch, request.zoom, request.width, request.height)
    screenByIndex.set(node.index, screen)
    visibleNodes.push({ node, screen })
  }

  const idleStride = edges.length > 8000 ? 7 : edges.length > 3000 ? 3 : 1
  for (let index = 0; index < edges.length; index++) {
    const edge = edges[index]
    const touchesSlice = !sliceMode || edge.layer === sliceIndex || edge.layer + 1 === sliceIndex
    if (!touchesSlice) {
      continue
    }

    // Slice view keeps edges that land on the selected layer; neighbor stubs stay when one end is on-slice.
    const onPath = !hasEdgeSelection || activeEdges.has(edge.key)
    if (!onPath && index % idleStride !== 0) {
      continue
    }

    let from = screenByIndex.get(edge.a)
    let to = screenByIndex.get(edge.b)
    if (!from || !to) {
      const fromNode = nodes[edge.a]
      const toNode = nodes[edge.b]
      if (!from && fromNode) {
        from = project(
          { x: fromNode.x - maxX / 2, y: fromNode.y, z: fromNode.z },
          camera.yaw,
          camera.pitch,
          request.zoom,
          request.width,
          request.height,
        )
      }
      if (!to && toNode) {
        to = project(
          { x: toNode.x - maxX / 2, y: toNode.y, z: toNode.z },
          camera.yaw,
          camera.pitch,
          request.zoom,
          request.width,
          request.height,
        )
      }
    }
    if (!from || !to) {
      continue
    }

    // In pure top-down slice, drop off-layer stubs so the plane stays one layer.
    if (sliceMode && request.slicePerspective === 'top') {
      const fromOnSlice = nodes[edge.a]?.layer === sliceIndex
      const toOnSlice = nodes[edge.b]?.layer === sliceIndex
      if (!(fromOnSlice && toOnSlice)) {
        continue
      }
    }

    const style = toneStroke(request.pathTone, onPath && request.pathTone !== 'idle', request.colors)
    const midX = (from.x + to.x) / 2
    const bend = ((edge.source + edge.target) % 7 - 3) * 2.2
    commands.push({
      kind: 'edge',
      x1: from.x,
      y1: from.y,
      x2: to.x,
      y2: to.y,
      cpx: midX,
      cpy: (from.y + to.y) / 2 + bend,
      color: style.color,
      alpha: style.alpha,
      width: style.width,
      dashed: request.pathTone === 'backprop' && onPath,
    })
  }

  visibleNodes.sort((left, right) => right.screen.depth - left.screen.depth)
  for (const item of visibleNodes) {
    const active = activeNodes.size === 0 ? request.pathTone !== 'idle' : activeNodes.has(item.node.index)
    const style = toneFill(request.pathTone, active, request.colors)
    const radius =
      item.node.layer === 0 ? 3.4 : item.node.layer === request.layerWidths.length - 1 ? 4.4 : 3.9
    commands.push({
      kind: 'node',
      x: item.screen.x,
      y: item.screen.y,
      radius,
      color: style.color,
      alpha: style.alpha,
      stroke: request.colors.borderStrong,
    })
  }

  if (!sliceMode && request.layerLabels.length > 0) {
    for (let layer = 0; layer < request.layerWidths.length; layer++) {
      const label = request.layerLabels[layer] ?? `layer ${layer}`
      const anchor = project(
        { x: layer * request.layerGap - maxX / 2, y: -160, z: 0 },
        camera.yaw,
        camera.pitch,
        request.zoom,
        request.width,
        request.height,
      )
      commands.push({
        kind: 'label',
        x: anchor.x,
        y: anchor.y,
        text: label.replace(/-/g, ' '),
        color: request.colors.inkSecondary,
      })
    }
  }

  const nodeCount = request.layerWidths.reduce((sum, width) => sum + width, 0)
  const edgeCount = request.layerWidths
    .slice(0, -1)
    .reduce((sum, width, index) => sum + width * (request.layerWidths[index + 1] ?? 0), 0)
  const sliceLabel = request.layerLabels[sliceIndex] ?? `layer ${sliceIndex}`
  const hud = sliceMode
    ? `Slice ${sliceIndex + 1}/${request.layerWidths.length} (${sliceLabel}) · ${request.slicePerspective} · ${visibleNodes.length} nodes`
    : `${request.layerWidths.join(' → ')} · ${nodeCount} nodes · ${edgeCount} edges`

  const response: MeshProjectResponse = {
    requestId: request.requestId,
    commands,
    hud,
  }
  self.postMessage(response)
}
