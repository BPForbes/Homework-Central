# Pin the Ollama base image (never :latest). Tag verified via Docker Hub API
# (ollama/ollama:0.32.4, active as of 2026-07-25) and GitHub release v0.32.4.
ARG OLLAMA_VERSION=0.32.4
FROM ollama/ollama:${OLLAMA_VERSION}

ENV OLLAMA_HOST=0.0.0.0:11434
EXPOSE 11434

# Official ollama image has no curl; healthchecks must use the ollama CLI
# (see ollama/ollama#9781 and google/langextract#361).
HEALTHCHECK --interval=30s --timeout=10s --start-period=60s --retries=5 \
  CMD ollama list >/dev/null 2>&1 || exit 1

# Pull a small chat + embedding model on first boot when volumes are empty.
COPY entrypoint.sh /entrypoint.sh
RUN chmod +x /entrypoint.sh
ENTRYPOINT ["/entrypoint.sh"]
