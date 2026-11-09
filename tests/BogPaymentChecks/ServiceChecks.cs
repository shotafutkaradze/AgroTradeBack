using System.Collections;
using System.Linq.Expressions;
using AgroTrade.Application.Abstractions;
using AgroTrade.Application.Services;
using AgroTrade.Contracts.ActivityLogs;
using AgroTrade.Contracts.Orders;
using AgroTrade.Contracts.Payments;
using AgroTrade.Domain.Enums;
using AgroTrade.Domain.Models;
using AgroTrade.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

static class ServiceChecks
{
    static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    public static async Task RunAsync()
    {
        await RunLibertyAsync();
        var db = new MemoryStore();
        var email = new Emails();
        var log = new Logs();
        db.Products.Add(new Product { Id = 7, Name = "Database product", Price = 100, StockQuantity = 10, StockStatus = StockStatus.InStock });
        var orders = new OrderService(db, log, email, NullLogger<OrderService>.Instance);
        var request = new CreateOrderRequest(null, new("First", "Last", "555", null),
            new("pickup", null, null, "tbilisi", null), "bog_installment_plan", 999,
            [new(7, "Spoofed", null, null, null, 0.01m, 2)]);
        var quote = await orders.QuoteBogAsync(request, default);
        Check(quote.Total == 200 && ((MemorySet<Order>)db.Orders).Rows.Count == 0 && ((MemorySet<Product>)db.Products).Rows[0].StockQuantity == 10,
            "Quote must use database prices without creating orders or reserving stock");
        var created = await orders.CreateAsync(request, default);
        Check(created.Total == 200 && created.Items[0].UnitPrice == 100 && created.Items[0].ProductName == "Database product", "Database price/name must override frontend");
        Check(created.PaymentAccessToken?.Length == 64, "Checkout capability missing");
        var bank = new Bank();
        var payments = new PaymentService(db, null!, bank, null!, email, log, NullLogger<PaymentService>.Instance,
            Options.Create(new LibertyPayOptions()), Options.Create(new BogInstallmentOptions()));
        try { await payments.StartBogInstallmentAsync(created.Id, new(4, "standard", "wrong"), default); throw new Exception("Unauthorized checkout accepted"); }
        catch (UnauthorizedAccessException) { }
        try { await payments.GetBogStatusAsync(created.Id, "wrong", default); throw new Exception("Unauthorized status read accepted"); }
        catch (UnauthorizedAccessException) { }
        var start = new BogInstallmentPaymentStartRequest(4, "standard", created.PaymentAccessToken);
        await payments.StartBogInstallmentAsync(created.Id, start, default);
        await payments.StartBogInstallmentAsync(created.Id, start, default);
        Check(bank.Creates == 1 && bank.Key.HasValue, "Repeated start must reuse persisted bank order/key");
        var callback = new BogInstallmentCallbackRequest("completed", "bank-id", created.OrderNumber, "bog_loan", 200, "GEL");
        bank.Details = callback with { Status = "processing" };
        var result = await payments.HandleBogInstallmentCallbackAsync(callback, default);
        Check(result?.PaymentStatus == "pending" && email.Count == 0, "Callback must not override pending bank receipt");
        bank.Details = callback with { Status = "rejected" };
        result = await payments.HandleBogInstallmentCallbackAsync(callback, default);
        Check(result?.PaymentStatus == "failed" && email.Count == 0, "Rejected application counted as paid");
        bank.Details = callback with { Amount = 1, TransferredAmount = 200 };
        try { await payments.HandleBogInstallmentCallbackAsync(callback, default); throw new Exception("Amount mismatch accepted"); }
        catch (InvalidOperationException) { }
        bank.Details = callback with { TransferredAmount = 1 };
        try { await payments.HandleBogInstallmentCallbackAsync(callback, default); throw new Exception("Transfer amount mismatch accepted"); }
        catch (InvalidOperationException) { }
        bank.Details = callback with { TransferredAmount = 200 };
        result = await payments.HandleBogInstallmentCallbackAsync(callback, default);
        Check(result?.PaymentStatus == "paid" && email.Count == 1, "Confirmed payment must be applied once");
        await payments.HandleBogInstallmentCallbackAsync(callback, default);
        Check(email.Count == 1, "Duplicate callback resent email");
        bank.Details = callback with { Status = "processing" };
        result = await payments.HandleBogInstallmentCallbackAsync(callback, default);
        Check(result?.PaymentStatus == "paid", "Intermediate receipt downgraded paid order");
        bank.Details = callback with { Status = "refunded_partially", RefundedAmount = 50 };
        result = await payments.HandleBogInstallmentCallbackAsync(callback, default);
        Check(result?.PaymentStatus == "partially_refunded", "Partial refund status missing");

        using var context = new AgroTradeDbContext(new DbContextOptionsBuilder<AgroTradeDbContext>()
            .UseNpgsql("Host=localhost;Database=unused;Username=unused;Password=unused").Options);
        var assembly = context.GetService<IMigrationsAssembly>();
        Check(assembly.Migrations.ContainsKey("20261109140000_BogPaymentManager"), "BOG migration missing");
        var snapshot = context.GetService<IModelRuntimeInitializer>().Initialize(assembly.ModelSnapshot!.Model, designTime: true);
        var differences = context.GetService<IMigrationsModelDiffer>().GetDifferences(snapshot.GetRelationalModel(),
            context.GetService<IDesignTimeModel>().Model.GetRelationalModel());
        Check(differences.Count == 0, "EF snapshot differs from current model");
        var sql = context.GetService<IMigrator>().GenerateScript("20261005100000_CategoryImageUrl", "20261109140000_BogPaymentManager");
        Check(sql.Contains("CheckoutTokenHash") && sql.Contains("Version"), "Migration SQL generation failed");
        Console.WriteLine("Service checks passed: trusted prices, access control, idempotency, receipt reconciliation, duplicates, refunds, and migration SQL/model.");
    }

