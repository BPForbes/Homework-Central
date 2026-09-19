# ---------- Build Stage ----------
FROM node:22-alpine AS build
WORKDIR /app

# NODE_OPTIONS caps V8's old space so the Vite/Rollup build cannot balloon to the default heap
# ceiling (which is derived from total host RAM). UV_THREADPOOL_SIZE trims libuv's worker pool
# from the default 4; the build is CPU-bound in-process, not I/O-bound.
ENV NODE_ENV=development \
    NPM_CONFIG_FUND=false \
    NPM_CONFIG_AUDIT=false \
    NPM_CONFIG_UPDATE_NOTIFIER=false \
    NODE_OPTIONS=--max-old-space-size=512 \
    UV_THREADPOOL_SIZE=2

COPY package*.json ./
RUN npm ci

COPY . ./
RUN npm run build && npm cache clean --force

# ---------- Runtime Stage ----------
FROM nginxinc/nginx-unprivileged:alpine AS runtime

USER root
# nginx defaults to `worker_processes auto` — one worker process per core, each with its own
# connection pool. This image serves a handful of static files behind the dev stack, so a single
# worker with a smaller pool is all it needs, and it saves a process (and its preallocated
# connection structures) per core.
RUN sed -i \
        -e 's/^[[:space:]]*worker_processes[[:space:]].*/worker_processes 1;/' \
        -e 's/worker_connections[[:space:]]\+[0-9]\+;/worker_connections 512;/' \
        /etc/nginx/nginx.conf && \
    grep -q '^worker_processes 1;' /etc/nginx/nginx.conf && \
    apk add --no-cache wget
USER 101

COPY --from=build /app/dist /usr/share/nginx/html
COPY nginx.conf /etc/nginx/conf.d/default.conf

EXPOSE 8080

# 30s rather than 15s — halves the number of wget processes forked inside the container.
HEALTHCHECK --interval=30s --timeout=5s --start-period=10s --retries=3 \
    CMD wget -q --spider http://127.0.0.1:8080/ || exit 1

CMD ["nginx", "-g", "daemon off;"]
