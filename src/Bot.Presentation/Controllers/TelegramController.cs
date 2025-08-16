using Bot.Application.Interfaces;
using Bot.Presentation.Security;
using Bot.Shared.Config;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using System.Text;
using Telegram.Bot.Types;

namespace Bot.Presentation.Controllers;

[ApiController]
[Route("bot/update/{secret}")]
public sealed class TelegramController : ControllerBase
{
    private readonly IUpdateRouter _router;
    private readonly UpdateDedupCache _dedup;
    private readonly BotOptions _options;

    public TelegramController(IUpdateRouter router, UpdateDedupCache dedup, IOptions<BotOptions> options)
    {
        _router = router;
        _dedup = dedup;
        _options = options.Value;
    }

    [HttpPost]
    public async Task<IActionResult> Post(
        [FromRoute] string secret,
        [FromBody] Update update,
        CancellationToken cancellationToken)
    {
        if (!TimingSafeEquals(secret, _options.WebhookSecretPathSegment)) return Unauthorized();
        if (_dedup.Seen(update)) return Ok();

        await _router.Handle(update, cancellationToken);
        return Ok();
    }

    private static bool TimingSafeEquals(string a, string b)
    {
        var ba = Encoding.UTF8.GetBytes(a);
        var bb = Encoding.UTF8.GetBytes(b);
        if (ba.Length != bb.Length) return false;
        int diff = 0;
        for (int i = 0; i < ba.Length; i++) diff |= ba[i] ^ bb[i];
        return diff == 0;
    }
}