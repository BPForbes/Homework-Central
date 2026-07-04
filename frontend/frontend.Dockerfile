# ---------- Build Stage ----------
FROM node:22-alpine AS build
WORKDIR /app

ENV NODE_ENV=development \
    NPM_CONFIG_FUND=false \
    NPM_CONFIG_AUDIT=false \
    NODE_OPTIONS=--max-old-space-size=512

COPY package*.json ./
RUN npm ci

COPY . ./
RUN npm run build && npm cache clean --force

# ---------- Runtime Stage ----------
FROM nginxinc/nginx-unprivileged:alpine AS runtime

USER root
RUN apk add --no-cache wget
USER 101

COPY --from=build /app/dist /usr/share/nginx/html
COPY nginx.conf /etc/nginx/conf.d/default.conf

EXPOSE 8080

HEALTHCHECK --interval=15s --timeout=5s --start-period=10s --retries=3 \
    CMD wget -q --spider http://127.0.0.1:8080/ || exit 1

CMD ["nginx", "-g", "daemon off;"]
