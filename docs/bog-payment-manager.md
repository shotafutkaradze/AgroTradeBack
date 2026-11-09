# BOG Payment Manager: installment and piece by piece

This implementation uses the current Payment Manager API. No BOG card method or deprecated installment API is used. Both merchant services must be enabled (confirmed by the bank).

## Configuration

Set these **backend environment variables** through the hosting provider, IIS application environment, container configuration, or a secrets store. ASP.NET Core does not automatically load a `.env` file.

```text
BogInstallment__ClientId=<Payment Manager client_id>
BogInstallment__SecretKey=<Payment Manager client_secret>
BogInstallment__CallbackUrl=https://<public-backend-host>/api/bog-pay/callback
BogInstallment__FrontendReturnUrl=https://fixtop.ge/
```

Defaults already point to:

```text
BogInstallment__BaseUrl=https://api.bog.ge
BogInstallment__TokenUrl=https://oauth2.bog.ge/auth/realms/bog/protocol/openid-connect/token
BogInstallment__Currency=GEL
BogInstallment__Locale=ka
```

BOG credentials are blank in both appsettings files. `appsettings.Development.json` is ignored by Git; its legacy BOG settings were updated locally too. Keep the secret in backend environment variables. Existing credentials from the deprecated API are not assumed to work.

Set these **Vue build variables** in `agrotrade-front/.env.local` or the frontend build environment:

```text
VITE_API_URL=https://<public-backend-host>
VITE_BOG_CLIENT_ID=<the same client_id as the backend>
```

These values are public. Never put client_secret or a bank access token in a `VITE_*` variable. Rebuild Vue after changing build variables. Allow the frontend origin in the backend CORS policy; production currently includes fixtop.ge and www.fixtop.ge. Local development origins must be allowed explicitly for local browser testing.

## Flow

1. Checkout shows two independent buttons: installment (`bnpl: false`) and piece by piece (`bnpl: true`). Other providers' backend implementations are preserved, but the checkout payment controls show only these two buttons.
2. `POST /api/orders/bog-quote` reads product prices and availability from PostgreSQL and calculates delivery. It creates no order and reserves no stock.
3. Vue opens the official Calculator SDK using that server total.
4. After selection, Vue creates a local order once, then calls `POST /api/bog-pay/orders/{id}/start` with `installmentMonth`, `installmentType` (the exact SDK `discount_code`), `paymentMethod`, and `paymentAccessToken` returned only by order creation. No frontend amount controls the bank request.
5. The backend persists a UUID v4 idempotency key and the selected conditions before calling BOG. It sends only `payment_method: ["bog_loan"]` or `["bnpl"]`, `config.loan.month/type`, the DB-derived basket, and `callback_url`.
6. Vue passes the returned **providerOrderId** to `successCb`. The local integer `orderId` is not the bank order ID.
7. The callback verifies `Callback-Signature` over the original bytes with BOG's published RSA/SHA-256 key, before JSON parsing. The signed body only triggers reconciliation; its status is not trusted as the final state.
8. Backend calls `GET /payments/v1/receipt/{bankOrderId}` with a backend-only access token. Bank ID, external order number, amount, currency, transfer method, and completed transfer amount must match the stored payment. Only `completed` marks the order paid. Created, processing, blocked, and unknown states cannot confirm payment.
9. On return, Vue uses `GET /api/bog-pay/orders/{id}/status` with `X-Payment-Token`; this reconciles the bank receipt again. A return URL or SDK completion alone never confirms a BOG payment. The cart clears after confirmed payment.

The payment access token is a random checkout capability, **not** the BOG access token. PostgreSQL stores only its SHA-256 hash. Vue keeps it in sessionStorage to support same-tab return and retries. Without that session (for example on another device), the public result screen remains unconfirmed; administrators can inspect the order through the existing authenticated order UI.

Loan conditions are fixed once the bank request begins, including after an uncertain network response. A retry must use the same method/month/discount_code so the persisted Idempotency-Key has the same request body. Starting a new application after rejection/expiry is a new checkout; existing historical or legacy pending checkouts do not receive new public access tokens automatically.

## Persistence and duplicate handling

The existing `Payments` table already contains ProviderOrderId, Method, ProviderStatus, UpdatedAt, Amount, and Currency. Migration `20261109140000_BogPaymentManager` adds:

- CheckoutIdempotencyKey (UUID)
- CheckoutTokenHash
- InstallmentMonth and InstallmentType
- RedirectUrl
- Version (EF optimistic concurrency token)

A callback updates an existing payment; it never creates an order or payment. Version checks protect simultaneous start/status/callback updates. EF saves the payment and order in one transaction, so a losing callback cannot overwrite the winning result. Concurrency returns a retryable response. Completed payments cannot be downgraded by a stale intermediate status. Refunds are reconciled to the existing refund statuses; initiating refunds is outside this checkout integration.

The snapshot also includes the already-existing email verification, address, and category image migrations, which had not previously been reflected there. The new migration does not alter those tables.

