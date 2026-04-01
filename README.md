# 📻 bndradio. web-radio with votes

## Fast start

```bash
git clone <repo-url>
cd bndradio

docker build -t bndradio_backend:latest -f bndradio-backend/Dockerfile .
docker build -t bndradio_frontend:latest -f bndradio-frontend/Dockerfile .
docker compose -f .\docker-compose-test.yml up -d 
```

default: **http://localhost:23000**
