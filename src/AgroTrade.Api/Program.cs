
using AgroTrade.Application;
using AgroTrade.Api;
using AgroTrade.Domain.Enums;
using AgroTrade.Infrastructure;
using AgroTrade.Infrastructure.Services;
using Microsoft.AspNetCore.Authentication;

var builder = WebApplication.CreateBuilder(args);



var webRootPath = builder.Environment.WebRootPath
    ?? Path.Combine(builder.Environment.ContentRootPath, "wwwroot");

builder.Environment.WebRootPath = webRootPath;

Directory.CreateDirectory(
    Path.Combine(webRootPath, "uploads", "products")
);



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

await app.Services.MigrateDatabaseAsync();


if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseStaticFiles();

app.UseRouting();

// CORS must run before authentication
app.UseCors("FixtopCors");

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