Stock reservation after local order creation follows the project's existing behavior. Closing the calculator before selecting conditions does not reserve stock. After an application starts, pending/rejected orders still require the merchant's existing stock/order management process; this change does not introduce an automatic reservation expiry job.

Sequential and concurrent payment updates do not create duplicate orders. Email is sent after the winning paid transition. SMTP delivery is not transactional with PostgreSQL; a process crash after payment commit can still require an email retry by operations (there is no transactional email outbox).

## Errors and logging

Invalid callback signature: 401. Missing/wrong checkout capability: 401. Invalid selection: 400. Already-started incompatible conditions or unavailable payment state: 409. Concurrent updates: 409 for the browser or 503 for callbacks. Bank network/timeout/schema failures: 502. An early callback racing the stored bank ID returns 503 so it can be retried. Upstream errors are logged without credentials, access tokens, or raw bank response bodies. BOG HTTP calls have a 20-second timeout.

## Verification

From the backend directory:

```powershell
dotnet build AgroTrade.sln -m:1 -p:UseSharedCompilation=false
dotnet build tests/BogPaymentChecks -m:1 -p:UseSharedCompilation=false
dotnet tests/BogPaymentChecks/bin/Debug/net8.0/BogPaymentChecks.dll
node --test tests/BogPaymentChecks/frontend.test.mjs
```

From the frontend directory:

```powershell
npm run build
```

The checks use fake bank HTTP responses and async query doubles. They cover both request methods, exact discount code preservation, receipt parsing, rejected signatures, tampered frontend prices, quote without stock reservation, checkout access control, repeated start, pending receipt, mismatched amount, confirmed payment, duplicate callback, stale status, partial refund, EF model/snapshot agreement, and migration SQL generation. They do not substitute for executing concurrent PostgreSQL transactions or receiving a real BOG-signed sandbox callback.

## Deployment

1. Back up the production database using the normal deployment procedure.
2. Set backend variables and verify the HTTPS callback is publicly reachable through the proxy/firewall. The callback uses a BOG signature and must not be protected by an interactive login.
3. Apply `20261109140000_BogPaymentManager` before serving checkout requests. The API already runs `Database.MigrateAsync()` on startup; alternatively apply migrations with the project's EF tooling during deployment. Do not start multiple migration runners simultaneously.
4. Set Vue build variables, build, and deploy its dist directory after the backend is ready.
5. Complete a bank-approved test for **each** enabled method. Verify the selected month/code, paid/rejected/pending states, signed callback, repeated delivery, wrong signature, amount mismatch, and return-page status. Never treat a build passing as proof of a bank transaction succeeding.
6. Inspect backend logs and Orders/Payments records; enable production traffic after these end-to-end checks pass.

No live database migration or real bank transaction was executed by the local checks.

## Official sources

- [Calculator SDK](https://api.bog.ge/docs/payments/external-orders/modal)
- [Authentication](https://api.bog.ge/docs/payments/authentication)
- [Create Order](https://api.bog.ge/docs/payments/standard-process/create-order)
- [Callback signature](https://api.bog.ge/docs/payments/standard-process/callback)
- [Payment Details](https://api.bog.ge/docs/payments/standard-process/get-payment-details)

## Changed files

Backend (relative to argrotrade-back):

- `src/AgroTrade.Api/Controllers/BogPayController.cs`
- `src/AgroTrade.Api/Controllers/OrdersController.cs`
- `src/AgroTrade.Api/appsettings.json`
- `src/AgroTrade.Api/appsettings.Development.json (local, ignored by Git)`
- `src/AgroTrade.Application/Abstractions/IBogInstallmentClient.cs`
- `src/AgroTrade.Application/Services/BogInstallmentOptions.cs`
- `src/AgroTrade.Application/Services/BogCheckoutSecurity.cs`
- `src/AgroTrade.Application/Services/OrderService.cs`
- `src/AgroTrade.Application/Services/PaymentService.cs`
- `src/AgroTrade.Contracts/Orders/OrderContracts.cs`
- `src/AgroTrade.Contracts/Payments/PaymentContracts.cs`
- `src/AgroTrade.Domain/Models/Payment.cs`
- `src/AgroTrade.Infrastructure/Data/AgroTradeDbContext.cs`
- `src/AgroTrade.Infrastructure/DependencyInjection.cs`
- `src/AgroTrade.Infrastructure/Services/BogInstallmentClient.cs`
- `src/AgroTrade.Infrastructure/Migrations/20261109140000_BogPaymentManager.cs`
- `src/AgroTrade.Infrastructure/Migrations/AgroTradeDbContextModelSnapshot.cs`
- `docs/bog-payment-manager.md`
- `tests/BogPaymentChecks/BogPaymentChecks.csproj`
- `tests/BogPaymentChecks/Program.cs`
- `tests/BogPaymentChecks/ServiceChecks.cs`
- `tests/BogPaymentChecks/frontend.test.mjs`

Frontend (relative to agrotrade-front):

- `src/App.vue`
- `src/services/bog.js`
- `bog.env.example`

Frontend production output was regenerated with Vite. Local intermediate authoring files are under ignored backend artifacts/bog-front.
