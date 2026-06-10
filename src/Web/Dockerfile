FROM node:22-alpine AS deps
WORKDIR /app

COPY package.json package-lock.json ./
RUN npm ci

FROM node:22-alpine AS build
WORKDIR /app

ARG API_URL=http://api:8080
ENV API_URL=${API_URL}

COPY --from=deps /app/node_modules ./node_modules
COPY . ./
RUN npm run build

FROM node:22-alpine AS runtime
WORKDIR /app

ENV NODE_ENV=production
ENV PORT=3000
ENV HOSTNAME=0.0.0.0

COPY --from=build /app/.next ./.next
COPY --from=build /app/public ./public
COPY --from=build /app/package.json ./package.json
COPY --from=deps /app/node_modules ./node_modules

EXPOSE 3000
CMD ["npm", "start"]
