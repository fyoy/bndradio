#!/bin/bash

dockerComposeFile="docker-compose.yml"

secretAdminAuth=$( curl -s -H "X-Vault-Token: ${VaultToken}" ${baseURL}/Admin%2520auth )

ADMIN_LOGIN=$(echo ${ADMIN_LOGIN} | jq -r '.data.data.ADMIN_LOGIN')
ADMIN_PASSWORD=$(echo ${ADMIN_PASSWORD} | jq -r '.data.data.ADMIN_PASSWORD')
JWT_SECRET=$(echo ${JWT_SECRET} | jq -r '.data.data.JWT_SECRET')

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

MINIO_ROOT_USER=$(echo ${MINIO_ROOT_USER} | jq -r '.data.data.MINIO_ROOT_USER')
MINIO_ROOT_PASSWORD=$(echo ${MINIO_ROOT_PASSWORD} | jq -r '.data.data.MINIO_ROOT_PASSWORD')
MINIO_ENDPOINT=$(echo ${MINIO_ENDPOINT} | jq -r '.data.data.MINIO_ENDPOINT')
MINIO_ACCESS_KEY=$(echo ${MINIO_ACCESS_KEY} | jq -r '.data.data.MINIO_ACCESS_KEY')
MINIO_SECRET_KEY=$(echo ${MINIO_SECRET_KEY} | jq -r '.data.data.MINIO_SECRET_KEY')
MINIO_BUCKET_NAME=$(echo ${MINIO_BUCKET_NAME} | jq -r '.data.data.MINIO_BUCKET_NAME')

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

FRONTEND_API_KEY=$(echo ${FRONTEND_API_KEY} | jq -r '.data.data.FRONTEND_API_KEY')

if [[ "${FRONTEND_API_KEY}" == null ]]
then
        echo "FRONTEND: API_KEY is empty, check vault status or secret path"
        exit 1
else
        sed -i "s%${FRONTEND_API_KEY}%${FRONTEND_API_KEY}%g" ${dockerComposeFile}
fi

secretBackend=$( curl -s -H "X-Vault-Token: ${VaultToken}" ${baseURL}/Backend )

ASPNETCORE_URLS=$(echo ${ASPNETCORE_URLS} | jq -r '.data.data.ASPNETCORE_URLS')
CORS_ORIGINS=$(echo ${CORS_ORIGINS} | jq -r '.data.data.CORS_ORIGINS')

if [[ "${BACKEND_API_KEY}" == null ]]
then
        echo "BACKEND: API_KEY is empty, check vault status or secret path"
        exit 1
else
        sed -i "s%${ASPNETCORE_URLS}%${ASPNETCORE_URLS}%g" ${dockerComposeFile}
        sed -i "s%${CORS_ORIGINS}%${CORS_ORIGINS}%g" ${dockerComposeFile}

fi

secretPostgreSQL=$( curl -s -H "X-Vault-Token: ${VaultToken}" ${baseURL}/PostgreSQL )

POSTGRES_USER=$(echo ${POSTGRES_USER} | jq -r '.data.data.POSTGRES_USER')
POSTGRES_PASSWORD=$(echo ${POSTGRES_PASSWORD} | jq -r '.data.data.POSTGRES_PASSWORD')
POSTGRES_DB=$(echo ${POSTGRES_DB} | jq -r '.data.data.POSTGRES_DB')

if [[ "${POSTGRES_USER}" == null || "${POSTGRES_PASSWORD}" == null || "${POSTGRES_DB}" == null ]]
then
        echo "ADMIN: LOGIN/PASSWORD/JWT_SECRET is empty, check vault status or secret path"
        exit 1
else
        sed -i "s%${POSTGRES_USER}%${POSTGRES_USER}%g" ${dockerComposeFile}
        sed -i "s%${POSTGRES_PASSWORD}%${POSTGRES_PASSWORD}%g" ${dockerComposeFile}
        sed -i "s%${POSTGRES_DB}%${POSTGRES_DB}%g" ${dockerComposeFile}
fi
