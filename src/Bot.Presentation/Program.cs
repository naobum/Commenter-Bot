using Bot.Application.Interfaces;
using Bot.Application.Services;
using Bot.Infrastructure.Llm;
using Bot.Infrastructure.Storage;
using Bot.Infrastructure.Telegram;
using Bot.Presentation.Security;
using Bot.Shared.Config;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using System.Net;
using Telegram.Bot;
using IPNetwork = Microsoft.AspNetCore.HttpOverrides.IPNetwork;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<BotOptions>(builder.Configuration.GetSection("Bot"));
builder.Services.AddLogging();
builder.Services.AddHealthChecks();
// Add services to the container.

builder.Services.AddSingleton<UpdateDedupCache>();

builder.Services.AddSingleton<ITelegramBotClient>(sp =>
{
    var opts = sp.GetRequiredService<IOptions<BotOptions>>().Value;
    return TelegramClientFactory.Create(Options.Create(opts));
});

builder.Services.AddSingleton<IMemoryStore>(sp =>
{
    var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");
    var cs = builder.Configuration.GetConnectionString("sqlite")
             ?? "Data Source=/data/memory.db;Cache=Shared;Mode=ReadWriteCreate";
    logger.LogInformation("SQLite connection string: {cs}", cs);
    return new SqliteMemoryStore(cs);
});

builder.Services.AddHttpClient<IChatModel, OpenAIChatModel>();

builder.Services.AddSingleton(sp =>
{
    var opts = sp.GetRequiredService<IOptions<BotOptions>>().Value;
    var chat = sp.GetRequiredService<IChatModel>();
    var mem = sp.GetRequiredService<IMemoryStore>();
    return new LlmCommentService(chat, mem, opts.MaxContextMessages);
});

builder.Services.AddSingleton<MemoryService>();

builder.Services.AddSingleton<IUpdateRouter>(sp =>
{
    var bot = sp.GetRequiredService<ITelegramBotClient>();
    var llm = sp.GetRequiredService<LlmCommentService>();
    var mem = sp.GetRequiredService<MemoryService>();
    var opts = sp.GetRequiredService<IOptions<BotOptions>>().Value;
    var logger = sp.GetRequiredService<ILogger<UpdateRouter>>();
    return new UpdateRouter(bot, llm, mem, opts, logger);
});

builder.Services.AddControllers();

builder.Services.Configure<ForwardedHeadersOptions>(opts =>
{
    opts.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    opts.RequireHeaderSymmetry = false;
    opts.ForwardLimit = 2;

    opts.KnownNetworks.Add(new IPNetwork(IPAddress.Parse("172.18.0.0"), 16));
});

builder.Services.Configure<ApiBehaviorOptions>(options =>
{
    options.InvalidModelStateResponseFactory = ctx =>
    {
        var errors = string.Join("; ",
            ctx.ModelState.Where(kv => kv.Value?.Errors.Count > 0)
                          .Select(kv => $"{kv.Key}: {string.Join(",", kv.Value!.Errors.Select(e => e.ErrorMessage))}"));
        var log = ctx.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("ModelState");
        log.LogWarning("Model binding failed: {Errors}", errors);
        return new BadRequestObjectResult(ctx.ModelState);
    };
});

var app = builder.Build();

app.UseForwardedHeaders();

app.MapControllers();
app.MapHealthChecks("/healthz");
app.MapGet("/", () => Results.Ok());

// On start: set webhook to our secret path
app.Lifetime.ApplicationStarted.Register(async () =>
{
    var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");
    var bot = app.Services.GetRequiredService<ITelegramBotClient>();
    var opts = app.Services.GetRequiredService<IOptions<BotOptions>>().Value;
    try
    {
        var baseUrl = app.Configuration["PublicBaseUrl"] ?? throw new InvalidOperationException("Set PublicBaseUrl");
        var url = $"{baseUrl.TrimEnd('/')}/bot/update/{opts.WebhookSecretPathSegment}";
        await bot.SetWebhook(url, allowedUpdates: new[]
        {
            Telegram.Bot.Types.Enums.UpdateType.Message,
            Telegram.Bot.Types.Enums.UpdateType.ChannelPost,
            Telegram.Bot.Types.Enums.UpdateType.EditedMessage
        });
        logger.LogInformation("Webhook set to {Url}", url);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Failed to set webhook");
        throw;
    }
});

app.Run();
