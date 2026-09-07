ARG PHP_VERSION=8.5
ARG COMPOSER_VERSION=2

# ============================================================
# Stage 1 : récupération de Composer
# ============================================================
FROM composer:${COMPOSER_VERSION} AS composer

# ============================================================
# Stage 2 : construction des extensions PHP
# ============================================================
FROM php:${PHP_VERSION}-fpm-alpine AS php_extensions

RUN apk add --no-cache \
        icu-libs \
        libzip \
    && apk add --no-cache --virtual .build-deps \
        icu-dev \
        libzip-dev \
        mariadb-connector-c-dev \
        $PHPIZE_DEPS \
    && docker-php-ext-configure intl \
    && docker-php-ext-install -j"$(nproc)" \
        intl \
        opcache \
        pdo_mysql \
        zip \
    && apk del .build-deps


# ============================================================
# Stage 3 : installation des dépendances Composer
# ============================================================
FROM php_extensions AS vendor

WORKDIR /var/www/app

COPY --from=composer /usr/bin/composer /usr/local/bin/composer

COPY app/composer.json app/composer.lock app/symfony.lock ./

RUN composer install \
        --no-dev \
        --no-interaction \
        --no-progress \
        --no-scripts \
        --prefer-dist \
        --optimize-autoloader


# ============================================================
# Stage 4 : image PHP finale
# ============================================================
FROM php_extensions AS production

WORKDIR /var/www/app

RUN apk add --no-cache \
        icu-libs \
        libzip \
        mariadb-connector-c \
    && mv "$PHP_INI_DIR/php.ini-production" "$PHP_INI_DIR/php.ini"

COPY docker/php/php.ini "$PHP_INI_DIR/conf.d/99-app.ini"

COPY --chown=www-data:www-data app/ ./

COPY --from=vendor \
    --chown=www-data:www-data \
    /var/www/app/vendor \
    /var/www/app/vendor

RUN mkdir -p var/cache var/log \
    && chown -R www-data:www-data var

USER www-data

EXPOSE 9000

CMD ["php-fpm"]