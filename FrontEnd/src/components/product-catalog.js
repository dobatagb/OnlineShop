import { api } from '../app-context.js';
import { escapeHtml, formatMoney, sharedStyles } from '../shared/ui.js';

const styles = new CSSStyleSheet();
styles.replaceSync(`
  .layout { display: grid; grid-template-columns: 1fr 300px; gap: 1rem; align-items: start; }
  @media (max-width: 800px) { .layout { grid-template-columns: 1fr; } }
  .toolbar { margin-bottom: .75rem; }
  .pager { margin-top: .75rem; }
  .desc { font-size: .85rem; color: #57606a; }
  .out { color: #cf222e; }
  .selection ul { list-style: none; padding: 0; margin: 0 0 .75rem; }
  .selection li { display: flex; justify-content: space-between; gap: .5rem; padding: .25rem 0; }
  .selection input[name=coupon] { width: 100%; margin-bottom: .75rem; }
  .selection button.primary { width: 100%; }
`);

const SORT_OPTIONS = [
  ['name:asc', 'Name (A–Z)'],
  ['name:desc', 'Name (Z–A)'],
  ['price:asc', 'Price (low → high)'],
  ['price:desc', 'Price (high → low)'],
  ['category:asc', 'Category'],
  ['createdAt:desc', 'Newest'],
];

class ProductCatalog extends HTMLElement {
  #query = { page: 1, pageSize: 8, sort: 'name:asc' };
  #data = null;
  #loading = false;
  #submitting = false;
  #selection = new Map();
  #loadController = null;

  constructor() {
    super();
    this.attachShadow({ mode: 'open' }).adoptedStyleSheets = [sharedStyles, styles];
    this.shadowRoot.innerHTML = `
      <div class="layout">
        <section class="card">
          <div class="row toolbar">
            <h2>Products</h2>
            <span class="spacer"></span>
            <label class="muted">Sort
              <select name="sort">
                ${SORT_OPTIONS.map(([value, label]) => `<option value="${value}">${label}</option>`).join('')}
              </select>
            </label>
          </div>
          <p class="error" data-part="list-error" hidden></p>
          <div class="table-wrap">
            <table>
              <thead><tr><th>Product</th><th>SKU / Category</th><th class="num">Price</th><th class="num">In stock</th><th class="num">Qty</th></tr></thead>
              <tbody data-part="rows"></tbody>
            </table>
          </div>
          <div class="row pager" data-part="pager"></div>
        </section>

        <aside class="card selection">
          <h2>New order</h2>
          <div data-part="selection"></div>
          <input name="coupon" placeholder="Coupon code (optional)" aria-label="Coupon code">
          <button class="primary" data-action="create">Create order</button>
          <p class="error" data-part="create-error" hidden></p>
          <p class="muted">Final prices and discounts are calculated by the server.</p>
        </aside>
      </div>`;

    this.shadowRoot.addEventListener('input', (e) => this.#onInput(e));
    this.shadowRoot.addEventListener('change', (e) => this.#onChange(e));
    this.shadowRoot.addEventListener('click', (e) => this.#onClick(e));
  }

  connectedCallback() {
    this.#renderSelection();
    this.#load();
  }

  disconnectedCallback() {
    this.#loadController?.abort();
    this.#loadController = null;
  }

  #part(name) {
    return this.shadowRoot.querySelector(`[data-part="${name}"]`);
  }

  async #load() {
    this.#loadController?.abort();
    const controller = new AbortController();
    this.#loadController = controller;

    const [sortBy, sortDirection] = this.#query.sort.split(':');
    this.#loading = true;
    this.#showError('list-error', null);
    this.#renderProducts();
    try {
      const data = await api.getProducts(
        { page: this.#query.page, pageSize: this.#query.pageSize, sortBy, sortDirection },
        { signal: controller.signal });
      if (controller.signal.aborted) return;
      this.#data = data;
    } catch (err) {
      if (controller.signal.aborted) return;
      this.#showError('list-error', err.message);
    } finally {
      if (this.#loadController === controller) {
        this.#loadController = null;
        this.#loading = false;
        this.#renderProducts();
      }
    }
  }

  #onInput(event) {
    if (event.target.name !== 'quantity') return;
    const input = event.target;
    const product = this.#data.items.find((p) => p.id === input.dataset.id);
    const quantity = Math.max(0, Math.min(Number.parseInt(input.value, 10) || 0, product.stockQuantity));

    if (quantity > 0) this.#selection.set(product.id, { product, quantity });
    else this.#selection.delete(product.id);
    this.#renderSelection();
  }

  #onChange(event) {
    if (event.target.name === 'sort') {
      this.#query = { ...this.#query, sort: event.target.value, page: 1 };
      this.#load();
    } else if (event.target.name === 'quantity') {
      event.target.value = this.#selection.get(event.target.dataset.id)?.quantity ?? 0;
    }
  }

