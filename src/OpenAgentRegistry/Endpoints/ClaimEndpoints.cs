using System.Text;
using OpenAgentRegistry.Contracts;
using OpenAgentRegistry.Data;
using OpenAgentRegistry.Services;

namespace OpenAgentRegistry.Endpoints;

public static class ClaimEndpoints
{
    public static IEndpointRouteBuilder MapClaimEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/claim/{token}", ClaimPage);
        app.MapPost("/claim/{token}/begin", Begin);
        app.MapPost("/claim/{token}/begin-2fa", Begin2Fa);
        app.MapPost("/claim/{token}/confirm-email", ConfirmEmail);
        app.MapPost("/claim/{token}/setup-totp", SetupTotp);
        app.MapPost("/claim/{token}/confirm-totp", ConfirmTotp);
        app.MapPost("/claim/{token}/request-code", RequestCode);
        app.MapPost("/claim/{token}/confirm", Confirm);
        return app;
    }

    private static IResult ClaimPage(string token, AgentRepository repository)
    {
        var agent = repository.GetByClaimToken(token);
        if (agent is null)
            return Results.Content("<h1>Invalid claim link</h1>", "text/html; charset=utf-8", statusCode: 404);

        if (agent.IsClaimed)
            return Results.Content($"<p>Agent <strong>{agent.Name}</strong> is already claimed.</p>", "text/html; charset=utf-8");

        var html = ClaimPageHtml(token, agent.Name);
        return Results.Content(html, "text/html; charset=utf-8");
    }

    private static async Task<IResult> Begin(
        string token,
        ClaimBeginRequest body,
        ClaimFlowService claimFlow,
        CancellationToken cancellationToken)
    {
        try
        {
            return Results.Ok(await claimFlow.BeginAsync(token, body.Email, body.Channel, body.TelegramChatId, cancellationToken));
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(new { detail = ex.Message });
        }
        catch (RegistryApiException ex)
        {
            return Results.Json(new { detail = ex.Message }, statusCode: ex.StatusCode);
        }
    }

    private static async Task<IResult> Begin2Fa(string token, ClaimEmailRequest body, ClaimFlowService claimFlow, CancellationToken cancellationToken)
    {
        try
        {
            return Results.Ok(await claimFlow.Begin2FaAsync(token, body.Email, cancellationToken));
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(new { detail = ex.Message });
        }
        catch (RegistryApiException ex)
        {
            return Results.Json(new { detail = ex.Message }, statusCode: ex.StatusCode);
        }
    }

    private static IResult ConfirmEmail(string token, ClaimConfirmRequest body, ClaimFlowService claimFlow)
    {
        try
        {
            return Results.Ok(claimFlow.ConfirmEmailStepAsync(token, body.Email, body.Code));
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(new { detail = ex.Message });
        }
        catch (RegistryApiException ex)
        {
            return Results.Json(new { detail = ex.Message }, statusCode: ex.StatusCode);
        }
    }

    private static IResult SetupTotp(string token, ClaimFlowService claimFlow)
    {
        try
        {
            return Results.Ok(claimFlow.SetupTotp2Fa(token));
        }
        catch (RegistryApiException ex)
        {
            return Results.Json(new { detail = ex.Message }, statusCode: ex.StatusCode);
        }
    }

    private static IResult ConfirmTotp(string token, ClaimConfirmRequest body, ClaimFlowService claimFlow)
    {
        try
        {
            return Results.Ok(claimFlow.ConfirmTotpAsync(token, body.Email, body.Code));
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(new { detail = ex.Message });
        }
        catch (RegistryApiException ex)
        {
            return Results.Json(new { detail = ex.Message }, statusCode: ex.StatusCode);
        }
    }

    private static Task<IResult> RequestCode(
        string token,
        ClaimRequestCodeRequest body,
        ClaimFlowService claimFlow,
        CancellationToken cancellationToken) =>
        Begin(token, new ClaimBeginRequest(body.Email, body.Channel, body.TelegramChatId), claimFlow, cancellationToken);

    private static IResult Confirm(string token, ClaimConfirmRequest body, ClaimFlowService claimFlow)
    {
        try
        {
            return Results.Ok(claimFlow.ConfirmAsync(token, body.Email, body.Code));
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(new { detail = ex.Message });
        }
        catch (RegistryApiException ex)
        {
            return Results.Json(new { detail = ex.Message }, statusCode: ex.StatusCode);
        }
    }

    private static string ClaimPageHtml(string token, string name)
    {
        var sb = new StringBuilder();
        sb.Append("<!DOCTYPE html><html><head><meta charset='utf-8'><title>Claim ").Append(name).AppendLine("</title></head><body>");
        sb.Append("<h1>Claim agent: ").Append(name).AppendLine("</h1>");
        sb.AppendLine("<p>No X/Twitter. Pick a verification channel:</p>");
        sb.AppendLine("<ul><li><b>totp</b> — Authenticator</li><li><b>email</b></li><li><b>telegram</b></li></ul>");
        sb.Append("<label>Email <input id=\"email\" type=\"email\" /></label>");
        sb.Append("<label>Channel <select id=\"channel\"><option value=\"email\">email</option><option value=\"totp\">totp</option><option value=\"telegram\">telegram</option></select></label>");
        sb.Append("<label>Telegram chat_id <input id=\"tg\" placeholder=\"optional\" /></label>");
        sb.AppendLine("<button onclick=\"beginClaim()\">Begin</button>");
        sb.Append("<label>Code <input id=\"code\" /></label>");
        sb.AppendLine("<button onclick=\"confirmClaim()\">Confirm</button><pre id=\"out\"></pre>");
        sb.Append("<script>const out=document.getElementById('out');async function beginClaim(){const email=document.getElementById('email').value;const channel=document.getElementById('channel').value;const telegram_chat_id=document.getElementById('tg').value||null;const r=await fetch('/claim/");
        sb.Append(token).Append("'/begin',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({email,channel,telegram_chat_id})});out.textContent=JSON.stringify(await r.json(),null,2);}async function confirmClaim(){const email=document.getElementById('email').value;const code=document.getElementById('code').value;const r=await fetch('/claim/");
        sb.Append(token).Append("'/confirm',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({email,code})});out.textContent=JSON.stringify(await r.json(),null,2);}</script>");
        sb.AppendLine("</body></html>");
        return sb.ToString();
    }
}
