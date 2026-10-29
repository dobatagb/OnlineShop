import { api } from '../app-context.js';
import { escapeHtml, formatDate, formatMoney, sharedStyles } from '../shared/ui.js';

class OrderList extends HTMLElement {
  constructor() {
    super();
    this.attachShadow({ mode: 'open' }).adoptedStyleSheets = [sharedStyles];
    this.shadowRoot.addEventListener('click', (e) => {
      const id = e.target.closest('[data-id]')?.dataset.id;
      if (id) this.dispatchEvent(new CustomEvent('order-selected', { detail: { id }, bubbles: true, composed: true }));
    });
  }

  connectedCallback() {
    this.#load();
  }

  async #load() {
    this.#render({ loading: true });
    try {
      this.#render({ data: await api.listOrders({ pageSize: 50 }) });
    } catch (err) {
      this.#render({ error: err.message });
    }
  }

  #render({ data, loading, error }) {
    const rows = (data?.items ?? []).map((o) => `
      <tr>
        <td><button class="link" data-id="${escapeHtml(o.id)}">${escapeHtml(o.id.slice(-8))}</button></td>
        <td><span class="badge ${escapeHtml(o.status)}">${escapeHtml(o.status)}</span></td>
        <td class="num">${o.itemCount}</td>
        <td class="num">${formatMoney(o.total)}</td>
        <td>${formatDate(o.createdAt)}</td>
      </tr>`).join('');

    this.shadowRoot.innerHTML = `
      <section class="card">
        <h2>My orders</h2>
        ${error ? `<p class="error">${escapeHtml(error)}</p>` : ''}
        <div class="table-wrap">
          <table>
            <thead><tr><th>Order</th><th>Status</th><th class="num">Items</th><th class="num">Total</th><th>Created</th></tr></thead>
            <tbody>${rows || `<tr><td colspan="5" class="muted">${loading ? 'Loading…' : 'No orders yet. Create one from the product list.'}</td></tr>`}</tbody>
          </table>
        </div>
      </section>`;
  }
}

customElements.define('order-list', OrderList);
