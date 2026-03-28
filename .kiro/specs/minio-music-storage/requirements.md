# Requirements Document

## Introduction

This feature migrates audio file storage in bndradio from PostgreSQL `BYTEA` columns to MinIO, an S3-compatible object storage service. Currently, song binary data is stored directly in the `songs` table (`audio_data BYTEA`), which limits scalability and makes the database unnecessarily large. After migration, PostgreSQL retains only song metadata (id, title, artist, duration_ms, play_count, file_hash), while audio files are stored as objects in a MinIO bucket. The `ISongRepository` interface is preserved so that `StreamServer`, `UploadController`, and other consumers require no changes beyond DI wiring.

## Glossary

- **MinIO**: An S3-compatible open-source object storage server, deployed as a Docker service alongside the existing stack.
- **MinIO_Client**: The .NET `AWSSDK.S3` or `Minio` SDK client used to communicate with the MinIO server.
- **Song_Repository**: The `SongRepository` service that implements `ISongRepository` and coordinates metadata storage in PostgreSQL with audio object storage in MinIO.
- **Object_Key**: The MinIO object identifier for an audio file, formatted as `songs/{song_id}` (e.g., `songs/3fa85f64-5717-4562-b3fc-2c963f66afa6`).
- **Audio_Bucket**: The MinIO bucket that holds all audio objects, named `audio` by default and configurable via environment variable.
- **Metadata_DB**: The PostgreSQL database that stores song metadata rows (id, title, artist, duration_ms, play_count, file_hash) without audio binary data.
- **Stream_Server**: The `StreamServer` hosted service that reads audio streams via `ISongRepository.OpenAudioStreamAsync` and broadcasts them to listeners.
- **Upload_Controller**: The `UploadController` ASP.NET controller that accepts multipart audio uploads and delegates storage to `ISongRepository.AddAsync`.
- **Migration_Tool**: A one-time CLI utility or startup routine that reads existing `audio_data` from PostgreSQL and writes each file to MinIO, then removes the `audio_data` column.

## Requirements

### Requirement 1: MinIO Service in Docker Compose

**User Story:** As a developer, I want MinIO to run as part of the Docker Compose stack, so that the object storage service is available alongside the existing backend, PostgreSQL, and Redis services.

#### Acceptance Criteria

1. THE Docker_Compose_Stack SHALL include a MinIO service using the `minio/minio` image.
2. WHEN the MinIO service starts, THE Docker_Compose_Stack SHALL expose the MinIO API on port 9000 and the MinIO Console on port 9001.
3. THE Docker_Compose_Stack SHALL persist MinIO data using a named Docker volume.
4. WHEN the backend service starts, THE Docker_Compose_Stack SHALL ensure the MinIO service is healthy before the backend service starts.
5. THE Docker_Compose_Stack SHALL read MinIO root credentials (`MINIO_ROOT_USER`, `MINIO_ROOT_PASSWORD`) from environment variables.

---

### Requirement 2: MinIO Bucket Initialization

**User Story:** As a developer, I want the Audio_Bucket to be created automatically on startup, so that the system is ready to store audio files without manual setup.

#### Acceptance Criteria

1. WHEN the backend application starts, THE Song_Repository SHALL create the Audio_Bucket if it does not already exist.
2. WHEN the Audio_Bucket already exists, THE Song_Repository SHALL proceed without error.
3. THE Song_Repository SHALL read the bucket name from the `MinIO:BucketName` configuration key, defaulting to `audio`.
4. THE Song_Repository SHALL read the MinIO endpoint, access key, and secret key from configuration keys `MinIO:Endpoint`, `MinIO:AccessKey`, and `MinIO:SecretKey`.

---

### Requirement 3: Audio Upload to MinIO

**User Story:** As an admin, I want uploaded audio files to be stored in MinIO, so that the PostgreSQL database is not burdened with binary data.

#### Acceptance Criteria

1. WHEN `ISongRepository.AddAsync` is called with a valid audio stream, THE Song_Repository SHALL upload the audio data as an object to the Audio_Bucket using the Object_Key `songs/{song_id}`.
2. WHEN the audio object is successfully stored in MinIO, THE Song_Repository SHALL insert a metadata row into the Metadata_DB containing id, title, artist, duration_ms, play_count, and file_hash, but no audio binary data.
3. IF the MinIO upload fails, THEN THE Song_Repository SHALL not insert a metadata row into the Metadata_DB and SHALL propagate the exception to the caller.
4. IF the Metadata_DB insert fails after a successful MinIO upload, THEN THE Song_Repository SHALL delete the uploaded object from MinIO and SHALL propagate the exception to the caller.
5. THE Song_Repository SHALL compute the SHA-256 file hash before uploading and store it in the `file_hash` column of the Metadata_DB.

