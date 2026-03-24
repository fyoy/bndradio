# 📻 bndradio

Веб-радио с живым стримом, каталогом треков и голосованием за следующую песню.

---

## Быстрый старт

Нужен только [Docker](https://docs.docker.com/get-docker/).

```bash
git clone <repo-url>
cd bndradio
docker compose up --build

или

docker compose up -d --force-recreate
```

Открой в браузере: **http://localhost:23000**

---

## Что умеет

- 🎵 Живой аудиострим
- 📂 Каталог треков с поиском
- ⬆️ Загрузка треков через drag & drop или форму
- 🗳️ Голосование за следующую песню
- 👥 Онлайн-счётчик слушателей
- 📜 История воспроизведения

---

## Стек

| Слой | Технология |
|------|-----------|
| Frontend | React + TypeScript + Vite |
| Backend | ASP.NET Core 10 |
| БД | PostgreSQL 16 |
| Кэш / pub-sub | Redis 7 |
| Прокси | nginx |

---

## Остановка

```bash
docker compose down
```

Данные (треки, БД) сохраняются в Docker volume `postgres_data`. Чтобы удалить всё:

```bash
docker compose down -v
```