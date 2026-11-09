# Frontend URL navigation

The Vue app now derives its page from the browser URL and writes URLs through the existing History API navigation. No new routing dependency was added.

Routes:

- `/` — home
- `/products` — catalog; `search` and `offers=1` query parameters preserve catalog selection
- `/products/<database-id>/<readable-name>` — product; the database ID is authoritative, so renaming a product does not break its old link
- `/categories/<encoded-category-name>` — filtered catalog
- `/categories`, `/about`, `/contact`, `/services` — existing home page sections
- `/cart`
- `/checkout/details`, `/checkout/payment`
- `/compare?ids=db-1,db-2`
- `/account`, `/admin` — existing account/admin UI; backend authentication policies still apply
- `/payment/result?provider=...&payment=...&order=...` — return result; no checkout capability or bank token is put in the URL

Direct product links wait for products to load before selecting the product. Unknown product/page links show a not-found screen. URL state takes priority over the old session-only navigation snapshot. Refreshing `/checkout/complete` opens payment checkout rather than claiming an unverified successful order. Navigation away from payment results invalidates old polling responses.

Changed frontend files: `src/App.vue`, `src/services/navigation.js`. The existing `public/web.config` already rewrites non-file HTML GET/HEAD requests to index.html for IIS and is included in the built dist output. Deploy the entire new dist directory including web.config. IIS URL Rewrite must be installed/enabled. Other hosting platforms need their equivalent SPA fallback; do not rewrite real assets or API requests to index.html.

Validation: Vue production build; 9 Node checks across navigation and BOG SDK helpers, including Georgian categories/search, stable product IDs, checkout steps, section links, payment return refresh, subdirectory bases and malformed paths. A real browser deployment on the production IIS host was not exercised locally.

Backend-side Node checks: `node --test tests/BogPaymentChecks/navigation.test.mjs tests/BogPaymentChecks/frontend.test.mjs`.
