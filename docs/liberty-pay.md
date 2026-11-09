# Liberty PAY review and fixes

Liberty uses the existing PAY.ge form POST integration, not the BOG API.

## Fixed

- Browser approve/decline/cancel redirects now read stored status only. They cannot mark orders paid or downgrade an already confirmed payment.
- Only a valid signed callback can confirm payment. Signature, amount, currency, test/live mode, payment method/provider, existing payment and optional customdata are checked. CHECK remains pending; unknown statuses cannot confirm payment.
- Callback updates increment the existing Payment.Version concurrency token. A racing callback returns a retryable 503. Sequential duplicates do not resend the email or create payments.
- Start requires the order's random payment access token and reuses an existing pending payment. Confirmed, refunded, cancelled or failed payments cannot be restarted as fresh charges.
- Liberty orders now use database prices, product availability and server-calculated delivery. The frontend price is not authoritative.
- Refund requires the AdminOrManager authorization policy and a confirmed paid/partially refunded payment. A persisted REQUESTED marker and version check prevent concurrent duplicate refunds. On uncertain network failure this marker remains set: reconcile with the bank before retrying. A known bank rejection clears the marker to the bank's result code.
- Refund request signatures are redacted from activity log bodies.
- Frontend posts to PAY in the same tab, retains the cart until confirmation, preserves checkout retries, and reads the protected backend status endpoint after return. Payment errors have a visible retry dialog.

## Deployment configuration

The checked local appsettings currently use TestMode=1 and localhost URLs. Production must supply:

```text
LibertyPay__Merchant=<merchant supplied by Liberty>
LibertyPay__Key=<secret supplied by Liberty; backend only>
LibertyPay__PublicApiBaseUrl=https://api.fixtop.ge
LibertyPay__FrontendReturnUrl=https://fixtop.ge/
LibertyPay__TestMode=1
```

Public URLs above assume the deployed backend remains api.fixtop.ge. Start with the bank-approved test configuration. Switch TestMode to 0 only for the production merchant configuration after testing. Do not change the secret or test/live mode while an application is awaiting its callback.

Verify the merchant's registered PAY URLs with the bank:

- Callback: `https://api.fixtop.ge/api/liberty-pay/callback`
- Approve: `https://api.fixtop.ge/api/liberty-pay/approve`
- Decline: `https://api.fixtop.ge/api/liberty-pay/decline`
- Cancel: `https://api.fixtop.ge/api/liberty-pay/cancel`

The callback is server-to-server and must be publicly accessible. Bank callbacks cannot reach localhost. Existing payment form URL parameters are preserved; merchant-side registration must agree with the deployed endpoints.

No new migration is needed for this review: it reuses Payment fields introduced previously. Deploy frontend and backend together because new Liberty checkouts now require their payment access token. Legacy pending checkouts without this token need to be recreated; their valid callbacks can still be received.

## Verification and limits

The local console checks cover trusted prices, repeated start, unsigned approve/decline/cancel, CHECK, invalid signature, amount/currency/mode mismatch, completed callback, sequential duplicate callback, and duplicate refund prevention. Existing BOG checks still pass. Vue production build passes.

No real payment, refund, or bank credential validation was executed. Async query doubles are not a live PostgreSQL concurrency test. SMTP and PostgreSQL are not one transaction; email recovery after a process crash is an operations concern.

Official PAY integration manual search excerpt states that browser success return alone does not confirm a payment; callback confirmation is required. The full PDF endpoint returned 404 during this review. Existing local Liberty gateway code was also used to cross-check the form and callback signature conventions.

- https://merchant.pay.ge/Content/Files/PAY%20System%20Integration%20Manual_ENG.pdf
- https://www.libertybank.ge/ka/produqtebi/chemi-biznesistvis/distantsiuri-momsakhureba/e-commerce
