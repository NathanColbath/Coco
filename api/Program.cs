using System.Text.Json.Serialization;
using ComicStudio.Api.Auth;
using ComicStudio.Api.Comics;
using ComicStudio.Api.Data;
using ComicStudio.Api.Data.Seed;
using ComicStudio.Api.Domain;
using ComicStudio.Api.Features.Comics;
using ComicStudio.Api.Features.Stories;
using ComicStudio.Api.Hubs;
using ComicStudio.Api.NovelAi;
using ComicStudio.Api.NovelAi.Image;
using ComicStudio.Api.Security;
using ComicStudio.Api.Storytelling;
using ComicStudio.Api.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

var contentRoot = builder.Environment.ContentRootPath;

// Paths in configuration are relative to the content root so the API behaves
// the same under `dotnet run`, tests, and a published deployment.
string ResolvePath(string? configured, string fallback) =>
    Path.GetFullPath(Path.Combine(contentRoot, configured ?? fallback));

var dataDirectory = ResolvePath("App_Data", "App_Data");
var artifactsPath = ResolvePath(builder.Configuration["Storage:ArtifactsPath"], "App_Data/artifacts");
var keysPath = ResolvePath(builder.Configuration["DataProtection:KeysPath"], "App_Data/keys");

Directory.CreateDirectory(dataDirectory);
Directory.CreateDirectory(artifactsPath);
Directory.CreateDirectory(keysPath);

var connectionString =
    builder.Configuration.GetConnectionString("Default")
    ?? $"Data Source={Path.Combine(dataDirectory, "comicstudio.db")}";

// --- Persistence -----------------------------------------------------------

builder.Services.AddDbContext<AppDbContext>(options => options.UseSqlite(connectionString));

// --- Identity & authentication --------------------------------------------

builder
    .Services.AddIdentityCore<AppUser>(options =>
    {
        options.Password.RequiredLength = 10;
        options.Password.RequireNonAlphanumeric = false;
        options.User.RequireUniqueEmail = true;
    })
    .AddEntityFrameworkStores<AppDbContext>()
    .AddSignInManager()
    .AddDefaultTokenProviders();

builder
    .Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(keysPath))
    .SetApplicationName("ComicStudio");

var signingKey = new SigningKeyProvider(
    Path.Combine(keysPath, "jwt-signing.key"),
    builder.Configuration["Jwt:SigningKey"]
);

builder.Services.AddSingleton(signingKey);
builder.Services.AddScoped<JwtTokenService>();
builder.Services.AddScoped<SecretProtector>();

builder
    .Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"] ?? "comic-studio",
            ValidateAudience = true,
            ValidAudience = builder.Configuration["Jwt:Audience"] ?? "comic-studio",
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(signingKey.Key),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1),
        };

        // SignalR delivers its token as a query string parameter.
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                if (
                    !string.IsNullOrEmpty(accessToken)
                    && context.HttpContext.Request.Path.StartsWithSegments("/hubs")
                )
                {
                    context.Token = accessToken;
                }

                return Task.CompletedTask;
            },
        };
    });

builder.Services.AddAuthorization();

// --- NovelAI ---------------------------------------------------------------

builder
    .Services.AddHttpClient<INovelAiClient, NovelAiClient>(client =>
    {
        // Requests use absolute URIs (the account lookup retries a second host),
        // so the base address is only a default for anything else added later.
        client.BaseAddress = new Uri(
            NovelAiEndpoints.Normalize(
                builder.Configuration["NovelAi:AccountBaseUrl"] ?? NovelAiEndpoints.Image
            )
        );
        client.Timeout = TimeSpan.FromSeconds(60);
        client.DefaultRequestHeaders.UserAgent.ParseAdd("ComicStudio/0.1");
    });

// --- Text generation -------------------------------------------------------

var textBaseUrl = NovelAiEndpoints.Normalize(
    builder.Configuration["NovelAi:TextBaseUrl"] ?? NovelAiEndpoints.Text
);

void ConfigureTextClient(HttpClient client)
{
    client.BaseAddress = new Uri(textBaseUrl);
    client.Timeout = Timeout.InfiniteTimeSpan; // streaming responses are long-lived
    client.DefaultRequestHeaders.UserAgent.ParseAdd("ComicStudio/0.1");
}

builder.Services.AddHttpClient<ITextClient, ChatTextClient>(ConfigureTextClient);
builder.Services.AddHttpClient<ITextClient, LegacyCompletionTextClient>(ConfigureTextClient);
builder.Services.AddHttpClient(TextHttpClient.Name, ConfigureTextClient);

builder.Services.AddSingleton<ITokenCounter, HeuristicTokenCounter>();
builder.Services.AddScoped<ContextAssembler>();
builder.Services.AddScoped<TextModelResolver>();
builder.Services.AddScoped<ITextClientResolver, TextClientResolver>();
builder.Services.AddSingleton<StoryGenerationRunner>();

// --- Comic adaptation & image generation -----------------------------------

var imageBaseUrl = NovelAiEndpoints.Normalize(
    builder.Configuration["NovelAi:ImageBaseUrl"] ?? NovelAiEndpoints.Image
);

builder.Services.AddHttpClient<NovelAiImageClient>(client =>
{
    client.BaseAddress = new Uri(imageBaseUrl);
    client.Timeout = Timeout.InfiniteTimeSpan; // generation is slow
    client.DefaultRequestHeaders.UserAgent.ParseAdd("ComicStudio/0.1");
});

builder.Services.AddScoped<ImageStore>();
builder.Services.AddScoped<ComicStudio.Api.Comics.ImageCleanupService>();
builder.Services.AddScoped<ImageModelResolver>();
builder.Services.AddScoped<StructuredTextService>();
builder.Services.AddSingleton<CuratorThrottle>();
builder.Services.AddScoped<Curator>();
builder.Services.AddSingleton<PagePromptComposer>();
builder.Services.AddSingleton<CurationRunner>();
builder.Services.AddSingleton<ImageGenerationRunner>();

builder.Services.AddSignalR();

// --- Web -------------------------------------------------------------------

builder
    .Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        options.JsonSerializerOptions.DefaultIgnoreCondition =
            JsonIgnoreCondition.WhenWritingNull;
    });

builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();

builder.Services.AddCors(options =>
    options.AddPolicy(
        "spa",
        policy =>
            policy
                .WithOrigins("http://localhost:4200", "https://localhost:4200")
                .AllowAnyHeader()
                .AllowAnyMethod()
                .AllowCredentials()
    )
);

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    await app.SeedDatabaseAsync();
}

app.UseCors("spa");
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapHub<GenerationHub>("/hubs/generation");

app.Run();
