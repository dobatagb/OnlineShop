import { api } from '../app-context.js';
import { escapeHtml, formatDate, formatMoney, sharedStyles } from '../shared/ui.js';

const styles = new CSSStyleSheet();
styles.replaceSync(`
  .meta { margin: -.5rem 0 1rem; }
  .footer { margin-top: 1rem; align-items: flex-start; }
  .actions { margin-top: 1.25rem; border-top: 1px solid #eaeef2; padding-top: 1rem; }
`);

class OrderDetail extends HTMLElement {
  static observedAttributes = ['order-id'];

  #order = null;
  #error = null;
  #notice = null;
  #busy = false;

  constructor() {
    super();
    this.attachShadow({ mode: 'open' }).adoptedStyleSheets = [sharedStyles, styles];
    this.shadowRoot.addEventListener('click', (e) => this.#onClick(e));
  }

  attributeChangedCallback() {
    if (this.isConnected) this.#load();
  }

  connectedCallback() {
    this.#load();
  }

  get #orderId() {
    return this.getAttribute('order-id');
  }

  async #load() {
    this.#order = null;
    await this.#run(async () => {
      this.#order = await api.getOrder(this.#orderId);
    });
  }

  #onClick(event) {
    const button = event.target.closest('[data-action]');
    if (!button || this.#busy) return;

    switch (button.dataset.action) {
      case 'save':
        return this.#update({ itemsMode: 'Replace', items: this.#readQuantities() }, 'Order updated.');
      case 'remove-line':
        return this.#update({ items: [{ productId: button.dataset.id, quantity: 0 }] }, 'Product removed.');
      case 'place':
        return this.#run(async () => {
          this.#order = await api.placeOrder(this.#orderId);
          this.#notice = 'Order placed.';
        });
      case 'delete':
        if (!confirm('Delete this order? Reserved stock will be released.')) return;
        return this.#run(async () => {
          await api.deleteOrder(this.#orderId);
          this.dispatchEvent(new CustomEvent('order-deleted', { detail: { id: this.#orderId }, bubbles: true, composed: true }));
        });
    }
  }

  #readQuantities() {
    return [...this.shadowRoot.querySelectorAll('input[name=quantity]')].map((input) => ({
      productId: input.dataset.id,
      quantity: Math.max(0, Number.parseInt(input.value, 10) || 0),
    }));
  }

  #update(changes, notice) {
    return this.#run(async () => {
      this.#order = await api.updateOrder(this.#orderId, changes);
      this.#notice = notice;
    });
  }

  async #run(action) {
    this.#busy = true;
    this.#error = null;
    this.#notice = null;
    this.#render();
    try {
      await action();
    } catch (err) {
      this.#error = err.message;
    } finally {
      this.#busy = false;
      if (this.isConnected) this.#render();
    }
  }

  #render() {
    const order = this.#order;
    const messages = `
      ${this.#error ? `<p class="error">${escapeHtml(this.#error)}</p>` : ''}
      ${this.#notice ? `<p class="notice">${escapeHtml(this.#notice)}</p>` : ''}`;

    if (!order) {
      this.shadowRoot.innerHTML = `
        <section class="card">
          <h2>Order</h2>
          ${messages}
          ${this.#busy ? '<p class="muted">Loading…</p>' : ''}
          <p><a href="#/orders">← Back to my orders</a></p>
        </section>`;
      return;
    }

    const editable = order.status === 'Draft';
    const disabled = this.#busy ? 'disabled' : '';

    const rows = order.items.map((i) => `
      <tr>
        <td><strong>${escapeHtml(i.productName)}</strong><div class="muted">${escapeHtml(i.sku)}</div></td>
        <td class="num">${formatMoney(i.unitPrice)}</td>
        <td class="num">${editable
          ? `<input type="number" name="quantity" min="0" max="100" data-id="${escapeHtml(i.productId)}" value="${i.quantity}" aria-label="Quantity of ${escapeHtml(i.productName)}" ${disabled}>`
          : i.quantity}</td>
        <td class="num">${formatMoney(i.lineTotal)}</td>
        ${editable ? `<td class="num"><button class="link danger" data-action="remove-line" data-id="${escapeHtml(i.productId)}" ${disabled}>Remove</button></td>` : ''}
      </tr>`).join('');

    this.shadowRoot.innerHTML = `
      <section class="card">
        <div class="row">
          <h2>Order ${escapeHtml(order.id.slice(-8))}</h2>
          <span class="badge ${escapeHtml(order.status)}">${escapeHtml(order.status)}</span>
          <span class="spacer"></span>
          <a href="#/orders">← My orders</a>
        </div>
        <p class="muted meta">Created ${formatDate(order.createdAt)} · Updated ${formatDate(order.updatedAt)}
          ${order.placedAt ? ` · Placed ${formatDate(order.placedAt)}` : ''}</p>
        ${messages}

        <div class="table-wrap">
          <table>
            <thead><tr><th>Product</th><th class="num">Unit price</th><th class="num">Qty</th><th class="num">Line total</th>${editable ? '<th></th>' : ''}</tr></thead>
            <tbody>${rows}</tbody>
          </table>
        </div>

        <div class="row footer">
          <div class="totals">
            <div><span>Subtotal</span><span>${formatMoney(order.subtotal)}</span></div>
            ${order.discount > 0 ? `<div><span>Discount</span><span>−${formatMoney(order.discount)}</span></div>` : ''}
            <div class="grand"><span>Total</span><span>${formatMoney(order.total)}</span></div>
          </div>
        </div>

        <div class="row actions">
          ${editable ? `
            <button data-action="save" ${disabled}>Save quantities</button>
            <button class="primary" data-action="place" ${disabled}>Place order</button>` : ''}
          <span class="spacer"></span>
          <button class="danger" data-action="delete" ${disabled}>Delete order</button>
        </div>
      </section>`;
  }
}

customElements.define('order-detail', OrderDetail);
