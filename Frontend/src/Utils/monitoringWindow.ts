export function isMonitoringWindowLocation(location: Location = window.location): boolean {
  if (location.hash === '#/monitoring') return true;
  return new URLSearchParams(location.search).get('window') === 'monitoring';
}
