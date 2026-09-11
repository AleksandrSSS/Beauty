# Multi-stage збірка клієнта: Vite build → статика в nginx.
# Контекст збірки — папка client/.

# ── Етап build: node збирає production-бандл ───────────────────────────────
FROM node:20-alpine AS build
WORKDIR /app

# API у docker-стеку — відносний (той самий origin через nginx), тому VITE_API_URL порожній.
# Клієнтський код падає на відносні шляхи `/api` при порожньому значенні.
ENV VITE_API_URL=""

# Спершу маніфести — щоб npm ci кешувався, поки залежності не змінюються.
COPY package.json package-lock.json ./
RUN npm ci

COPY . ./
RUN npm run build

# ── Етап runtime: nginx віддає статику й проксує API ───────────────────────
FROM nginx:alpine AS runtime
COPY nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /app/dist /usr/share/nginx/html
EXPOSE 80
