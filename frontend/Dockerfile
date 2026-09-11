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
ENV NEXT_PUBLIC_ENABLE_VIBIX=$NEXT_PUBLIC_ENABLE_VIBIX
ENV NEXT_PUBLIC_ENABLE_AD_BANNER=$NEXT_PUBLIC_ENABLE_AD_BANNER
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