    static async Task RunLibertyAsync()
    {
        var db = new MemoryStore(); var emails = new Emails(); var logs = new Logs();
        db.Products.Add(new Product { Id = 7, Name = "Product", Price = 100, StockQuantity = 10, StockStatus = StockStatus.InStock });
        var orders = new OrderService(db, logs, emails, NullLogger<OrderService>.Instance);
        var orderDto = await orders.CreateAsync(new CreateOrderRequest(null, new("First", "Last", "555", null),
            new("pickup", null, null, "tbilisi", null), "liberty", 0, [new(7, "Spoofed", null, null, null, 0.01m, 2)]), default);
        Check(orderDto.Total == 200 && orderDto.PaymentAccessToken is not null, "Liberty must use trusted database prices and a checkout capability");
        var order = ((MemorySet<Order>)db.Orders).Rows.Single();
        var payment = order.Payments.Single(); payment.OrderId = order.Id; db.Payments.Add(payment);
        var settings = new LibertyPayOptions { Merchant = "test-merchant", Key = "test-key", TestMode = "1", PublicApiBaseUrl = "https://api.example" };
        var service = new PaymentService(db, null!, null!, null!, emails, logs, NullLogger<PaymentService>.Instance,
            Options.Create(settings), Options.Create(new BogInstallmentOptions()));
        var start = new LibertyPayPaymentStartRequest(null, orderDto.PaymentAccessToken);
        await service.StartLibertyPaymentAsync(order.Id, start, default);
        await service.StartLibertyPaymentAsync(order.Id, start, default);
        Check(((MemorySet<Payment>)db.Payments).Rows.Count == 1, "Repeated Liberty start created duplicate payment");
        await service.HandleLibertyApproveAsync(order.OrderNumber, "forged", default);
        Check(order.PaymentStatus == PaymentStatus.Pending && emails.Count == 0, "Unsigned approve marked order paid");
        LibertyPayCallbackRequest Sign(LibertyPayCallbackRequest request)
        {
            var value = request.Status + request.TransactionCode + request.Amount + request.Currency + request.OrderCode + request.PayMethod +
                (request.PayMethod == "PAYTERM" ? request.PayedAmount : "") + request.CustomData + request.TestMode + settings.Key;
            return request with { Check = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value))).ToLowerInvariant() };
        }
        var callback = new LibertyPayCallbackRequest("CHECK", "txn", null, "20000", "GEL", order.OrderNumber, "CARD", order.Id.ToString(), null, "1", null);
        var result = await service.HandleLibertyCallbackAsync(Sign(callback), default);
        Check(result.ResultCode == "0" && order.PaymentStatus == PaymentStatus.PendingCallback && emails.Count == 0, "CHECK must not confirm payment");
        await service.StartLibertyPaymentAsync(order.Id, start, default);
        Check(order.PaymentStatus == PaymentStatus.PendingCallback, "Repeated start downgraded callback state");
        result = await service.HandleLibertyCallbackAsync(Sign(callback with { Status = "COMPLETED", Currency = "USD" }), default);
        Check(result.ResultCode == "-3" && payment.Status != PaymentStatus.Paid, "Wrong Liberty currency accepted");
        result = await service.HandleLibertyCallbackAsync(Sign(callback with { Status = "COMPLETED", TestMode = "0" }), default);
        Check(result.ResultCode == "-3", "Wrong test/live mode accepted");
        result = await service.HandleLibertyCallbackAsync(Sign(callback with { Status = "COMPLETED", Amount = "1" }), default);
        Check(result.ResultCode == "-3", "Wrong amount accepted");
        result = await service.HandleLibertyCallbackAsync(callback with { Status = "COMPLETED", Check = new string('0', 64) }, default);
        Check(result.ResultCode == "-3", "Invalid callback signature accepted");
        result = await service.HandleLibertyCallbackAsync(Sign(callback with { Status = "COMPLETED" }), default);
        Check(result.ResultCode == "0" && payment.Status == PaymentStatus.Paid && emails.Count == 1, "Signed completed callback must confirm once");
        result = await service.HandleLibertyCallbackAsync(Sign(callback with { Status = "COMPLETED" }), default);
        Check(result.ResultCode == "1" && emails.Count == 1, "Duplicate Liberty callback resent email");
        await service.HandleLibertyDeclineAsync(order.OrderNumber, default);
        await service.HandleLibertyCancelAsync(order.OrderNumber, default);
        Check(payment.Status == PaymentStatus.Paid && order.PaymentStatus == PaymentStatus.Paid, "Return URL downgraded paid order");
        payment.ProviderRefundStatus = "REQUESTED";
        try { await service.RefundLibertyPaymentAsync(order.Id, new RefundPaymentRequest(50, null), default); throw new Exception("Pending refund allowed a duplicate request"); }
        catch (InvalidOperationException) { }
        Console.WriteLine("Liberty checks passed: database prices, repeated start, unsigned returns, CHECK, signature/amount/currency/mode, completed callback and duplicates.");
    }

    sealed class Bank : IBogInstallmentClient
    {
        public int Creates; public Guid? Key;
        public BogInstallmentCallbackRequest Details = null!;
        public Task<BogInstallmentOrderResult> CreateOrderAsync(Order order, int month, string type, CancellationToken ct, Guid? idempotencyKey = null)
        { Creates++; Key = idempotencyKey; return Task.FromResult(new BogInstallmentOrderResult("bank-id", "created", "https://payment.bog.ge/test")); }
        public Task<BogInstallmentCallbackRequest> GetPaymentDetailsAsync(string id, CancellationToken ct) => Task.FromResult(Details);
        public BogInstallmentCallbackRequest? VerifyCallback(byte[] body, string signature) => null;
    }
    sealed class Emails : IOrderEmailService
    {
        public int Count;
        public Task SendOrderCreatedAsync(OrderDto order, CancellationToken ct) { Count++; return Task.CompletedTask; }
    }
    sealed class Logs : IActivityLogService
    {
        public Task AddAsync(ActivityLogEntry entry, CancellationToken ct) => Task.CompletedTask;
        public Task<IReadOnlyList<ActivityLogDto>> GetImportantAsync(int take, CancellationToken ct) => throw new NotSupportedException();
    }
}

