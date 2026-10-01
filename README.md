# AgroTrade Backend

Minimal ASP.NET Core Web API for the AgroTrade backend.

## Open in Visual Studio

Open `AgroTrade.sln`, select `AgroTrade.Api` as the startup project if needed, then press Run.

## Run from terminal

```powershell
dotnet run --project src/AgroTrade.Api/AgroTrade.Api.csproj
```

Swagger opens in development at:

```text
/swagger
```

Test endpoint:

```text
GET /api/health
```

## Database

PostgreSQL is used for the local database. Keep local secrets in:

```text
src/AgroTrade.Api/appsettings.Development.json
```

Example connection string:

```text
Host=localhost;Port=5432;Database=AgroTradeDb;Username=postgres;Password=YOUR_PASSWORD
```

## Products

Swagger endpoints:

```text
GET /api/products
GET /api/products?search=apple
GET /api/products/{id}
POST /api/products
```

`GET /api/products` returns locally managed products. Use `search` to search by name, SKU, or categories.

Local product create body:

```json
{
  "name": "Local apple",
  "slug": "local-apple",
  "sku": "APL-001",
  "price": 12.5,
  "regularPrice": 15,
  "salePrice": null,
  "stockStatus": "instock",
  "stockQuantity": 20,
  "description": "Full product description",
  "shortDescription": "Short description",
  "imageUrl": "https://example.com/apple.jpg",
  "categoryNames": "Fruits"
}
```

## Liberty TXPG Payments

Liberty TXPG credentials are saved with .NET user-secrets or server environment variables, not in `appsettings.json`.

Local secret keys:

```powershell
dotnet user-secrets set "LibertyTxpg:TerminalId" "YOUR_TERMINAL_ID" --project src/AgroTrade.Api/AgroTrade.Api.csproj
dotnet user-secrets set "LibertyTxpg:BasicAuthUsername" "YOUR_USERNAME" --project src/AgroTrade.Api/AgroTrade.Api.csproj
dotnet user-secrets set "LibertyTxpg:BasicAuthPassword" "YOUR_PASSWORD" --project src/AgroTrade.Api/AgroTrade.Api.csproj
```

Payment flow:

```text
POST /api/orders
POST /api/payments/liberty/orders/{orderId}/start
GET /api/payments/liberty/approve
GET /api/payments/liberty/decline
GET /api/payments/liberty/cancel
```

Give Liberty these URLs after replacing the domain:

```text
Approve URL: https://YOUR-API-DOMAIN/api/payments/liberty/approve
Decline URL: https://YOUR-API-DOMAIN/api/payments/liberty/decline
Cancel URL:  https://YOUR-API-DOMAIN/api/payments/liberty/cancel
```
