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

COPY --from=build /app/dist /usr/share/nginx/html
COPY nginx.conf /etc/nginx/conf.d/default.conf

EXPOSE 8080
CMD ["nginx", "-g", "daemon off;"]