---

### Requirement 4: Audio Streaming from MinIO

**User Story:** As a listener, I want the radio stream to play audio files retrieved from MinIO, so that playback is unaffected by the storage migration.

#### Acceptance Criteria

1. WHEN `ISongRepository.OpenAudioStreamAsync` is called with a valid song id, THE Song_Repository SHALL retrieve the audio object from MinIO using the Object_Key `songs/{song_id}` and return a readable `Stream`.
2. IF the object does not exist in MinIO for the given song id, THEN THE Song_Repository SHALL throw a `KeyNotFoundException`.
3. WHILE the Stream_Server is broadcasting, THE Song_Repository SHALL support concurrent calls to `OpenAudioStreamAsync` without data corruption.

---

### Requirement 5: Audio Deletion from MinIO

**User Story:** As an admin, I want deleting a song to remove both its metadata and its audio file, so that storage is not leaked.

#### Acceptance Criteria

1. WHEN `ISongRepository.DeleteAsync` is called with a valid song id, THE Song_Repository SHALL delete the metadata row from the Metadata_DB.
2. WHEN `ISongRepository.DeleteAsync` is called with a valid song id, THE Song_Repository SHALL delete the audio object from MinIO using the Object_Key `songs/{song_id}`.
3. IF the audio object does not exist in MinIO during deletion, THEN THE Song_Repository SHALL complete the deletion without error (idempotent delete).

---

### Requirement 6: Schema Migration — Remove audio_data Column

**User Story:** As a developer, I want the `audio_data` BYTEA column removed from the `songs` table, so that PostgreSQL no longer stores audio binary data.

#### Acceptance Criteria

1. THE Migration_Tool SHALL add a SQL migration that drops the `audio_data` column from the `songs` table.
2. WHEN the migration is applied to a database that has no `audio_data` column, THE Migration_Tool SHALL complete without error (idempotent).
3. THE Song_Repository SHALL not reference the `audio_data` column in any SQL query after the migration is applied.

---

### Requirement 7: Data Migration from PostgreSQL to MinIO

**User Story:** As a developer, I want existing songs stored in PostgreSQL to be migrated to MinIO, so that no audio data is lost during the transition.

#### Acceptance Criteria

1. THE Migration_Tool SHALL read each existing song row from the `songs` table, including its `audio_data` bytes.
2. WHEN a song's audio data is read from PostgreSQL, THE Migration_Tool SHALL upload the audio bytes to MinIO using the Object_Key `songs/{song_id}`.
3. WHEN a song has already been migrated (object exists in MinIO), THE Migration_Tool SHALL skip re-uploading that song (idempotent).
4. WHEN all songs have been migrated to MinIO, THE Migration_Tool SHALL drop the `audio_data` column from the `songs` table.
5. IF an upload to MinIO fails for a specific song, THEN THE Migration_Tool SHALL log the error and continue migrating remaining songs.

---

### Requirement 8: Configuration and Environment Variables

**User Story:** As a developer, I want all MinIO connection parameters to be configurable via environment variables, so that the system can be deployed in different environments without code changes.

#### Acceptance Criteria

1. THE Backend_Application SHALL read MinIO configuration from the following environment variables: `MINIO_ENDPOINT`, `MINIO_ACCESS_KEY`, `MINIO_SECRET_KEY`, and `MINIO_BUCKET_NAME`.
2. IF a required MinIO environment variable is missing at startup, THEN THE Backend_Application SHALL throw an `InvalidOperationException` with a descriptive message identifying the missing variable.
3. THE Docker_Compose_Stack SHALL pass the MinIO environment variables to the backend service.
4. THE `appsettings.json` file SHALL include default MinIO configuration values suitable for local development.

---

### Requirement 9: Health Check Compatibility

**User Story:** As a developer, I want the existing `/health` endpoint to remain functional after the migration, so that Docker Compose health checks and monitoring are unaffected.

#### Acceptance Criteria

1. WHEN a GET request is made to `/health`, THE Backend_Application SHALL return HTTP 200 with `{"status": "healthy"}`.
2. THE Backend_Application SHALL not add MinIO connectivity to the `/health` response, keeping the existing contract unchanged.
