import { navigate, session } from '../app-context.js';
import { escapeHtml, sharedStyles } from '../shared/ui.js';

const styles = new CSSStyleSheet();
styles.replaceSync(`
  header { display: flex; align-items: center; gap: 1.5rem; padding: .75rem 1.5rem;
           background: #24292f; color: #fff; }
  header strong { font-size: 1.1rem; }
  nav a { color: #d0d7de; text-decoration: none; margin-right: 1rem; }
  nav a[aria-current=page] { color: #fff; font-weight: 600; }
  header .user { margin-left: auto; font-size: .9rem; color: #d0d7de; }
  header button { color: #fff; background: transparent; border-color: #57606a; }
  main { max-width: 1100px; margin: 1.5rem auto; padding: 0 1rem; }
`);

class ShopApp extends HTMLElement {
  #onChange = () => this.#render();

  constructor() {
    super();
    this.attachShadow({ mode: 'open' }).adoptedStyleSheets = [sharedStyles, styles];

    this.shadowRoot.addEventListener('order-created', (e) => navigate(`/orders/${e.detail.id}`));
    this.shadowRoot.addEventListener('order-selected', (e) => navigate(`/orders/${e.detail.id}`));
    this.shadowRoot.addEventListener('order-deleted', () => navigate('/orders'));
    this.shadowRoot.addEventListener('click', (e) => {
      if (e.target.closest('[data-action=logout]')) session.clear();
    });
  }

  connectedCallback() {
    session.addEventListener('change', this.#onChange);
    window.addEventListener('hashchange', this.#onChange);
    this.#render();
  }

  disconnectedCallback() {
    session.removeEventListener('change', this.#onChange);
    window.removeEventListener('hashchange', this.#onChange);
  }

  #route() {
    const path = location.hash.replace(/^#/, '') || '/products';
    const orderMatch = path.match(/^\/orders\/([0-9a-f-]{36})$/i);
    if (orderMatch) return { name: 'order', id: orderMatch[1] };
    if (path === '/orders') return { name: 'orders' };
    return { name: 'products' };
  }

  #render() {
    if (!session.isAuthenticated) {
      this.shadowRoot.innerHTML = `<main><login-form></login-form></main>`;
      return;
    }

    const route = this.#route();
    const current = (name) => (route.name === name || (name === 'orders' && route.name === 'order') ? 'aria-current="page"' : '');

    const view = {
      products: `<product-catalog></product-catalog>`,
      orders: `<order-list></order-list>`,
      order: `<order-detail order-id="${escapeHtml(route.id)}"></order-detail>`,
    }[route.name];

    this.shadowRoot.innerHTML = `
      <header>
        <strong>Mini Shop</strong>
        <nav>
          <a href="#/products" ${current('products')}>Products</a>
          <a href="#/orders" ${current('orders')}>My orders</a>
        </nav>
        <span class="user">${escapeHtml(session.user.displayName)} (${escapeHtml(session.user.email)})</span>
        <button data-action="logout">Log out</button>
      </header>
      <main>${view}</main>`;
  }
}

customElements.define('shop-app', ShopApp);
