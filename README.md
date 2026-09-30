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

## WooCommerce Import

WooCommerce credentials are saved locally with .NET user-secrets, not inside the project files.

Swagger endpoints:

```text
POST /api/products/sync
GET /api/products
GET /api/products?search=apple
GET /api/products?source=local
GET /api/products/{id}
POST /api/products
```

`POST /api/products/sync` reads products from WooCommerce and saves them to PostgreSQL.

`GET /api/products` returns both WooCommerce and locally created products. Use `search`
to search by name, SKU, or categories, and `source` with `local` or `woocommerce`.

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
