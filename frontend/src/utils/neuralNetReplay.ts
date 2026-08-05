import type { NeuralNetReplay, ReplayFrame } from '../types/neuralNetReplay'

// Dense cascade scorers (moderation ≈ 21k edges) plus full training traces need room.
const maximumBytes = 64 * 1024 * 1024
const maximumFrames = 100_000
const maximumNodes = 2048
const maximumEdges = 65536

/** Matches backend ReplayPhase declaration order for older numeric-enum downloads. */
const REPLAY_PHASES = [
  'Llm1Input',
  'InitialForward',
  'Llm2Evaluation',
  'VoteResolution',
  'EpochForward',
  'LossCalculation',
  'BackwardPropagation',
  'ParameterUpdate',
  'PostUpdateForward',
  'FinalVerdict',
] as const

export type ReplayPhaseName = (typeof REPLAY_PHASES)[number]

const PHASE_BY_NAME = new Map<string, ReplayPhaseName>(
  REPLAY_PHASES.map((phase) => [phase.toLowerCase(), phase]),
)

export function normalizeReplayPhase(phase: string | number | undefined | null): ReplayPhaseName | string {
  if (phase === undefined || phase === null) return ''
  if (typeof phase === 'number') {
    if (!Number.isInteger(phase) || phase < 0 || phase >= REPLAY_PHASES.length) return String(phase)
    return REPLAY_PHASES[phase]
  }
  const asString = String(phase).trim()
  if (!asString) return ''
  if (/^\d+$/.test(asString)) {
    const index = Number(asString)
    if (index < 0 || index >= REPLAY_PHASES.length) return asString
    return REPLAY_PHASES[index]
  }
  return PHASE_BY_NAME.get(asString.toLowerCase()) ?? asString
}

export function payloadCollectionForPhase(phase: string | number | undefined | null): string | undefined {
  const normalized = normalizeReplayPhase(phase)
  switch (normalized) {
    case 'Llm1Input':
      return 'inputs'
    case 'InitialForward':
    case 'EpochForward':
    case 'PostUpdateForward':
      return 'forwardPasses'
    case 'Llm2Evaluation':
      return 'evaluations'
    case 'VoteResolution':
      return 'voteSampling'
    case 'LossCalculation':
      return 'losses'
    case 'BackwardPropagation':
      return 'backpropagations'
    case 'ParameterUpdate':
      return 'parameterUpdates'
    case 'FinalVerdict':
      return 'finalVerdicts'
    default:
      return undefined
  }
}

function normalizeFrames(frames: ReplayFrame[]): ReplayFrame[] {
  return frames.map((frame) => ({
    ...frame,
    phase: normalizeReplayPhase(frame.phase as string | number),
  }))
}

export function parseReplayImport(text: string): NeuralNetReplay {
  if (text.length > maximumBytes) throw new Error('The replay file is too large.')
  const parsed = JSON.parse(text) as NeuralNetReplay
  if (parsed.schemaVersion !== '2.0' || !parsed.topology || !Array.isArray(parsed.frames)) {
    throw new Error('Unsupported replay schema.')
  }
  if (
    parsed.topology.nodes.length > maximumNodes ||
    parsed.topology.edges.length > maximumEdges ||
    parsed.frames.length > maximumFrames
  ) {
    throw new Error('Replay exceeds supported limits.')
  }
  const nodeIds = new Set(parsed.topology.nodes.map((node) => node.index))
  if (
    nodeIds.size !== parsed.topology.nodes.length ||
    parsed.topology.edges.some(
      (edge) => !nodeIds.has(edge.sourceNodeIndex) || !nodeIds.has(edge.targetNodeIndex),
    )
  ) {
    throw new Error('Replay topology references are invalid.')
  }
  return {
    ...parsed,
    frames: normalizeFrames(parsed.frames),
  }
}
