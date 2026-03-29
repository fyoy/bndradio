#!/bin/bash

dockerComposeFile="docker-compose.yml"

secretAdminAuth=$(curl -s -H "X-Vault-Token: ${VaultToken}" ${baseURL}/Admin%2520auth)

ADMIN_LOGIN=$(echo ${secretAdminAuth} | jq -r '.data.data.ADMIN_LOGIN')
ADMIN_PASSWORD=$(echo ${secretAdminAuth} | jq -r '.data.data.ADMIN_PASSWORD')
JWT_SECRET=$(echo ${secretAdminAuth} | jq -r '.data.data.JWT_SECRET')

if [[ "${ADMIN_LOGIN}" == "null" || "${ADMIN_PASSWORD}" == "null" || "${JWT_SECRET}" == "null" ]]
then
    echo "ADMIN: LOGIN/PASSWORD/JWT_SECRET is empty, check vault status or secret path"
    exit 1
else
    export ADMIN_LOGIN
    export ADMIN_PASSWORD
    export JWT_SECRET
fi

secretMinIO=$(curl -s -H "X-Vault-Token: ${VaultToken}" ${baseURL}/MinIO)

MINIO_ROOT_USER=$(echo ${secretMinIO} | jq -r '.data.data.MINIO_ROOT_USER')
MINIO_ROOT_PASSWORD=$(echo ${secretMinIO} | jq -r '.data.data.MINIO_ROOT_PASSWORD')
MINIO_ENDPOINT=$(echo ${secretMinIO} | jq -r '.data.data.MINIO_ENDPOINT')
MINIO_ACCESS_KEY=$(echo ${secretMinIO} | jq -r '.data.data.MINIO_ACCESS_KEY')
MINIO_SECRET_KEY=$(echo ${secretMinIO} | jq -r '.data.data.MINIO_SECRET_KEY')
MINIO_BUCKET_NAME=$(echo ${secretMinIO} | jq -r '.data.data.MINIO_BUCKET_NAME')

if [[ "${MINIO_ROOT_USER}" == "null" || "${MINIO_ROOT_PASSWORD}" == "null" || "${MINIO_ENDPOINT}" == "null" || "${MINIO_ACCESS_KEY}" == "null" || "${MINIO_SECRET_KEY}" == "null" || "${MINIO_BUCKET_NAME}" == "null" ]]
then
    echo "MINIO: ROOT_USER/ROOT_PASSWORD/ENDPOINT/ACCESS_KEY/SECRET_KEY/BUCKET_NAME is empty, check vault status or secret path"
    exit 1
else
    export MINIO_ROOT_USER
    export MINIO_ROOT_PASSWORD
    export MINIO_ENDPOINT
    export MINIO_ACCESS_KEY
    export MINIO_SECRET_KEY
    export MINIO_BUCKET_NAME
fi

secretFrontend=$(curl -s -H "X-Vault-Token: ${VaultToken}" ${baseURL}/Frontend)

FRONTEND_PORT=$(echo ${secretFrontend} | jq -r '.data.data.FRONTEND_PORT')

if [[ "${FRONTEND_PORT}" == "null" ]]
then
    echo "FRONTEND: PORT is empty, check vault status or secret path"
    exit 1
else
    export FRONTEND_PORT
fi

secretBackend=$(curl -s -H "X-Vault-Token: ${VaultToken}" ${baseURL}/Backend)

ASPNETCORE_URLS=$(echo ${secretBackend} | jq -r '.data.data.ASPNETCORE_URLS')
CORS_ORIGINS=$(echo ${secretBackend} | jq -r '.data.data.CORS_ORIGINS')

if [[ "${ASPNETCORE_URLS}" == "null" || "${CORS_ORIGINS}" == "null" ]]
then
    echo "BACKEND: ASPNETCORE_URLS or CORS_ORIGINS is empty, check vault status or secret path"
    exit 1
else
    export ASPNETCORE_URLS
    export CORS_ORIGINS
fi

secretPostgreSQL=$(curl -s -H "X-Vault-Token: ${VaultToken}" ${baseURL}/PostgreSQL)

POSTGRES_USER=$(echo ${secretPostgreSQL} | jq -r '.data.data.POSTGRES_USER')
POSTGRES_PASSWORD=$(echo ${secretPostgreSQL} | jq -r '.data.data.POSTGRES_PASSWORD')
POSTGRES_DB=$(echo ${secretPostgreSQL} | jq -r '.data.data.POSTGRES_DB')

if [[ "${POSTGRES_USER}" == "null" || "${POSTGRES_PASSWORD}" == "null" || "${POSTGRES_DB}" == "null" ]]
then
    echo "POSTGRES: USER/PASSWORD/DB is empty, check vault status or secret path"
    exit 1
else
    export POSTGRES_USER
    export POSTGRES_PASSWORD
    export POSTGRES_DB
fi

echo "All environment variables exported successfully"