#!/bin/sh
set -eu

ollama serve &
pid=$!

# Wait for the API, then ensure default models exist.
i=0
until wget -q -O /dev/null http://127.0.0.1:11434/api/tags 2>/dev/null; do
  i=$((i + 1))
  if [ "$i" -gt 60 ]; then
    echo "Ollama failed to become ready" >&2
    exit 1
  fi
  sleep 1
done

CHAT_MODEL="${LLM_CHAT_MODEL:-qwen3:1.7b}"
EMBED_MODEL="${LLM_EMBED_MODEL:-nomic-embed-text}"
ollama pull "$CHAT_MODEL" || true
ollama pull "$EMBED_MODEL" || true

wait "$pid"
