FROM node:22-alpine AS build
WORKDIR /app
ARG NPM_REGISTRY=https://registry.npmmirror.com
COPY package.json ./
RUN npm config set registry "$NPM_REGISTRY" \
    && npm config set fetch-retries 5 \
    && npm config set fetch-retry-factor 2 \
    && npm config set fetch-retry-mintimeout 5000 \
    && npm config set fetch-retry-maxtimeout 60000 \
    && npm config set fetch-timeout 120000 \
    && npm install --no-package-lock --prefer-offline --no-audit --no-fund --legacy-peer-deps --ignore-scripts --progress=false
ARG NEXT_PUBLIC_ENABLE_VIBIX=true
ARG NEXT_PUBLIC_ENABLE_AD_BANNER=true
ARG NEXT_PUBLIC_SITE_URL=https://stmstmstmstmstm.ru
ARG NEXT_PUBLIC_PLAUSIBLE_ENABLED=false
ARG NEXT_PUBLIC_PLAUSIBLE_DOMAIN=stmstmstmstmstm.ru
ARG NEXT_PUBLIC_PLAUSIBLE_BASE_PATH=/statsadmin
ENV NEXT_PUBLIC_ENABLE_VIBIX=$NEXT_PUBLIC_ENABLE_VIBIX
ENV NEXT_PUBLIC_ENABLE_AD_BANNER=$NEXT_PUBLIC_ENABLE_AD_BANNER
ENV NEXT_PUBLIC_SITE_URL=$NEXT_PUBLIC_SITE_URL
ENV NEXT_PUBLIC_PLAUSIBLE_ENABLED=$NEXT_PUBLIC_PLAUSIBLE_ENABLED
ENV NEXT_PUBLIC_PLAUSIBLE_DOMAIN=$NEXT_PUBLIC_PLAUSIBLE_DOMAIN
ENV NEXT_PUBLIC_PLAUSIBLE_BASE_PATH=$NEXT_PUBLIC_PLAUSIBLE_BASE_PATH
COPY . .
RUN npm run build

FROM node:22-alpine AS runtime
WORKDIR /app
ENV NODE_ENV=production
COPY --from=build /app/.next/standalone ./
COPY --from=build /app/.next/static ./.next/static
COPY --from=build /app/public ./public
EXPOSE 3000
CMD ["node", "server.js"]
