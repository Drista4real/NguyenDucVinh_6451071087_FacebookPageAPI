using CoreService.Models;

namespace CoreService.Services;

public interface IAutomationLogicService
{
    AutomationAction DetermineAction(AiAnalysisResult analysis, RawEvent @event);
}

public sealed class AutomationLogicService : IAutomationLogicService
{
    public AutomationAction DetermineAction(AiAnalysisResult analysis, RawEvent @event)
    {
        if (analysis.IsSpam || string.Equals(analysis.Intent, "spam", StringComparison.OrdinalIgnoreCase))
        {
            return new AutomationAction
            {
                ActionType = "hide_comment",
                Reason = $"spam:{analysis.SpamType}",
                Confidence = analysis.Confidence
            };
        }

        if (string.Equals(analysis.Intent, "ask_price", StringComparison.OrdinalIgnoreCase))
        {
            return new AutomationAction
            {
                ActionType = "reply_comment",
                ReplyMessage = "Cam on ban da quan tam. Vui long inbox page de duoc tu van gia chinh xac va nhanh nhat.",
                Reason = "price_inquiry",
                Confidence = analysis.Confidence
            };
        }

        if (string.Equals(analysis.Intent, "complaint_support", StringComparison.OrdinalIgnoreCase))
        {
            return new AutomationAction
            {
                ActionType = "reply_comment",
                ReplyMessage = "Rat xin loi ve trai nghiem chua tot. Team ho tro se kiem tra va phan hoi ban som.",
                Reason = "complaint_support",
                Confidence = analysis.Confidence
            };
        }

        if (string.Equals(analysis.Intent, "positive_feedback", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(analysis.Sentiment, "positive", StringComparison.OrdinalIgnoreCase))
        {
            return new AutomationAction
            {
                ActionType = "reply_comment",
                ReplyMessage = "Cam on ban da phan hoi tich cuc. Shop rat vui khi duoc ho tro ban.",
                Reason = "positive_feedback",
                Confidence = analysis.Confidence
            };
        }

        return new AutomationAction
        {
            ActionType = "pending_review",
            Reason = $"manual_review:{analysis.Intent}:{analysis.Sentiment}",
            Confidence = analysis.Confidence
        };
    }
}
