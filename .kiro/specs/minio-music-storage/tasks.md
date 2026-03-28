# Implementation Plan: MinIO Music Storage

## Overview

Migrate audio file storage from PostgreSQL `BYTEA` columns to MinIO. PostgreSQL retains only song metadata; MinIO holds all audio objects keyed by `songs/{song_id}`. The `ISongRepository` interface is preserved so no consumers need changes beyond DI wiring.

## Tasks

- [x] 1. Add MinIO service to Docker Compose
  - Add `minio` service using `minio/minio` image with API port 9000 and console port 9001
  - Add `minio_data` named volume for persistence
  - Add healthcheck using `curl -f http://localhost:9000/minio/health/live`
  - Add `depends_on: minio: condition: service_healthy` to the backend service
  - Pass `MINIO_ROOT_USER`, `MINIO_ROOT_PASSWORD`, `MINIO_ENDPOINT`, `MINIO_ACCESS_KEY`, `MINIO_SECRET_KEY`, `MINIO_BUCKET_NAME` env vars to the backend service
  - _Requirements: 1.1, 1.2, 1.3, 1.4, 1.5, 8.3_

- [x] 2. Add MinioOptions configuration and startup validation
  - [x] 2.1 Create `MinioOptions` POCO and bind it in `Program.cs`
    - Add `MinioOptions` class with `Endpoint`, `AccessKey`, `SecretKey`, `BucketName` properties
    - Bind via `builder.Services.Configure<MinioOptions>(builder.Configuration.GetSection("MinIO"))`
    - Add default values to `appsettings.json` suitable for local development
    - _Requirements: 2.3, 2.4, 8.1, 8.4_
  - [x] 2.2 Add startup validation for required MinIO config
    - After binding, validate that `Endpoint`, `AccessKey`, and `SecretKey` are non-empty; throw `InvalidOperationException` with a descriptive message if any are missing
    - _Requirements: 8.2_
  - [ ]* 2.3 Write property test for configuration validation (Property 10)
    - **Property 10: Configuration validation at startup**
    - **Validates: Requirements 8.2**

- [x] 3. Register `IAmazonS3` in DI and update `SongRepository` constructor
  - [x] 3.1 Register `IAmazonS3` as a singleton in `Program.cs`
    - Create `AmazonS3Client` from `MinioOptions` (endpoint, access key, secret key, `ForcePathStyle = true`)
    - Register as `IAmazonS3` singleton
    - _Requirements: 2.4, 8.1_
  - [x] 3.2 Update `SongRepository` constructor
    - Add `IAmazonS3 s3` and `IOptions<MinioOptions> minioOptions` parameters
    - Remove any existing `audio_data` read/write logic from all SQL queries
    - _Requirements: 6.3_

- [x] 4. Implement `EnsureSchemaAsync` — bucket creation and SQL migration
  - [x] 4.1 Create SQL migration file `migrations/002_remove_audio_data.sql`
    - Content: `ALTER TABLE songs DROP COLUMN IF EXISTS audio_data;`
    - _Requirements: 6.1, 6.2_
  - [x] 4.2 Implement bucket creation in `EnsureSchemaAsync`
    - Call `s3.ListBucketsAsync` or `PutBucketAsync`; create the bucket if it does not exist; proceed without error if it already exists
    - _Requirements: 2.1, 2.2_
  - [x] 4.3 Apply SQL migration in `EnsureSchemaAsync`
    - Execute `002_remove_audio_data.sql` as part of startup schema setup
    - _Requirements: 6.1, 6.2, 6.3_
  - [ ]* 4.4 Write property test for bucket initialization idempotency (Property 7)
    - **Property 7: Bucket initialization is idempotent**
    - **Validates: Requirements 2.1, 2.2**
  - [ ]* 4.5 Write property test for schema migration idempotency (Property 8)
    - **Property 8: Schema migration is idempotent**
    - **Validates: Requirements 6.1, 6.2**

