using BackendAPI.Models;
using BackendAPI.Options;
using BackendAPI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace BackendAPI.Controllers;

[ApiController]
[Route("")]
[Produces("application/json")]
public sealed class DashboardController : ApiControllerBase
{
    private readonly IFacebookService _facebookService;
    private readonly FacebookOptions _options;

    public DashboardController(
        IFacebookService facebookService,
        IOptions<FacebookOptions> options)
    {
        _facebookService = facebookService;
        _options = options.Value;
    }

    [HttpGet("posts")]
    public Task<IActionResult> GetPosts(
        [FromQuery] string? pageId = null,
        [FromQuery] int limit = 25,
        [FromQuery] string? after = null,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(() => _facebookService.GetPostsAsync(
            ResolvePageId(pageId),
            limit,
            after,
            cancellationToken));

    [HttpPost("post")]
    public Task<IActionResult> CreatePost(
        [FromBody] CreatePostRequest request,
        [FromQuery] string? pageId = null,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(() => _facebookService.CreatePostAsync(
            ResolvePageId(pageId),
            request,
            cancellationToken));

    [HttpGet("comments")]
    public Task<IActionResult> GetComments(
        [FromQuery] string postId,
        [FromQuery] int limit = 25,
        [FromQuery] string? after = null,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(() => _facebookService.GetCommentsAsync(
            postId,
            limit,
            after,
            cancellationToken));

    private string ResolvePageId(string? pageId)
    {
        var resolved = string.IsNullOrWhiteSpace(pageId)
            ? _options.DefaultPageId
            : pageId;
        return string.IsNullOrWhiteSpace(resolved)
            ? throw new FacebookConfigurationException(
                "Thiếu pageId. Truyền query pageId hoặc cấu hình Facebook:DefaultPageId.")
            : resolved;
    }

    private async Task<IActionResult> ExecuteAsync(
        Func<Task<System.Text.Json.JsonElement>> action)
    {
        try
        {
            return Ok(await action());
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }
}
