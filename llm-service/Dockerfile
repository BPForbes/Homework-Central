FROM ollama/ollama:latest

ENV OLLAMA_HOST=0.0.0.0:11434
EXPOSE 11434

# Pull a small chat + embedding model on first boot when volumes are empty.
COPY entrypoint.sh /entrypoint.sh
RUN chmod +x /entrypoint.sh
ENTRYPOINT ["/entrypoint.sh"]
