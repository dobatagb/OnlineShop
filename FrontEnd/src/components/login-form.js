import { api, session } from '../app-context.js';
import { escapeHtml, sharedStyles } from '../shared/ui.js';

const styles = new CSSStyleSheet();
styles.replaceSync(`
  .card { max-width: 380px; margin: 4rem auto; }
  label { display: block; margin-bottom: .9rem; font-size: .9rem; }
  input { display: block; width: 100%; margin-top: .3rem; }
  button { width: 100%; }
`);

class LoginForm extends HTMLElement {
  constructor() {
    super();
    this.attachShadow({ mode: 'open' }).adoptedStyleSheets = [sharedStyles, styles];
  }

  connectedCallback() {
    this.shadowRoot.innerHTML = `
      <form class="card" novalidate>
        <h2>Sign in</h2>
        <label>Email <input name="email" type="email" autocomplete="username" required value="demo@shop.test"></label>
        <label>Password <input name="password" type="password" autocomplete="current-password" required></label>
        <p class="error" hidden></p>
        <button class="primary" type="submit">Sign in</button>
        <p class="muted">Demo user: demo@shop.test / Demo123!</p>
      </form>`;

    this.shadowRoot.querySelector('form').addEventListener('submit', (e) => this.#submit(e));
  }

  async #submit(event) {
    event.preventDefault();
    const form = event.target;
    const button = form.querySelector('button');
    const error = form.querySelector('.error');

    const email = form.email.value.trim();
    const password = form.password.value;
    if (!email || !password) {
      this.#showError(error, 'Email and password are required.');
      return;
    }

    button.disabled = true;
    error.hidden = true;
    try {
      session.start(await api.login(email, password));
    } catch (err) {
      this.#showError(error, err.message);
      button.disabled = false;
    }
  }

  #showError(element, message) {
    element.innerHTML = escapeHtml(message);
    element.hidden = false;
  }
}

customElements.define('login-form', LoginForm);
