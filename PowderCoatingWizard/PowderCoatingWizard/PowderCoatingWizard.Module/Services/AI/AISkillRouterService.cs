using PowderCoatingWizard.Module.BusinessObjects.AI;

namespace PowderCoatingWizard.Module.Services.AI
{
    /// <summary>
    /// Selects the most appropriate runtime-configured workflow skill for a user question.
    /// </summary>
    public sealed class AISkillRouterService
    {
        private readonly IReadOnlySet<AgentSkill>? _allowedSkills;

        public AISkillRouterService(IEnumerable<AgentSkill>? allowedSkills = null)
        {
            _allowedSkills = allowedSkills?.ToHashSet();
        }

        public AgentSkill Route(string userText, AIQueryIntent intent)
        {
            var text = (userText ?? string.Empty).ToLowerInvariant();

            var preferred = intent switch
            {
                AIQueryIntent.DocumentQuestion => AgentSkill.DocumentCompliance,
                AIQueryIntent.CaseStudyQuestion => AgentSkill.CaseStudyMatching,
                AIQueryIntent.DatabaseQuestion when ContainsAny(text, "trend", "тенден", "drift", "послед", "history", "истор") => AgentSkill.ProcessTrendAnalysis,
                AIQueryIntent.DatabaseQuestion => AgentSkill.ChemicalBathAnalysis,
                AIQueryIntent.HybridInvestigation => RouteHybrid(text),
                _ => AgentSkill.GeneralAnswer
            };

            if (IsAllowed(preferred))
                return preferred;

            return FirstAllowed(
                AgentSkill.CoatingDefectInvestigation,
                AgentSkill.ChemicalBathAnalysis,
                AgentSkill.ProcessTrendAnalysis,
                AgentSkill.DocumentCompliance,
                AgentSkill.CaseStudyMatching,
                AgentSkill.GeneralAnswer);
        }

        private AgentSkill RouteHybrid(string text)
        {
            if (ContainsAny(text, "case study", "казус", "подобен случай", "lessons learned", "best practice"))
                return AgentSkill.CaseStudyMatching;

            if (ContainsAny(text, "certificate", "сертификат", "qualicoat", "standard", "стандарт", "sop", "tds", "sds", "документ"))
                return AgentSkill.DocumentCompliance;

            if (ContainsAny(text, "trend", "тенден", "drift", "послед", "history", "истор"))
                return AgentSkill.ProcessTrendAnalysis;

            if (ContainsAny(text, "вана", "bath", "ph", "концентрац", "температур", "дозир", "хим", "chemical"))
                return AgentSkill.ChemicalBathAnalysis;

            return AgentSkill.CoatingDefectInvestigation;
        }

        private AgentSkill FirstAllowed(params AgentSkill[] skills)
            => skills.FirstOrDefault(IsAllowed, AgentSkill.GeneralAnswer);

        private bool IsAllowed(AgentSkill skill)
            => _allowedSkills == null || _allowedSkills.Count == 0 || _allowedSkills.Contains(skill);

        private static bool ContainsAny(string text, params string[] terms)
            => terms.Any(text.Contains);
    }
}
