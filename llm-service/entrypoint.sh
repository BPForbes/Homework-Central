#!/bin/sh
set -eu

ollama serve &
pid=$!

# Wait for the API, then ensure default models exist.
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
ollama pull "$CHAT_MODEL" || true
if [ -n "${LLM_EMBED_MODEL:-}" ]; then
  ollama pull "$LLM_EMBED_MODEL" || true
fi

wait "$pid"
