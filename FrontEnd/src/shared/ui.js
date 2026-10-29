const escapes = { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' };

export function escapeHtml(value) {
  return String(value ?? '').replace(/[&<>"']/g, (c) => escapes[c]);
}

const currency = new Intl.NumberFormat('en-US', { style: 'currency', currency: 'EUR' });

export function formatMoney(amount) {
  return currency.format(Number(amount));
}

export function formatDate(iso) {
  return iso ? new Date(iso).toLocaleString() : '';
}

export const sharedStyles = new CSSStyleSheet();
sharedStyles.replaceSync(`
  :host { display: block; }
  * { box-sizing: border-box; }
  h2 { margin: 0 0 1rem; font-size: 1.25rem; }
  .card { background: #fff; border: 1px solid #d8dee4; border-radius: 8px; padding: 1rem 1.25rem; }
  table { width: 100%; border-collapse: collapse; }
  th, td { text-align: left; padding: .5rem; border-bottom: 1px solid #eaeef2; vertical-align: middle; }
  th { font-size: .8rem; text-transform: uppercase; letter-spacing: .03em; color: #57606a; }
  td.num, th.num { text-align: right; }
  button { font: inherit; padding: .4rem .9rem; border-radius: 6px; border: 1px solid #d0d7de;
           background: #f6f8fa; cursor: pointer; }
  button:hover:not(:disabled) { background: #eef1f4; }
  button:disabled { opacity: .55; cursor: not-allowed; }
  button.primary { background: #1f6feb; border-color: #1f6feb; color: #fff; }
  button.primary:hover:not(:disabled) { background: #1a5fd0; }
  button.danger { color: #cf222e; }
  button.link { border: none; background: none; color: #0969da; padding: 0; }
  input, select { font: inherit; padding: .35rem .5rem; border: 1px solid #d0d7de; border-radius: 6px; }
  input[type=number] { width: 5rem; }
  .row { display: flex; gap: .75rem; align-items: center; flex-wrap: wrap; }
  .spacer { flex: 1; }
  .muted { color: #57606a; font-size: .9rem; }
  .error { color: #cf222e; background: #ffebe9; border: 1px solid #ffcecb; border-radius: 6px; padding: .5rem .75rem; }
  .notice { color: #1a7f37; background: #dafbe1; border: 1px solid #aceebb; border-radius: 6px; padding: .5rem .75rem; }
  .badge { display: inline-block; padding: .1rem .5rem; border-radius: 999px; font-size: .8rem; background: #eaeef2; }
  .badge.Draft { background: #fff8c5; }
  .badge.Placed { background: #dafbe1; }
  .badge.Expired { background: #ffebe9; }
  .totals { margin-left: auto; min-width: 16rem; }
  .totals div { display: flex; justify-content: space-between; padding: .2rem 0; }
  .totals .grand { font-weight: 600; border-top: 1px solid #d8dee4; margin-top: .3rem; padding-top: .4rem; }
  .table-wrap { overflow-x: auto; }
`);
