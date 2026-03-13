FROM node:20-alpine AS deps
WORKDIR /app
COPY package.json tsconfig.json ./
RUN npm install

FROM node:20-alpine AS build
WORKDIR /app
COPY --from=deps /app/node_modules ./node_modules
COPY package.json tsconfig.json ./
COPY src ./src
COPY mock-api ./mock-api
COPY scripts ./scripts
COPY test-data ./test-data
RUN npm run build

FROM node:20-alpine
WORKDIR /app
ENV NODE_ENV=production
COPY --from=build /app/package.json ./
COPY --from=build /app/node_modules ./node_modules
COPY --from=build /app/dist ./dist
COPY --from=build /app/test-data ./test-data
CMD ["node", "dist/mock-api/src/server.js"]