- [x] 5. Implement `AddAsync` with MinIO upload and SHA-256 hashing
  - [x] 5.1 Implement SHA-256 hash computation before upload
    - Buffer the incoming stream, compute lowercase hex SHA-256 digest, store in `file_hash`
    - _Requirements: 3.5_
  - [x] 5.2 Implement MinIO upload in `AddAsync`
    - Upload buffered bytes to `Audio_Bucket` using key `songs/{song_id}` with `Content-Type: application/octet-stream`
    - If upload throws, propagate exception without inserting a DB row
    - _Requirements: 3.1, 3.3_
  - [x] 5.3 Implement metadata insert after successful upload
    - Insert row with id, title, artist, duration_ms, play_count, file_hash (no audio_data)
    - If DB insert throws, delete the MinIO object and propagate the exception
    - _Requirements: 3.2, 3.4_
  - [ ]* 5.4 Write property test for upload round-trip (Property 1)
    - **Property 1: Upload round-trip**
    - **Validates: Requirements 3.1, 3.2, 4.1**
  - [ ]* 5.5 Write property test for upload atomicity — MinIO failure (Property 2)
    - **Property 2: Upload atomicity — MinIO failure prevents metadata insert**
    - **Validates: Requirements 3.3**
  - [ ]* 5.6 Write property test for upload atomicity — DB failure (Property 3)
    - **Property 3: Upload atomicity — metadata failure triggers MinIO rollback**
    - **Validates: Requirements 3.4**
  - [ ]* 5.7 Write property test for SHA-256 hash consistency (Property 6)
    - **Property 6: SHA-256 hash consistency**
    - **Validates: Requirements 3.5**
  - [ ]* 5.8 Write unit tests for `AddAsync` error paths
    - Mock `IAmazonS3` throwing → verify no DB row inserted
    - Mock DB throwing after MinIO succeeds → verify MinIO delete called
    - _Requirements: 3.3, 3.4_

- [x] 6. Checkpoint — Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [x] 7. Implement `OpenAudioStreamAsync` with MinIO retrieval
  - [x] 7.1 Implement `OpenAudioStreamAsync`
    - Call `s3.GetObjectAsync` with key `songs/{song_id}`; copy response stream to a `MemoryStream` and return it
    - If `AmazonS3Exception` with `NoSuchKey` error code is caught, throw `KeyNotFoundException`
    - _Requirements: 4.1, 4.2_
  - [ ]* 7.2 Write property test for missing object raises KeyNotFoundException (Property 5)
    - **Property 5: Missing object raises KeyNotFoundException**
    - **Validates: Requirements 4.2**
  - [ ]* 7.3 Write property test for concurrent audio streams (Property 11)
    - **Property 11: Concurrent audio streams are independent**
    - **Validates: Requirements 4.3**
  - [ ]* 7.4 Write unit test for `OpenAudioStreamAsync` error path
    - Mock returning `NoSuchKeyException` → verify `KeyNotFoundException` thrown
    - _Requirements: 4.2_

- [x] 8. Implement `DeleteAsync` with MinIO object removal
  - [x] 8.1 Implement `DeleteAsync`
    - Delete metadata row from PostgreSQL
    - Delete MinIO object using key `songs/{song_id}`; silently ignore `NoSuchKey` errors
    - _Requirements: 5.1, 5.2, 5.3_
  - [ ]* 8.2 Write property test for delete idempotency (Property 4)
    - **Property 4: Delete is idempotent**
    - **Validates: Requirements 5.1, 5.2, 5.3**
  - [ ]* 8.3 Write unit test for `DeleteAsync` with missing MinIO object
    - Mock returning `NoSuchKeyException` → verify no exception thrown
    - _Requirements: 5.3_

- [x] 9. Checkpoint — Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [x] 10. Implement the data migration tool (`bndradio-migration`)
  - [x] 10.1 Create the migration console project
    - Add a new .NET console project `bndradio-migration` to the solution
    - Wire up `IAmazonS3`, `NpgsqlDataSource`, and `MinioOptions` from configuration
    - _Requirements: 7.1_
  - [x] 10.2 Implement per-song migration logic
    - Query all rows from `songs` where `audio_data IS NOT NULL`
    - For each row, issue a HEAD request (`GetObjectMetadataAsync`) to check if `songs/{id}` already exists in MinIO
    - If absent, upload `audio_data` bytes; if present, skip
    - Log success or failure per song; on upload failure, log and continue
    - _Requirements: 7.1, 7.2, 7.3, 7.5_
  - [x] 10.3 Drop `audio_data` column after all songs are processed
    - After iterating all rows, execute `ALTER TABLE songs DROP COLUMN IF EXISTS audio_data`
    - _Requirements: 7.4_
  - [ ]* 10.4 Write property test for migration skips already-migrated songs (Property 9)
    - **Property 9: Data migration skips already-migrated songs**
    - **Validates: Requirements 7.3**
  - [ ]* 10.5 Write property test for migration continues past failures (Property 12)
    - **Property 12: Migration continues past individual upload failures**
    - **Validates: Requirements 7.5**

- [x] 11. Final checkpoint — Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

## Notes

- Tasks marked with `*` are optional and can be skipped for a faster MVP
- Each task references specific requirements for traceability
- Property tests use FsCheck.Xunit (already in project) with a minimum of 100 iterations
- The `ISongRepository` interface and all consumers (`StreamServer`, `UploadController`) remain unchanged
- Integration tests using `WebApplicationFactory<Program>` should continue to pass without modification
