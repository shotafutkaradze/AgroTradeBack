
using AgroTrade.Application;
using AgroTrade.Api;
using AgroTrade.Domain.Enums;
using AgroTrade.Infrastructure;
using AgroTrade.Infrastructure.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.FileProviders;

var builder = WebApplication.CreateBuilder(args);



builder.Services.AddSingleton<UploadStorage>();

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddApplication();



builder.Services
    .AddAuthentication("Bearer")
    .AddScheme<AuthenticationSchemeOptions,
        AppTokenAuthenticationHandler>("Bearer", _ => { });


builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(
        AuthorizationPolicies.AdminOrManager,
        policy => policy.RequireRole(
            UserRole.Admin.ToValue(),
            UserRole.Manager.ToValue()
        )
    );
});

builder.Services.AddCors(options =>
{
    options.AddPolicy("FixtopCors", policy =>
    {
        policy
            .WithOrigins(
                "https://fixtop.ge",
                "https://www.fixtop.ge",
                "http://fixtop.ge",
                "http://www.fixtop.ge"
            )
            .AllowAnyHeader()
            .AllowAnyMethod();
        if (builder.Environment.IsDevelopment())
            policy.WithOrigins("http://localhost:5173", "http://127.0.0.1:5173");
    });
});

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new()
    {
        Title = "AgroTrade API",
        Version = "v1",
        Description = "Backend API for the AgroTrade application."
    });
});

var app = builder.Build();

var uploadStorage = app.Services.GetRequiredService<UploadStorage>();
var copiedUploads = uploadStorage.Initialize();
app.Logger.LogInformation("Upload storage ready at {UploadsPath}; copied {Count} legacy files", uploadStorage.RootPath, copiedUploads);

await app.Services.MigrateDatabaseAsync();


if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(uploadStorage.RootPath),
    RequestPath = "/uploads"
});
app.UseStaticFiles();

app.UseRouting();

// CORS must run before authentication
app.UseCors("FixtopCors");

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
