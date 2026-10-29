export class ApiError extends Error {
  constructor(status, code, message) {
    super(message);
    this.name = 'ApiError';
    this.status = status;
    this.code = code;
  }
}

export class ApiClient {
  constructor(baseUrl, session) {
    this.baseUrl = baseUrl.replace(/\/$/, '');
    this.session = session;
  }

  login(email, password) {
    return this.#request('POST', '/auth/login', { email, password }, { auth: false });
  }

  getProducts({ page = 1, pageSize = 10, sortBy = 'name', sortDirection = 'asc' } = {}, { signal } = {}) {
    const query = new URLSearchParams({ page, pageSize, sortBy, sortDirection });
    return this.#request('GET', `/products?${query}`, undefined, { auth: false, signal });
  }

  listOrders({ page = 1, pageSize = 20 } = {}) {
    return this.#request('GET', `/orders?${new URLSearchParams({ page, pageSize })}`);
  }

  getOrder(id) {
    return this.#request('GET', `/orders/${encodeURIComponent(id)}`);
  }

  createOrder(order) {
    return this.#request('POST', '/orders', order);
  }

  updateOrder(id, changes) {
    return this.#request('PATCH', `/orders/${encodeURIComponent(id)}`, changes);
  }

  placeOrder(id) {
    return this.#request('POST', `/orders/${encodeURIComponent(id)}/place`);
  }

  deleteOrder(id) {
    return this.#request('DELETE', `/orders/${encodeURIComponent(id)}`);
  }

  async #request(method, path, body, { auth = true, signal } = {}) {
    const headers = { Accept: 'application/json' };
    if (body !== undefined) headers['Content-Type'] = 'application/json';
    if (auth && this.session.token) headers.Authorization = `Bearer ${this.session.token}`;

    let response;
    try {
      response = await fetch(`${this.baseUrl}${path}`, {
        method,
        headers,
        body: body === undefined ? undefined : JSON.stringify(body),
        signal,
      });
    } catch (err) {
      if (err.name === 'AbortError') throw err;
      throw new ApiError(0, 'network_error', 'Cannot reach the server. Is the API running?');
    }

    if (response.status === 204) return null;

    const payload = await response.json().catch(() => null);

    if (!response.ok) {
      if (response.status === 401 && auth) this.session.clear();
      throw new ApiError(
        response.status,
        payload?.code ?? `http_${response.status}`,
        payload?.detail ?? payload?.title ?? `Request failed with status ${response.status}.`,
      );
    }

    return payload;
  }
}
