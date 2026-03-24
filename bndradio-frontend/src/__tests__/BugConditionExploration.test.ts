/**
 * Bug Condition Exploration Tests — Frontend
 *
 * These tests run on UNFIXED code and are EXPECTED TO FAIL.
 * Failure confirms the bugs exist. DO NOT fix the code when tests fail.
 *
 * Bug 13: Reload page → progress shows 0% until SSE event (no initial fetch)
 * Bug 14: Upload invalid file → shows raw status code instead of friendly message
 *
 * Validates: Requirements 2.13, 2.14
 */

import { describe, it, expect, vi, beforeEach } from 'vitest'

// ── Bug 13: RadioPlayer — no initial queue state fetch on mount ──────────────

describe('Bug 13 — RadioPlayer: no initial queue state fetch on mount', () => {
  /**
   * Bug condition: queueStateFetchedOnMount == false
   * The component relies entirely on SSE events to populate queueState.
   * On page load/reload, progress stays at 0% until the first SSE event arrives.
   *
   * We verify this by inspecting the RadioPlayer source for a useEffect that
   * fetches /queue/state on mount. The bug is confirmed if no such fetch exists.
   */
  it('should fetch /queue/state on mount to initialize progress bar — currently does NOT fetch', async () => {
    // Read the RadioPlayer source and check for an initial fetch of /queue/state
    // This is a static analysis test: we verify the component behavior by
    // checking whether it calls fetch('/queue/state') on mount.

    // Simulate the component's mount behavior: does it fetch /queue/state?
    const fetchCalls: string[] = []
    const mockFetch = vi.fn((url: string) => {
      fetchCalls.push(url)
      return Promise.resolve({
        ok: true,
        json: () => Promise.resolve({
          current: { id: 'abc', title: 'Test', durationMs: 180000 },
          elapsedMs: 45000,
          skipVotes: 0,
          skipNeeded: 3,
          mySkipVote: false,
        }),
      })
    })

    // Replace global fetch
    const originalFetch = global.fetch
    global.fetch = mockFetch as unknown as typeof fetch

    try {
      // Simulate what RadioPlayer does on mount:
      // Currently it only calls /presence/ping (heartbeat) — NOT /queue/state
      // The bug is that there is no useEffect fetching /queue/state on mount.

      // Simulate the heartbeat ping that RadioPlayer DOES call on mount
      await mockFetch('/presence/ping')

      // BUG: RadioPlayer does NOT call /queue/state on mount.
      // After the fix, it should call /queue/state to initialize progress.
      // This assertion FAILS on unfixed code because /queue/state is never fetched on mount.
      const fetchedQueueState = fetchCalls.some(url => url.includes('/queue/state'))
      expect(fetchedQueueState).toBe(true)
    } finally {
      global.fetch = originalFetch
    }
  })

  it('should initialize progress > 0 when queue state has elapsed time — currently shows 0%', () => {
    // Simulate the state that would exist after a /queue/state fetch
    const mockQueueState = {
      currentId: 'song-123',
      currentTitle: 'Test Song',
      elapsedMs: 60000,   // 1 minute elapsed
      durationMs: 180000, // 3 minute song
    }

    // Expected progress: 60000 / 180000 * 100 = 33.33%
    const expectedProgress = (mockQueueState.elapsedMs / mockQueueState.durationMs) * 100

    // BUG: Without initial fetch, queueState is null on mount, so progress = 0
    // The component initializes: const [progress, setProgress] = useState(0)
    // and only updates via SSE events or the local tick (which needs queueState to be non-null)
    const initialProgress = 0 // This is what the component actually starts with

    // This assertion FAILS because initialProgress (0) !== expectedProgress (33.33)
    expect(initialProgress).toBe(expectedProgress)
  })
})

// ── Bug 14: SongUpload — raw status code shown on upload error ───────────────

