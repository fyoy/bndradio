CREATE TABLE songs (
    id          UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    title       TEXT NOT NULL,
    artist      TEXT NOT NULL,
    duration_ms INTEGER NOT NULL,
    audio_data  BYTEA NOT NULL
);
