using System.Net;
using BackendAPI.Infrastructure;
using BackendAPI.Models;
using BackendAPI.Services;
using Microsoft.AspNetCore.Mvc;

namespace BackendAPI.Controllers;

public abstract class ApiControllerBase : ControllerBase
{
    protected IActionResult HandleException(Exception exception)
    {
        return exception switch
        {
            FacebookConfigurationException configurationException =>
                Error(
                    StatusCodes.Status503ServiceUnavailable,
                    configurationException.Message,
                    "facebook_not_configured"),

            CircuitBreakerOpenException circuitException =>
                Error(
                    StatusCodes.Status503ServiceUnavailable,
                    circuitException.Message,
                    "facebook_circuit_open"),

            FacebookApiException facebookException =>
                HandleFacebookException(facebookException),

            _ => Error(
                StatusCodes.Status500InternalServerError,
                "Đã xảy ra lỗi nội bộ.",
                "internal_error")
        };
    }

    private IActionResult HandleFacebookException(FacebookApiException exception)
    {
        var facebookError = exception.FacebookError;
        var details = facebookError is null
            ? exception.Message
            : $"{facebookError.Type} (code {facebookError.Code}, " +
              $"subcode {facebookError.ErrorSubcode}, trace {facebookError.FbtraceId}): " +
              facebookError.Message;

        if (facebookError?.Code == 190)
        {
            return Error(
                StatusCodes.Status401Unauthorized,
                $"Facebook access token không hợp lệ hoặc đã hết hạn. {details}",
                "facebook_token_invalid");
        }

        var upstream = exception.UpstreamStatusCode;
        var status = upstream == HttpStatusCode.TooManyRequests
            ? StatusCodes.Status429TooManyRequests
            : (int)upstream >= 500
                ? StatusCodes.Status502BadGateway
                : Math.Clamp((int)upstream, 400, 499);

        return Error(status, details, "facebook_api_error");
    }

    protected ObjectResult Error(int statusCode, string message, string code) =>
        StatusCode(statusCode, new ErrorResponse
        {
            Error = message,
            Code = code,
            TraceId = HttpContext.TraceIdentifier
        });
}
