using Microsoft.Extensions.AI;

namespace PowderCoatingWizard.Module.Services.AI
{
    public enum AIQueryIntent
    {
        GeneralChat = 0,
        DatabaseQuestion = 1,
        DocumentQuestion = 2,
        CaseStudyQuestion = 3,
        HybridInvestigation = 4
    }

    public sealed class AIIntentRouterService
    {
        private readonly IChatClient _chatClient;

        public AIIntentRouterService(IChatClient chatClient)
        {
            _chatClient = chatClient;
        }

        public async Task<AIQueryIntent> ClassifyAsync(string userText, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(userText))
                return AIQueryIntent.GeneralChat;

            var heuristic = ClassifyByHeuristic(userText);
            if (heuristic.HasValue)
                return heuristic.Value;

            const string systemPrompt =
                "Classify the user's message for an industrial powder-coating AI assistant. " +
                "Return exactly one label and nothing else: " +
                "GENERAL_CHAT, DATABASE_QUESTION, DOCUMENT_QUESTION, CASE_STUDY_QUESTION, HYBRID_INVESTIGATION. " +
                "DATABASE_QUESTION means measurements, thresholds, baths, stages, trends, alerts, current/historical production data, or database records. " +
                "DOCUMENT_QUESTION means certificates, standards, manuals, SOPs, PDFs, uploaded documents, or document content. " +
                "CASE_STUDY_QUESTION means prior incidents, lessons learned, best practices, similar problems, or approved case studies. " +
                "HYBRID_INVESTIGATION means root cause, why something happened, quality defects, stains, corrosion, adhesion, process failures, or questions needing both data and knowledge. " +
                "GENERAL_CHAT means greeting, capabilities, help, or conversation with no data/document/investigation request.";

            var messages = new List<ChatMessage>
            {
                new(ChatRole.System, systemPrompt),
                new(ChatRole.User, userText)
            };

            try
            {
                var response = await _chatClient.GetResponseAsync(messages, new ChatOptions { MaxOutputTokens = 10 }, ct);
                var text = response.Text?.Trim().ToUpperInvariant() ?? string.Empty;

                if (text.StartsWith("DATABASE")) return AIQueryIntent.DatabaseQuestion;
                if (text.StartsWith("DOCUMENT")) return AIQueryIntent.DocumentQuestion;
                if (text.StartsWith("CASE")) return AIQueryIntent.CaseStudyQuestion;
                if (text.StartsWith("HYBRID")) return AIQueryIntent.HybridInvestigation;
                if (text.StartsWith("GENERAL")) return AIQueryIntent.GeneralChat;
            }
            catch
            {
                // Non-fatal: fall back to a safe path that allows both RAG and tools.
            }

            return AIQueryIntent.HybridInvestigation;
        }

        private static AIQueryIntent? ClassifyByHeuristic(string userText)
        {
            var text = userText.ToLowerInvariant();

            if (ContainsAny(text, "здравей", "hello", "hi", "работиш", "какво можеш", "help", "помощ"))
                return AIQueryIntent.GeneralChat;

            if (ContainsAny(text, "сертификат", "certificate", "qualicoat", "документ", "document", "pdf", "стандарт", "standard", "sop", "инструкция"))
                return AIQueryIntent.DocumentQuestion;

            if (ContainsAny(text, "case study", "казус", "подобен случай", "предишен случай", "lessons learned", "best practice", "добра практика"))
                return AIQueryIntent.CaseStudyQuestion;

            if (ContainsAny(text, "защо", "причина", "root cause", "петна", "дефект", "корозия", "адхезия", "лющ", "отлеп", "проблем"))
                return AIQueryIntent.HybridInvestigation;

            if (ContainsAny(text, "вана", "bath", "етап", "stage", "измер", "measurement", "стойност", "праг", "threshold", "аларм", "trend", "тенден", "база", "database", "заявка"))
                return AIQueryIntent.DatabaseQuestion;

            return null;
        }

        private static bool ContainsAny(string text, params string[] terms)
            => terms.Any(text.Contains);
    }
}
