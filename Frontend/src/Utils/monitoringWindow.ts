export function isMonitoringWindowLocation(location: Location = window.location): boolean {
  // Dedicated monitoring.html entry (preferred — does not rely on query/hash).
  const path = location.pathname.replace(/\\/g, '/').toLowerCase();
  if (path.endsWith('/monitoring.html') || path === '/monitoring.html') return true;

  if (location.hash === '#/monitoring') return true;
  return new URLSearchParams(location.search).get('window') === 'monitoring';
}
