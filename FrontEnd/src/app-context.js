import { ApiClient } from './services/api-client.js';
import { SessionStore } from './state/session-store.js';

export const session = new SessionStore();
export const api = new ApiClient(window.SHOP_CONFIG?.apiBaseUrl ?? 'http://localhost:5080', session);

export function navigate(path) {
  location.hash = `#${path}`;
}
