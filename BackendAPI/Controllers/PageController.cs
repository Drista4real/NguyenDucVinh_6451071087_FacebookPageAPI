using BackendAPI.Models;
using BackendAPI.Services;
using Microsoft.AspNetCore.Mvc;

namespace BackendAPI.Controllers;

[ApiController]
[Route("api/page")]
[Produces("application/json")]
public sealed class PageController : ApiControllerBase
{
    private readonly IFacebookService _facebookService;
    private readonly ILogger<PageController> _logger;

    public PageController(
        IFacebookService facebookService,
        ILogger<PageController> logger)
    {
        _facebookService = facebookService;
        _logger = logger;
    }

    [HttpGet("{pageId}")]
    public Task<IActionResult> GetPageInfo(
        string pageId,
        CancellationToken cancellationToken) =>
        ExecuteAsync(() => _facebookService.GetPageInfoAsync(pageId, cancellationToken));

    [HttpGet("{pageId}/posts")]
    public Task<IActionResult> GetPosts(
        string pageId,
        [FromQuery] int limit = 25,
        [FromQuery] string? after = null,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(() =>
            _facebookService.GetPostsAsync(
                pageId,
                limit,
                after,
                cancellationToken));

    [HttpPost("{pageId}/posts")]
    public Task<IActionResult> CreatePost(
        string pageId,
        [FromBody] CreatePostRequest request,
        CancellationToken cancellationToken) =>
        ExecuteAsync(() =>
            _facebookService.CreatePostAsync(pageId, request, cancellationToken));

    [HttpDelete("post/{postId}")]
    public async Task<IActionResult> DeletePost(
        string postId,
        CancellationToken cancellationToken)
    {
        try
        {
            var success = await _facebookService.DeletePostAsync(
                postId,
                cancellationToken);
            return success
                ? Ok(new SuccessResponse { Message = "Post deleted successfully." })
                : Error(
                    StatusCodes.Status400BadRequest,
                    "Facebook did not confirm post deletion.",
                    "delete_failed");
        }
        catch (Exception ex)
        {
            LogException(ex);
            return HandleException(ex);
        }
    }

    [HttpGet("post/{postId}/comments")]
    public Task<IActionResult> GetComments(
        string postId,
        [FromQuery] int limit = 25,
        [FromQuery] string? after = null,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(() =>
            _facebookService.GetCommentsAsync(
                postId,
                limit,
                after,
                cancellationToken));

    [HttpGet("post/{postId}/likes")]
    public Task<IActionResult> GetLikes(
        string postId,
        [FromQuery] int limit = 25,
        [FromQuery] string? after = null,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(() =>
            _facebookService.GetLikesAsync(
                postId,
                limit,
                after,
                cancellationToken));

    [HttpGet("{pageId}/insights")]
    public Task<IActionResult> GetInsights(
        string pageId,
        CancellationToken cancellationToken) =>
        ExecuteAsync(() =>
            _facebookService.GetInsightsAsync(pageId, cancellationToken));

    [HttpPost("comment/{commentId}/reply")]
    public Task<IActionResult> ReplyToComment(
        string commentId,
        [FromBody] ReplyCommentRequest request,
        CancellationToken cancellationToken) =>
        ExecuteAsync(() =>
            _facebookService.ReplyToCommentAsync(
                commentId,
                request.Message,
                cancellationToken));

    [HttpPatch("comment/{commentId}/visibility")]
    public Task<IActionResult> SetCommentVisibility(
        string commentId,
        [FromBody] SetCommentVisibilityRequest request,
        CancellationToken cancellationToken) =>
        ExecuteAsync(() =>
            _facebookService.SetCommentHiddenAsync(
                commentId,
                request.IsHidden,
                cancellationToken));

    private async Task<IActionResult> ExecuteAsync(
        Func<Task<System.Text.Json.JsonElement>> action)
    {
        try
        {
            return Ok(await action());
        }
        catch (Exception ex)
        {
            LogException(ex);
            return HandleException(ex);
        }
    }

    private void LogException(Exception exception)
    {
        if (exception is FacebookApiException or FacebookConfigurationException)
        {
            _logger.LogWarning(exception, "Facebook request failed");
        }
        else
        {
            _logger.LogError(exception, "Page API request failed");
        }
    }
}
