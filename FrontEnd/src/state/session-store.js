const STORAGE_KEY = 'shop.session';

export class SessionStore extends EventTarget {
  #session = null;

  constructor() {
    super();
    this.#session = this.#load();
  }

  get token() {
    return this.isAuthenticated ? this.#session.accessToken : null;
  }

  get user() {
    return this.isAuthenticated ? { email: this.#session.email, displayName: this.#session.displayName } : null;
  }

  get isAuthenticated() {
    return this.#session !== null && new Date(this.#session.expiresAt) > new Date();
  }

  start(loginResponse) {
    this.#session = {
      accessToken: loginResponse.accessToken,
      expiresAt: loginResponse.expiresAt,
      email: loginResponse.email,
      displayName: loginResponse.displayName,
    };
    this.#save();
    this.dispatchEvent(new Event('change'));
  }

  clear() {
    if (this.#session === null) return;
    this.#session = null;
    this.#save();
    this.dispatchEvent(new Event('change'));
  }

  #load() {
    try {
      const raw = sessionStorage.getItem(STORAGE_KEY);
      return raw ? JSON.parse(raw) : null;
    } catch {
      return null;
    }
  }

  #save() {
    try {
      if (this.#session) sessionStorage.setItem(STORAGE_KEY, JSON.stringify(this.#session));
      else sessionStorage.removeItem(STORAGE_KEY);
    } catch {
    }
  }
}
