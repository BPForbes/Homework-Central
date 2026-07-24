export type ReplayNode = { index: number; nodeId: string; layerId: string; label: string }
export type ReplayEdge = { index: number; sourceNodeIndex: number; targetNodeIndex: number; parameterIndex: number }
/** `layerIndex` is the destination layer of a one-layer step; absent on whole-network frames. */
export type ReplayFrame = { sequence: number; phase: string; ticketIndex: number; passIndex: number; messageIndex?: number; epoch?: number; payloadKind: string; payloadIndex: number; layerIndex?: number | null }
export type NeuralNetReplay = {
  schemaVersion: string
  sessionId: string
  completionStatus: string
  topology: { nodes: ReplayNode[]; edges: ReplayEdge[] }
  frames: ReplayFrame[]
  tickets: { ticketIndex: number }[]
  integrity?: { reportChecksum?: string }
  payloads?: Record<string, unknown[]>
}