  #onClick(event) {
    const action = event.target.closest('[data-action]')?.dataset.action;
    if (action === 'prev' || action === 'next') {
      this.#query = { ...this.#query, page: this.#query.page + (action === 'next' ? 1 : -1) };
      this.#load();
    } else if (action === 'clear') {
      this.#selection.clear();
      this.#renderSelection();
      this.#renderProducts();
    } else if (action === 'create') {
      this.#createOrder();
    }
  }

  async #createOrder() {
    const couponCode = this.shadowRoot.querySelector('input[name=coupon]').value.trim();
    const items = [...this.#selection.values()].map(({ product, quantity }) => ({ productId: product.id, quantity }));

    this.#submitting = true;
    this.#showError('create-error', null);
    this.#renderSelection();
    try {
      const order = await api.createOrder({ items, couponCode: couponCode || undefined });
      this.#selection.clear();
      this.dispatchEvent(new CustomEvent('order-created', { detail: { id: order.id }, bubbles: true, composed: true }));
    } catch (err) {
      this.#showError('create-error', err.message);
    } finally {
      this.#submitting = false;
      if (this.isConnected) this.#renderSelection();
    }
  }

  #showError(part, message) {
    const element = this.#part(part);
    element.textContent = message ?? '';
    element.hidden = !message;
  }

  #renderProducts() {
    const data = this.#data;
    this.shadowRoot.querySelector('select[name=sort]').value = this.#query.sort;

    const rows = (data?.items ?? []).map((p) => `
      <tr>
        <td><strong>${escapeHtml(p.name)}</strong><div class="desc">${escapeHtml(p.description)}</div></td>
        <td class="muted">${escapeHtml(p.sku)}<br>${escapeHtml(p.category)}</td>
        <td class="num">${formatMoney(p.price)}</td>
        <td class="num ${p.stockQuantity === 0 ? 'out' : ''}">${p.stockQuantity === 0 ? 'Out of stock' : p.stockQuantity}</td>
        <td class="num">
          <input type="number" name="quantity" min="0" max="${p.stockQuantity}" step="1"
                 data-id="${escapeHtml(p.id)}" value="${this.#selection.get(p.id)?.quantity ?? 0}"
                 aria-label="Quantity of ${escapeHtml(p.name)}" ${p.stockQuantity === 0 ? 'disabled' : ''}>
        </td>
      </tr>`).join('');

    this.#part('rows').innerHTML = rows
      || `<tr><td colspan="5" class="muted">${this.#loading ? 'Loading…' : 'No products.'}</td></tr>`;

    this.#part('pager').innerHTML = data ? `
      <button data-action="prev" ${data.page <= 1 || this.#loading ? 'disabled' : ''}>‹ Previous</button>
      <span class="muted">Page ${data.page} of ${data.totalPages} · ${data.totalCount} products</span>
      <button data-action="next" ${data.page >= data.totalPages || this.#loading ? 'disabled' : ''}>Next ›</button>` : '';
  }

  #renderSelection() {
    const selected = [...this.#selection.values()];
    const estimate = selected.reduce((sum, { product, quantity }) => sum + product.price * quantity, 0);

    this.#part('selection').innerHTML = selected.length === 0
      ? '<p class="muted">Set a quantity on one or more products.</p>'
      : `<ul>${selected.map(({ product, quantity }) => `
           <li><span>${quantity} × ${escapeHtml(product.name)}</span><span>${formatMoney(product.price * quantity)}</span></li>`).join('')}
         </ul>
         <p class="row"><span>Estimated subtotal</span><span class="spacer"></span><strong>${formatMoney(estimate)}</strong></p>
         <p><button class="link" data-action="clear">Clear selection</button></p>`;

    const button = this.shadowRoot.querySelector('[data-action=create]');
    button.disabled = selected.length === 0 || this.#submitting;
    button.textContent = this.#submitting ? 'Creating…' : 'Create order';
  }
}

customElements.define('product-catalog', ProductCatalog);
