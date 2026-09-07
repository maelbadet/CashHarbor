ARG NGINX_VERSION=stable-alpine3.24-perl

FROM nginx:${NGINX_VERSION}

COPY docker/nginx/default.conf /etc/nginx/conf.d/default.conf
COPY app/public /var/www/app/public

RUN chown -R nginx:nginx /var/www/app/public