// Applies the stored color scheme before the app loads, so a light page never flashes dark or the
// other way round. Must match the key and values in src/app/theme.tsx.
(function () {
  var scheme = 'system';
  try {
    scheme = localStorage.getItem('autocanvas.theme') || 'system';
  } catch {
    // Storage can be blocked; the system setting applies then.
  }
  if (scheme !== 'light' && scheme !== 'dark') {
    scheme = window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light';
  }
  document.documentElement.dataset.theme = scheme;
})();