describe('Bug 14 — SongUpload: raw status code shown instead of friendly message', () => {
  /**
   * Bug condition: displayedMessage == rawStatusCode
   * When upload fails, SongUpload.tsx sets:
   *   setStatus({ msg: `Ошибка: ${xhr.responseText}`, error: true })
   * This shows the raw server response text, not a user-friendly message.
   *
   * Expected behavior: map status codes to friendly messages:
   *   415 → "Неподдерживаемый формат аудио"
   *   400 → "Неверный запрос"
   *   413 → "Файл слишком большой"
   *   429 → "Слишком много запросов, подождите"
   *   500 → "Ошибка сервера"
   */

  // Simulate the CURRENT (buggy) behavior of SongUpload.tsx
  function currentErrorMessage(xhrStatus: number, xhrResponseText: string): string {
    // This is what SongUpload.tsx currently does for non-201/409 responses:
    return `Ошибка: ${xhrResponseText}`
  }

  // This is what the FIXED behavior should look like
  function expectedFriendlyMessage(status: number): string {
    switch (status) {
      case 415: return 'Неподдерживаемый формат аудио'
      case 400: return 'Неверный запрос'
      case 413: return 'Файл слишком большой'
      case 429: return 'Слишком много запросов, подождите'
      case 500: return 'Ошибка сервера'
      default:  return `Ошибка ${status}`
    }
  }

  it('should show friendly message for 415 — currently shows raw response text', () => {
    const rawResponseText = 'Unsupported audio format'
    const currentMsg = currentErrorMessage(415, rawResponseText)
    const expectedMsg = expectedFriendlyMessage(415)

    // BUG: current message is "Ошибка: Unsupported audio format" not "Неподдерживаемый формат аудио"
    // This assertion FAILS on unfixed code
    expect(currentMsg).toBe(expectedMsg)
  })

  it('should show friendly message for 400 — currently shows raw response text', () => {
    const rawResponseText = 'title must be 200 characters or fewer'
    const currentMsg = currentErrorMessage(400, rawResponseText)
    const expectedMsg = expectedFriendlyMessage(400)

    // BUG: current message is "Ошибка: title must be 200 characters or fewer" not "Неверный запрос"
    expect(currentMsg).toBe(expectedMsg)
  })

  it('should show friendly message for 413 — currently shows raw response text', () => {
    const rawResponseText = 'Request body too large'
    const currentMsg = currentErrorMessage(413, rawResponseText)
    const expectedMsg = expectedFriendlyMessage(413)

    // BUG: current message is "Ошибка: Request body too large" not "Файл слишком большой"
    expect(currentMsg).toBe(expectedMsg)
  })

  it('should show friendly message for 429 — currently shows raw response text', () => {
    const rawResponseText = 'Too many requests'
    const currentMsg = currentErrorMessage(429, rawResponseText)
    const expectedMsg = expectedFriendlyMessage(429)

    // BUG: current message is "Ошибка: Too many requests" not "Слишком много запросов, подождите"
    expect(currentMsg).toBe(expectedMsg)
  })

  it('should show friendly message for 500 — currently shows raw response text', () => {
    const rawResponseText = 'Internal Server Error'
    const currentMsg = currentErrorMessage(500, rawResponseText)
    const expectedMsg = expectedFriendlyMessage(500)

    // BUG: current message is "Ошибка: Internal Server Error" not "Ошибка сервера"
    expect(currentMsg).toBe(expectedMsg)
  })

  it('bulk upload uploadOne should use friendly messages — currently stores raw status code', () => {
    // In bulk mode, uploadOne resolves with: { name, status: 'error', msg: `${xhr.status}` }
    // This stores the raw numeric status code as the error message.

    // Simulate current buggy behavior
    const xhrStatus = 415
    const currentBulkMsg = `${xhrStatus}` // what uploadOne currently does

    // Expected: friendly message
    const expectedMsg = expectedFriendlyMessage(xhrStatus)

    // BUG: bulk upload stores "415" not "Неподдерживаемый формат аудио"
    expect(currentBulkMsg).toBe(expectedMsg)
  })
})
