#!/bin/bash

dockerComposeFile="docker-compose.yml"

secretAdminAuth=$( curl -s -H "X-Vault-Token: ${VaultToken}" ${baseURL}/Admin%2520auth )

ADMIN_LOGIN=$(echo ${secretAdminAuth} | jq -r '.data.data.ADMIN_LOGIN')
ADMIN_PASSWORD=$(echo ${secretAdminAuth} | jq -r '.data.data.ADMIN_PASSWORD')
JWT_SECRET=$(echo ${secretAdminAuth} | jq -r '.data.data.JWT_SECRET')

if [[ "${ADMIN_LOGIN}" == null || "${ADMIN_PASSWORD}" == null || "${JWT_SECRET}" == null ]]
then
        echo "ADMIN: LOGIN/PASSWORD/JWT_SECRET is empty, check vault status or secret path"
        exit 1
else
        sed -i "s%${ADMIN_LOGIN}%${ADMIN_LOGIN}%g" ${dockerComposeFile}
        sed -i "s%${ADMIN_PASSWORD}%${ADMIN_PASSWORD}%g" ${dockerComposeFile}
        sed -i "s%${JWT_SECRET}%${JWT_SECRET}%g" ${dockerComposeFile}
fi

secretMinIO=$( curl -s -H "X-Vault-Token: ${VaultToken}" ${baseURL}/MinIO )

MINIO_ROOT_USER=$(echo ${secretMinIO} | jq -r '.data.data.MINIO_ROOT_USER')
MINIO_ROOT_PASSWORD=$(echo ${secretMinIO} | jq -r '.data.data.MINIO_ROOT_PASSWORD')
MINIO_ENDPOINT=$(echo ${secretMinIO} | jq -r '.data.data.MINIO_ENDPOINT')
MINIO_ACCESS_KEY=$(echo ${secretMinIO} | jq -r '.data.data.MINIO_ACCESS_KEY')
MINIO_SECRET_KEY=$(echo ${secretMinIO} | jq -r '.data.data.MINIO_SECRET_KEY')
MINIO_BUCKET_NAME=$(echo ${secretMinIO} | jq -r '.data.data.MINIO_BUCKET_NAME')

if [[ "${MINIO_ROOT_USER}" == null || "${MINIO_ROOT_PASSWORD}" == null || "${MINIO_ENDPOINT}" == null || "${MINIO_ACCESS_KEY}" == null || "${MINIO_SECRET_KEY}" == null || "${MINIO_BUCKET_NAME}" == null ]]
then
        echo "MINIO: ROOT_USER/ROOT_PASSWORD/ENDPOINT/ACCESS_KEY/SECRET_KEY/BUCKET_NAME is empty, check vault status or secret path"
        exit 1
else
        sed -i "s%${MINIO_ROOT_USER}%${MINIO_ROOT_USER}%g" ${dockerComposeFile}
        sed -i "s%${MINIO_ROOT_PASSWORD}%${MINIO_ROOT_PASSWORD}%g" ${dockerComposeFile}
        sed -i "s%${MINIO_ENDPOINT}%${MINIO_ENDPOINT}%g" ${dockerComposeFile}
        sed -i "s%${MINIO_ACCESS_KEY}%${MINIO_ACCESS_KEY}%g" ${dockerComposeFile}
        sed -i "s%${MINIO_SECRET_KEY}%${MINIO_SECRET_KEY}%g" ${dockerComposeFile}
        sed -i "s%${MINIO_BUCKET_NAME}%${MINIO_BUCKET_NAME}%g" ${dockerComposeFile}
fi

secretFrontend=$( curl -s -H "X-Vault-Token: ${VaultToken}" ${baseURL}/Frontend )

FRONTEND_PORT=$(echo ${secretFrontend} | jq -r '.data.data.FRONTEND_PORT')

if [[ "${FRONTEND_PORT}" == null ]]
then
        echo "FRONTEND: PORT is empty, check vault status or secret path"
        exit 1
else
        sed -i "s%${FRONTEND_PORT}%${FRONTEND_PORT}%g" ${dockerComposeFile}
fi

secretBackend=$( curl -s -H "X-Vault-Token: ${VaultToken}" ${baseURL}/Backend )

ASPNETCORE_URLS=$(echo ${secretBackend} | jq -r '.data.data.ASPNETCORE_URLS')
CORS_ORIGINS=$(echo ${secretBackend} | jq -r '.data.data.CORS_ORIGINS')

if [[ "${ASPNETCORE_URLS}" == null || "${CORS_ORIGINS}" == null ]]
then
        echo "BACKEND: ASPNETCORE_URLS or CORS_ORIGINS is empty, check vault status or secret path"
        exit 1
else
        sed -i "s%${ASPNETCORE_URLS}%${ASPNETCORE_URLS}%g" ${dockerComposeFile}
        sed -i "s%${CORS_ORIGINS}%${CORS_ORIGINS}%g" ${dockerComposeFile}

fi

secretPostgreSQL=$( curl -s -H "X-Vault-Token: ${VaultToken}" ${baseURL}/PostgreSQL )

POSTGRES_USER=$(echo ${secretPostgreSQL} | jq -r '.data.data.POSTGRES_USER')
POSTGRES_PASSWORD=$(echo ${secretPostgreSQL} | jq -r '.data.data.POSTGRES_PASSWORD')
POSTGRES_DB=$(echo ${secretPostgreSQL} | jq -r '.data.data.POSTGRES_DB')

if [[ "${POSTGRES_USER}" == null || "${POSTGRES_PASSWORD}" == null || "${POSTGRES_DB}" == null ]]
then
        echo "POSTGRES: USER/PASSWORD/DB is empty, check vault status or secret path"
        exit 1
else
        sed -i "s%${POSTGRES_USER}%${POSTGRES_USER}%g" ${dockerComposeFile}
        sed -i "s%${POSTGRES_PASSWORD}%${POSTGRES_PASSWORD}%g" ${dockerComposeFile}
        sed -i "s%${POSTGRES_DB}%${POSTGRES_DB}%g" ${dockerComposeFile}
fi