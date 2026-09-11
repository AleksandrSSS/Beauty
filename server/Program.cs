using Beauty.Server.Data;
using Beauty.Server.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<AppDbContext>(o => o.UseNpgsql(builder.Configuration.GetConnectionString("Default")));

var jwtKey = builder.Configuration["Jwt:Key"];
if (string.IsNullOrWhiteSpace(jwtKey))
    throw new InvalidOperationException(
        "Jwt:Key не задано. У Development збережіть його через 'dotnet user-secrets set \"Jwt:Key\" \"<key>\"' " +
        "або через env-змінну Jwt__Key.");
// S1-6a: HmacSha256 вимагає ключ ≥ 256 біт. Короткий ключ — фатальна помилка конфігурації.
if (Encoding.UTF8.GetByteCount(jwtKey) < 32)
    throw new InvalidOperationException(
        "Jwt:Key закороткий: потрібно щонайменше 32 байти (256 біт) для HmacSha256.");

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidAudience = builder.Configuration["Jwt:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey))
        };
    });

// S2-1: усе закрите за замовчуванням; публічні дії лишаються з явним [AllowAnonymous].
builder.Services.AddAuthorization(o =>
{
    o.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});

// S1-4: rate limiting. Перевищення → 429 у форматі { "error": "..." } (§4.5 AGENTS).
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.OnRejected = async (ctx, token) =>
    {
        ctx.HttpContext.Response.ContentType = "application/json";
        await ctx.HttpContext.Response.WriteAsync(
            "{\"error\":\"Забагато запитів. Спробуйте пізніше.\"}", token);
    };

    // request-code: 5 запитів / 10 хв на телефон+IP.
    o.AddPolicy("request-code", ctx =>
    {
        var phone = ReadPhone(ctx);
        var key = $"{ClientIp(ctx)}|{phone}";
        return RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 5,
            Window = TimeSpan.FromMinutes(10)
        });
    });

    // verify-code: 10 спроб / 10 хв на IP.
    o.AddPolicy("verify-code", ctx =>
        RateLimitPartition.GetFixedWindowLimiter(ClientIp(ctx), _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromMinutes(10)
        }));

    // admin-login: 5 спроб / 15 хв на IP (тимчасовий lockout вікном).
    o.AddPolicy("admin-login", ctx =>
        RateLimitPartition.GetFixedWindowLimiter(ClientIp(ctx), _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 5,
            Window = TimeSpan.FromMinutes(15)
        }));

    static string ClientIp(HttpContext ctx) => ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    static string ReadPhone(HttpContext ctx)
    {
        // Телефон читаємо з query як легкий партиційний ключ; тіло тут недоступне без буферизації.
        return ctx.Request.Query.TryGetValue("phone", out var p) ? p.ToString() : "-";
    }
});

builder.Services.AddHttpClient();
builder.Services.AddSingleton<SalonClock>();
builder.Services.AddScoped<JwtService>();
builder.Services.AddScoped<TokenHasher>();
builder.Services.AddScoped<RefreshTokenService>();
builder.Services.AddScoped<ScheduleService>();
builder.Services.AddScoped<INotificationService, NotificationService>();
builder.Services.AddHostedService<ReminderWorker>();

// CORS: лише дозволені origins клієнта (Cors:AllowedOrigins). AllowCredentials — для refresh-cookie.
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
                     ?? ["http://localhost:5173"];
builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod().AllowCredentials()));

builder.Services.AddControllers().AddJsonOptions(o =>
{
    o.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
    o.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
});

var app = builder.Build();

// S2-2: централізована обробка помилок — 500 з тілом { "error": ... }, без стектрейсу поза Development.
app.UseExceptionHandler(errApp => errApp.Run(async ctx =>
{
    ctx.Response.StatusCode = StatusCodes.Status500InternalServerError;
    ctx.Response.ContentType = "application/json";
    var feature = ctx.Features.Get<IExceptionHandlerFeature>();
    var message = app.Environment.IsDevelopment() && feature?.Error != null
        ? feature.Error.Message
        : "Внутрішня помилка сервера";
    await ctx.Response.WriteAsJsonAsync(new { error = message });
}));

// S2-2: базові security headers.
app.Use(async (ctx, next) =>
{
    ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
    ctx.Response.Headers["X-Frame-Options"] = "DENY";
    ctx.Response.Headers["Referrer-Policy"] = "no-referrer";
    ctx.Response.Headers["Content-Security-Policy"] = "frame-ancestors 'none'";
    await next();
});

// seed admin + демо-послуги/майстри
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    DbInitializer.Seed(db, app.Configuration);
}

app.UseCors();
app.UseStaticFiles();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();
