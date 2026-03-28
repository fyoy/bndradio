FROM node:20-alpine AS build
WORKDIR /app
COPY ./bndradio-frontend/package*.json ./
RUN npm install
COPY ./bndradio-frontend .
RUN npm run build

FROM nginx:alpine
COPY --from=build /app/dist /usr/share/nginx/html
COPY ./bndradio-frontend/nginx.conf /etc/nginx/conf.d/default.conf
EXPOSE 80
