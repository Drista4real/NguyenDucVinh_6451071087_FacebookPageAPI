using System.Security.Cryptography;
using System.Text;
using BackendAPI.Models;
using BackendAPI.Options;
using Microsoft.Extensions.Options;

namespace BackendAPI.Infrastructure;

public sealed class DashboardApiKeyMiddleware
{
    private const string HeaderName = "X-API-Key";
    private readonly RequestDelegate _next;

    public DashboardApiKeyMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(
        HttpContext context,
        IOptions<DashboardOptions> dashboardOptions)
    {
        var path = context.Request.Path;
        if (path.StartsWithSegments("/health") ||
            path.StartsWithSegments("/swagger"))
        {
            await _next(context);
            return;
        }

        if (string.IsNullOrWhiteSpace(dashboardOptions.Value.ApiKey))
        {
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            await context.Response.WriteAsJsonAsync(new ErrorResponse
            {
                Error = "Dashboard API key chưa được cấu hình.",
                Code = "dashboard_api_key_not_configured",
                TraceId = context.TraceIdentifier
            });
            return;
        }

        var suppliedKey = context.Request.Headers[HeaderName].ToString();
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(suppliedKey),
                Encoding.UTF8.GetBytes(dashboardOptions.Value.ApiKey)))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new ErrorResponse
            {
                Error = "API key không hợp lệ.",
                Code = "invalid_api_key",
                TraceId = context.TraceIdentifier
            });
            return;
        }

        await _next(context);
    }
}
