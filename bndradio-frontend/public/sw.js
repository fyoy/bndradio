// bndradio service worker — minimal, just enables PWA install
const CACHE = 'bndradio-v1'

self.addEventListener('install', e => {
  self.skipWaiting()
})

self.addEventListener('activate', e => {
  e.waitUntil(
    caches.keys().then(keys =>
      Promise.all(keys.filter(k => k !== CACHE).map(k => caches.delete(k)))
    )
  )
  self.clients.claim()
})

// Network-first for everything (live stream, SSE must not be cached)
self.addEventListener('fetch', e => {
  const url = new URL(e.request.url)
  // Never cache stream, SSE, or API calls
  if (url.pathname.startsWith('/stream') ||
      url.pathname.startsWith('/events') ||
      url.pathname.startsWith('/presence') ||
      url.pathname.startsWith('/queue') ||
      url.pathname.startsWith('/songs')) {
    return // let browser handle
  }
  e.respondWith(
    fetch(e.request).catch(() => caches.match(e.request))
  )
})
