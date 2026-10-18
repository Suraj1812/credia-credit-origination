const CACHE = 'credia-shell-v1';
self.addEventListener('install', e => { e.waitUntil(caches.open(CACHE).then(c => c.addAll(['/offline.html', '/icon.svg']))); self.skipWaiting(); });
self.addEventListener('activate', e => e.waitUntil(caches.keys().then(keys => Promise.all(keys.filter(k => k !== CACHE).map(k => caches.delete(k))))));
// Never cache authenticated HTML, API responses, documents or submitted forms.
self.addEventListener('fetch', e => { if (e.request.mode === 'navigate' && e.request.method === 'GET') e.respondWith(fetch(e.request).catch(() => caches.match('/offline.html'))); });
