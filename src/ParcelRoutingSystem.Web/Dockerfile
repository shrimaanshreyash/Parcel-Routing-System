FROM node:22-alpine AS build
WORKDIR /web
COPY src/ParcelRoutingSystem.Web/package.json src/ParcelRoutingSystem.Web/package-lock.json ./
RUN npm ci
COPY src/ParcelRoutingSystem.Web/ ./
RUN npm run build

FROM nginxinc/nginx-unprivileged:1.27-alpine AS runtime
COPY ops/nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /web/dist /usr/share/nginx/html
EXPOSE 8080
