#!/bin/sh
# Starts Ollama, waits until the CLI can talk to the server, then ensures the
# default chat + embedding models exist. Chat is required (matches LlmOptions /
# Compose); embed defaults to nomic-embed-text so the API is not stuck on
# HashEmbed when Compose omits LLM_EMBED_MODEL.
set -eu

ollama serve &
pid=$!

# Wait for the API using the ollama CLI (curl is not in the official image).
i=0
until ollama list >/dev/null 2>&1; do
  i=$((i + 1))
  if [ "$i" -gt 180 ]; then
    echo "Ollama failed to become ready" >&2
    exit 1
  fi
  sleep 1
done

CHAT_MODEL="${LLM_CHAT_MODEL:-qwen3:0.6b}"
EMBED_MODEL="${LLM_EMBED_MODEL:-nomic-embed-text}"

has_model() {
  model_name="$1"
  ollama list 2>/dev/null | awk 'NR > 1 { print $1 }' | grep -Fqx "$model_name"
}

ensure_model() {
  model_name="$1"
  required="$2"
  # Skip network pull when the volume already has the model — dominant restart cost.
  if has_model "$model_name"; then
    echo "Model $model_name already present; skipping pull"
    return 0
  fi
  if ollama pull "$model_name"; then
    return 0
  fi
  # Race: another process may have finished caching during a failed pull.
  if has_model "$model_name"; then
    echo "Using cached model $model_name after pull failure" >&2
    return 0
  fi
  if [ "$required" = "1" ]; then
    echo "Required model $model_name is unavailable" >&2
    exit 1
  fi
  echo "Optional model $model_name is unavailable; continuing" >&2
  return 0
}

ensure_model "$CHAT_MODEL" 1
ensure_model "$EMBED_MODEL" 1

wait "$pid"
