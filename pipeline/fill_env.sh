#!/usr/bin/env bash
set -euo pipefail

dockerComposeFile="docker-compose.yml"

escape_sed() {
    printf '%s' "$1" | sed -e 's/[\/&]/\\&/g'
}

replace_placeholder() {
    local placeholder="$1"
    local value="$2"

    sed -i "s|${placeholder}|$(escape_sed "$value")|g" "${dockerComposeFile}"
}

secretAdminAuth=$(curl -s -H "X-Vault-Token: ${VaultToken}" "${baseURL}/Admin%2520auth")

ADMIN_LOGIN=$(echo "${secretAdminAuth}" | jq -r '.data.data.ADMIN_LOGIN')
ADMIN_PASSWORD=$(echo "${secretAdminAuth}" | jq -r '.data.data.ADMIN_PASSWORD')
JWT_SECRET=$(echo "${secretAdminAuth}" | jq -r '.data.data.JWT_SECRET')

if [[ -z "${ADMIN_LOGIN}" || "${ADMIN_LOGIN}" == "null" || \
      -z "${ADMIN_PASSWORD}" || "${ADMIN_PASSWORD}" == "null" || \
      -z "${JWT_SECRET}" || "${JWT_SECRET}" == "null" ]]
then
    echo "ADMIN: LOGIN/PASSWORD/JWT_SECRET is empty, check vault status or secret path"
    exit 1
else
    replace_placeholder "__ADMIN_LOGIN__" "${ADMIN_LOGIN}"
    replace_placeholder "__ADMIN_PASSWORD__" "${ADMIN_PASSWORD}"
    replace_placeholder "__JWT_SECRET__" "${JWT_SECRET}"
fi

secretMinIO=$(curl -s -H "X-Vault-Token: ${VaultToken}" "${baseURL}/MinIO")

MINIO_ROOT_USER=$(echo "${secretMinIO}" | jq -r '.data.data.MINIO_ROOT_USER')
MINIO_ROOT_PASSWORD=$(echo "${secretMinIO}" | jq -r '.data.data.MINIO_ROOT_PASSWORD')
MINIO_ENDPOINT=$(echo "${secretMinIO}" | jq -r '.data.data.MINIO_ENDPOINT')
MINIO_ACCESS_KEY=$(echo "${secretMinIO}" | jq -r '.data.data.MINIO_ACCESS_KEY')
MINIO_SECRET_KEY=$(echo "${secretMinIO}" | jq -r '.data.data.MINIO_SECRET_KEY')
MINIO_BUCKET_NAME=$(echo "${secretMinIO}" | jq -r '.data.data.MINIO_BUCKET_NAME')

if [[ -z "${MINIO_ROOT_USER}" || "${MINIO_ROOT_USER}" == "null" || \
      -z "${MINIO_ROOT_PASSWORD}" || "${MINIO_ROOT_PASSWORD}" == "null" || \
      -z "${MINIO_ENDPOINT}" || "${MINIO_ENDPOINT}" == "null" || \
      -z "${MINIO_ACCESS_KEY}" || "${MINIO_ACCESS_KEY}" == "null" || \
      -z "${MINIO_SECRET_KEY}" || "${MINIO_SECRET_KEY}" == "null" || \
      -z "${MINIO_BUCKET_NAME}" || "${MINIO_BUCKET_NAME}" == "null" ]]
then
    echo "MINIO: ROOT_USER/ROOT_PASSWORD/ENDPOINT/ACCESS_KEY/SECRET_KEY/BUCKET_NAME is empty, check vault status or secret path"
    exit 1
else
    replace_placeholder "__MINIO_ROOT_USER__" "${MINIO_ROOT_USER}"
    replace_placeholder "__MINIO_ROOT_PASSWORD__" "${MINIO_ROOT_PASSWORD}"
    replace_placeholder "__MINIO_ENDPOINT__" "${MINIO_ENDPOINT}"
    replace_placeholder "__MINIO_ACCESS_KEY__" "${MINIO_ACCESS_KEY}"
    replace_placeholder "__MINIO_SECRET_KEY__" "${MINIO_SECRET_KEY}"
    replace_placeholder "__MINIO_BUCKET_NAME__" "${MINIO_BUCKET_NAME}"
fi

secretFrontend=$(curl -s -H "X-Vault-Token: ${VaultToken}" "${baseURL}/Frontend")

FRONTEND_PORT=$(echo "${secretFrontend}" | jq -r '.data.data.FRONTEND_PORT')

if [[ -z "${FRONTEND_PORT}" || "${FRONTEND_PORT}" == "null" ]]
then
    echo "FRONTEND: PORT is empty, check vault status or secret path"
    exit 1
else
    replace_placeholder "__FRONTEND_PORT__" "${FRONTEND_PORT}"
fi

secretBackend=$(curl -s -H "X-Vault-Token: ${VaultToken}" "${baseURL}/Backend")

ASPNETCORE_URLS=$(echo "${secretBackend}" | jq -r '.data.data.ASPNETCORE_URLS')
CORS_ORIGINS=$(echo "${secretBackend}" | jq -r '.data.data.CORS_ORIGINS')

if [[ -z "${ASPNETCORE_URLS}" || "${ASPNETCORE_URLS}" == "null" || \
      -z "${CORS_ORIGINS}" || "${CORS_ORIGINS}" == "null" ]]
then
    echo "BACKEND: ASPNETCORE_URLS or CORS_ORIGINS is empty, check vault status or secret path"
    exit 1
else
    replace_placeholder "__ASPNETCORE_URLS__" "${ASPNETCORE_URLS}"
    replace_placeholder "__CORS_ORIGINS__" "${CORS_ORIGINS}"
fi

secretPostgreSQL=$(curl -s -H "X-Vault-Token: ${VaultToken}" "${baseURL}/PostgreSQL")

POSTGRES_USER=$(echo "${secretPostgreSQL}" | jq -r '.data.data.POSTGRES_USER')
POSTGRES_PASSWORD=$(echo "${secretPostgreSQL}" | jq -r '.data.data.POSTGRES_PASSWORD')
POSTGRES_DB=$(echo "${secretPostgreSQL}" | jq -r '.data.data.POSTGRES_DB')

if [[ -z "${POSTGRES_USER}" || "${POSTGRES_USER}" == "null" || \
      -z "${POSTGRES_PASSWORD}" || "${POSTGRES_PASSWORD}" == "null" || \
      -z "${POSTGRES_DB}" || "${POSTGRES_DB}" == "null" ]]
then
    echo "POSTGRES: USER/PASSWORD/DB is empty, check vault status or secret path"
    exit 1
else
    replace_placeholder "__POSTGRES_USER__" "${POSTGRES_USER}"
    replace_placeholder "__POSTGRES_PASSWORD__" "${POSTGRES_PASSWORD}"
    replace_placeholder "__POSTGRES_DB__" "${POSTGRES_DB}"
fi

echo "docker-compose.yml filled successfully"