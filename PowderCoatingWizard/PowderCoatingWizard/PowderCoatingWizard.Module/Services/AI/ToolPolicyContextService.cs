using PowderCoatingWizard.Module.BusinessObjects.AI;
using System.ComponentModel;
using System.Text;

namespace PowderCoatingWizard.Module.Services.AI
{
    /// <summary>
    /// Provides concise tool-selection policy for the assistant without adding bulky instructions to every prompt.
    /// </summary>
    public sealed class ToolPolicyContextService
    {
        private readonly AIAgent? _agent;
        private readonly bool _databaseToolsEnabled;
        private readonly bool _ragAvailable;

        public ToolPolicyContextService(AIAgent? agent, bool databaseToolsEnabled, bool ragAvailable)
        {
            _agent = agent;
            _databaseToolsEnabled = databaseToolsEnabled;
            _ragAvailable = ragAvailable;
        }

        [Description("Returns concise internal policy for choosing among current context, XAF entity tools, DBChat, domain tools, enum lookup, and knowledge search. Use when tool choice is unclear.")]
        public string GetToolPolicy()
        {
            var sb = new StringBuilder();
            sb.AppendLine("Assistant tool-selection policy for internal reasoning.");
            sb.AppendLine("Preserve existing behavior and use the least powerful tool that can answer correctly.");
            sb.AppendLine();
            sb.AppendLine("Use get_current_context when the user refers to this, current, selected, here, active stage, active screen, or similar references.");
            sb.AppendLine("Use get_record_context for one known business object when XAF display values, references, enum names, or object semantics matter.");
            sb.AppendLine("Use list_entities and describe_entity for XAF model discovery, properties, relationships, and enum/display semantics.");
            sb.AppendLine("Use query_entity only for small XAF ObjectSpace-level samples or explicit record/list requests.");
            sb.AppendLine("Use get_database_insight for set-based database evidence: counts, aggregates, broad filters, joins, time windows, trends, comparisons, and analytical summaries.");
            sb.AppendLine("Use get_enum_mappings only when SQL evidence contains integer enum values that need human-readable names.");
            sb.AppendLine("Use search_knowledge for targeted documents, standards, SOPs, certificates, case studies, and vector-store evidence, especially after database facts clarify the issue.");
            sb.AppendLine("Use bath, trend, and threshold tools when the user asks for current bath values, measurement trends, or out-of-threshold conditions.");
            sb.AppendLine("Do not expose SQL, raw records, or tabular output unless explicitly requested.");
            sb.AppendLine();
            sb.AppendLine("Availability:");
            sb.AppendLine($"- Database tools enabled: {_databaseToolsEnabled}");
            sb.AppendLine($"- Knowledge retrieval available: {_ragAvailable}");
            sb.AppendLine($"- Agent: {(_agent == null ? "Default assistant profile" : Safe(_agent.Name))}");

            return sb.ToString();
        }

        private static string Safe(string? value)
        {
            var text = value?.Replace("\r", " ").Replace("\n", " ").Trim() ?? string.Empty;
            return text.Length <= 200 ? text : text[..200] + "...";
        }
    }
}
