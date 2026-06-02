using DevExpress.ExpressApp;
using PowderCoatingWizard.Module.BusinessObjects.AI;

namespace PowderCoatingWizard.Module.Services.AI
{
    public enum AutomaticRagRoutingDecision
    {
        None = 0,
        Context = 1,
        Database = 2,
        Knowledge = 3,
        Hybrid = 4
    }

    /// <summary>
    /// Loads per-agent runtime routing terms and decides whether automatic RAG should be skipped or preferred.
    /// </summary>
    public sealed class AutomaticRagRoutingPolicyService
    {
        private readonly IObjectSpaceFactory? _objectSpaceFactory;
        private readonly Guid? _agentOid;

        public AutomaticRagRoutingPolicyService(IObjectSpaceFactory? objectSpaceFactory, Guid? agentOid)
        {
            _objectSpaceFactory = objectSpaceFactory;
            _agentOid = agentOid;
        }

        public AutomaticRagRoutingDecision Classify(string userText)
        {
            if (string.IsNullOrWhiteSpace(userText))
                return AutomaticRagRoutingDecision.Context;

            var policy = LoadPolicy();
            if (!policy.Enabled)
                return AutomaticRagRoutingDecision.None;

            var text = userText.ToLowerInvariant();

            if (ContainsAny(text, policy.ContextTerms)) return AutomaticRagRoutingDecision.Context;
            if (ContainsAny(text, policy.DatabaseTerms)) return AutomaticRagRoutingDecision.Database;
            if (ContainsAny(text, policy.KnowledgeTerms)) return AutomaticRagRoutingDecision.Knowledge;
            if (ContainsAny(text, policy.HybridTerms)) return AutomaticRagRoutingDecision.Hybrid;

            return AutomaticRagRoutingDecision.None;
        }

        private RoutingPolicy LoadPolicy()
        {
            if (_objectSpaceFactory == null || !_agentOid.HasValue)
                return RoutingPolicy.Default;

            try
            {
                using var os = _objectSpaceFactory.CreateObjectSpace(typeof(AIAgent));
                var agent = os.GetObjectByKey<AIAgent>(_agentOid.Value);
                if (agent == null)
                    return RoutingPolicy.Default;

                return new RoutingPolicy(
                    agent.RagRoutingPolicyEnabled,
                    ParseTerms(agent.ContextSkipTerms, RoutingPolicy.Default.ContextTerms),
                    ParseTerms(agent.DatabasePreferredTerms, RoutingPolicy.Default.DatabaseTerms),
                    ParseTerms(agent.KnowledgePreferredTerms, RoutingPolicy.Default.KnowledgeTerms),
                    ParseTerms(agent.HybridPreferredTerms, RoutingPolicy.Default.HybridTerms));
            }
            catch
            {
                return RoutingPolicy.Default;
            }
        }

        private static IReadOnlyList<string> ParseTerms(string? configuredTerms, IReadOnlyList<string> fallback)
        {
            if (string.IsNullOrWhiteSpace(configuredTerms))
                return fallback;

            var values = configuredTerms
                .Split(['\r', '\n', ';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Select(v => v.ToLowerInvariant())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            return values.Count == 0 ? fallback : values;
        }

        private static bool ContainsAny(string text, IReadOnlyList<string> terms) =>
            terms.Any(text.Contains);

        private sealed record RoutingPolicy(
            bool Enabled,
            IReadOnlyList<string> ContextTerms,
            IReadOnlyList<string> DatabaseTerms,
            IReadOnlyList<string> KnowledgeTerms,
            IReadOnlyList<string> HybridTerms)
        {
            public static RoutingPolicy Default { get; } = new(
                true,
                [
                    "какво можеш", "какви тулове", "какви инструменти", "какви tools", "какви tool", "какво имаш",
                    "what can you", "what tools", "available tools", "capabilities",
                    "контекст", "контекста", "context", "kontekst", "konteksta",
                    "текущ", "текущия", "селектиран", "избран", "тук", "това", "този запис", "този етап",
                    "current", "selected", "active screen", "active stage", "this record", "this stage", "tuk", "tova"
                ],
                [
                    "вана", "bath", "етап", "stage", "измер", "measurement", "стойност", "праг", "threshold",
                    "аларм", "alert", "trend", "тенден", "база", "database", "брой", "count"
                ],
                [
                    "сертификат", "certificate", "qualicoat", "документ", "document", "pdf", "стандарт", "standard",
                    "sop", "tds", "sds", "инструкция", "case study", "казус", "подобен случай"
                ],
                [
                    "защо", "причина", "root cause", "петна", "дефект", "корозия", "адхезия", "лющ", "отлеп", "проблем"
                ]);
        }
    }
}
