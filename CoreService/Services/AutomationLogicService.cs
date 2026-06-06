using CoreService.Models;

namespace CoreService.Services;

public interface IAutomationLogicService
{
    AutomationAction DetermineAction(AiAnalysisResult analysis, RawEvent @event);
}

/// <summary>
/// Rule-based automation logic để quyết định hành động
/// </summary>
public class AutomationLogicService : IAutomationLogicService
{
    private readonly ILogger<AutomationLogicService> _logger;

    public AutomationLogicService(ILogger<AutomationLogicService> logger)
    {
        _logger = logger;
    }

    public AutomationAction DetermineAction(AiAnalysisResult analysis, RawEvent @event)
    {
        _logger.LogInformation(
            "Determining action - Intent: {Intent}, Sentiment: {Sentiment}, IsSpam: {IsSpam}",
            analysis.Intent, analysis.Sentiment, analysis.IsSpam);

        // Rule 1: Spam detection
        if (analysis.IsSpam)
        {
            return new AutomationAction
            {
                ActionType = "hide_comment",
                Reason = $"Spam detected ({analysis.SpamType})",
                Confidence = analysis.Confidence
            };
        }

        // Rule 2: Negative sentiment - complaint support
        if (analysis.Sentiment == "negative" && analysis.Intent == "complaint_support")
        {
            return new AutomationAction
            {
                ActionType = "auto_reply",
                ReplyMessage = "Rất xin lỗi vì trải nghiệm chưa tốt. Bên mình sẽ kiểm tra ngay và hỗ trợ bạn. Vui lòng chờ trong vài giờ.",
                Reason = "Negative sentiment - complaint support",
                Confidence = analysis.Confidence
            };
        }

        // Rule 3: Negative sentiment - other complaints
        if (analysis.Sentiment == "negative" && analysis.Intent == "complaint")
        {
            return new AutomationAction
            {
                ActionType = "pending_review",
                Reason = "Negative sentiment - needs manual review",
                Confidence = analysis.Confidence
            };
        }

        // Rule 4: Price inquiry
        if (analysis.Intent == "ask_price")
        {
            return new AutomationAction
            {
                ActionType = "auto_reply",
                ReplyMessage = "Cảm ơn bạn quan tâm! Vui lòng xem thông tin chi tiết trên trang của shop hoặc liên hệ trực tiếp để được tư vấn giá tốt nhất.",
                Reason = "Price inquiry",
                Confidence = analysis.Confidence
            };
        }

        // Rule 5: Positive feedback
        if (analysis.Sentiment == "positive" && analysis.Intent == "positive_feedback")
        {
            return new AutomationAction
            {
                ActionType = "auto_reply",
                ReplyMessage = "Cảm ơn bạn rất nhiều! Chúng tôi rất vui được phục vụ bạn. Hãy tiếp tục ủng hộ shop nhé! ❤️",
                Reason = "Positive feedback",
                Confidence = analysis.Confidence
            };
        }

        // Default: Pending review
        return new AutomationAction
        {
            ActionType = "pending_review",
            Reason = $"Default rule - Intent: {analysis.Intent}, Sentiment: {analysis.Sentiment}",
            Confidence = analysis.Confidence
        };
    }
}
