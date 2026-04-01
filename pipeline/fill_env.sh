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

admin=$(curl -s -H "X-Vault-Token: ${VaultToken}" "${baseURL}/Admin%2520auth")

ADMIN_LOGIN=$(echo "${admin}" | jq -r '.data.data.ADMIN_LOGIN')
ADMIN_PASSWORD=$(echo "${admin}" | jq -r '.data.data.ADMIN_PASSWORD')
JWT_SECRET=$(echo "${admin}" | jq -r '.data.data.JWT_SECRET')

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

minio=$(curl -s -H "X-Vault-Token: ${VaultToken}" "${baseURL}/MinIO")

MINIO_ROOT_USER=$(echo "${minio}" | jq -r '.data.data.MINIO_ROOT_USER')
MINIO_ROOT_PASSWORD=$(echo "${minio}" | jq -r '.data.data.MINIO_ROOT_PASSWORD')
MINIO_ENDPOINT=$(echo "${minio}" | jq -r '.data.data.MINIO_ENDPOINT')
MINIO_ACCESS_KEY=$(echo "${minio}" | jq -r '.data.data.MINIO_ACCESS_KEY')
MINIO_SECRET_KEY=$(echo "${minio}" | jq -r '.data.data.MINIO_SECRET_KEY')
MINIO_BUCKET_NAME=$(echo "${minio}" | jq -r '.data.data.MINIO_BUCKET_NAME')

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

front=$(curl -s -H "X-Vault-Token: ${VaultToken}" "${baseURL}/Frontend")

FRONTEND_PORT=$(echo "${front}" | jq -r '.data.data.FRONTEND_PORT')

if [[ -z "${FRONTEND_PORT}" || "${FRONTEND_PORT}" == "null" ]]
then
    echo "FRONTEND: PORT is empty, check vault status or secret path"
    exit 1
else
    replace_placeholder "__FRONTEND_PORT__" "${FRONTEND_PORT}"
fi

back=$(curl -s -H "X-Vault-Token: ${VaultToken}" "${baseURL}/Backend")

ASPNETCORE_URLS=$(echo "${back}" | jq -r '.data.data.ASPNETCORE_URLS')

if [[ -z "${ASPNETCORE_URLS}" || "${ASPNETCORE_URLS}" == "null" ]]
then
    echo "BACKEND: ASPNETCORE_URLS is empty, check vault status or secret path"
    exit 1
else
    replace_placeholder "__ASPNETCORE_URLS__" "${ASPNETCORE_URLS}"
fi

echo "docker-compose.yml filled successfully"