// Async query doubles exercise application behavior; they do not simulate PostgreSQL transactions.
sealed class MemoryStore : IAgroTradeDbContext
{
    public DbSet<Product> Products { get; } = new MemorySet<Product>();
    public DbSet<Order> Orders { get; } = new MemorySet<Order>();
    public DbSet<Payment> Payments { get; } = new MemorySet<Payment>();
    public DbSet<OrderItem> OrderItems => throw new NotSupportedException();
    public DbSet<Brand> Brands => throw new NotSupportedException();
    public DbSet<Category> Categories => throw new NotSupportedException();
    public DbSet<AppUser> AppUsers => throw new NotSupportedException();
    public DbSet<ActivityLog> ActivityLogs { get; } = new MemorySet<ActivityLog>();
    public Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        foreach (var order in ((MemorySet<Order>)Orders).Rows) if (order.Id == 0) order.Id = 1;
        return Task.FromResult(1);
    }
}
sealed class MemorySet<T> : DbSet<T>, IQueryable<T>, IAsyncEnumerable<T> where T : class
{
    public override IEntityType EntityType => throw new NotSupportedException();
    public List<T> Rows = [];
    IQueryable<T> Query => new AsyncQuery<T>(Rows);
    public override EntityEntry<T> Add(T entity) { Rows.Add(entity); return null!; }
    public Type ElementType => typeof(T);
    public Expression Expression => Query.Expression;
    public IQueryProvider Provider => Query.Provider;
    public override IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken ct = default) => new AsyncIterator<T>(Rows.GetEnumerator());
    IEnumerator<T> IEnumerable<T>.GetEnumerator() => Rows.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => Rows.GetEnumerator();
}
sealed class AsyncQuery<T> : EnumerableQuery<T>, IAsyncEnumerable<T>, IQueryable<T>
{
    public AsyncQuery(IEnumerable<T> rows) : base(rows) { }
    public AsyncQuery(Expression expression) : base(expression) { }
    IQueryProvider IQueryable.Provider => new AsyncProvider(this);
    public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken ct = default) => new AsyncIterator<T>(this.AsEnumerable().GetEnumerator());
}
sealed class AsyncProvider(IQueryProvider inner) : IAsyncQueryProvider
{
    public IQueryable CreateQuery(Expression expression) => throw new NotSupportedException();
    public IQueryable<T> CreateQuery<T>(Expression expression) => new AsyncQuery<T>(expression);
    public object? Execute(Expression expression) => inner.Execute(expression);
    public T Execute<T>(Expression expression) => inner.Execute<T>(expression);
    public T ExecuteAsync<T>(Expression expression, CancellationToken ct = default)
    {
        var valueType = typeof(T).GetGenericArguments()[0];
        var value = typeof(IQueryProvider).GetMethods().Single(m => m.Name == "Execute" && m.IsGenericMethod)
            .MakeGenericMethod(valueType).Invoke(inner, [expression]);
        return (T)typeof(Task).GetMethod(nameof(Task.FromResult))!.MakeGenericMethod(valueType).Invoke(null, [value])!;
    }
}
sealed class AsyncIterator<T>(IEnumerator<T> inner) : IAsyncEnumerator<T>
{
    public T Current => inner.Current;
    public ValueTask<bool> MoveNextAsync() => ValueTask.FromResult(inner.MoveNext());
    public ValueTask DisposeAsync() { inner.Dispose(); return ValueTask.CompletedTask; }
}
