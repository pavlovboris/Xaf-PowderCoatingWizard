using Microsoft.Extensions.AI;
using System.ComponentModel;
using System.Text;

namespace PowderCoatingWizard.Module.Services.AI
{
    /// <summary>
    /// Exposes explicit on-demand knowledge retrieval tools over the existing RAG pipeline.
    /// </summary>
    public sealed class KnowledgeSearchToolService
    {
        private readonly RagSearchService _ragSearch;
        private readonly IChatClient? _planningClient;
        private readonly Guid? _agentOid;

        public KnowledgeSearchToolService(RagSearchService ragSearch, IChatClient? planningClient, Guid? agentOid)
        {
            _ragSearch = ragSearch;
            _planningClient = planningClient;
            _agentOid = agentOid;
        }

        [Description("Searches approved documents, standards, SOPs, certificates, case studies, and vector-store knowledge on demand. Use after database evidence when a targeted document or prior-case search would improve the answer.")]
        public async Task<string> SearchKnowledge(
            [Description("Focused search query. Rephrase the user's need into keywords and domain terms, for example 'powder coating adhesion failure zinc phosphate low pH'.")] string query,
            [Description("Maximum result count. Default 5, capped at 10.")] int topK = 5,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(query))
                return "Knowledge search skipped: query is required.";

            var results = await _ragSearch.SearchDetailedAsync(
                query.Trim(),
                Math.Clamp(topK <= 0 ? 5 : topK, 1, 10),
                agentOid: _agentOid,
                chatClient: _planningClient,
                ct: ct);

            if (results.Count == 0)
                return "No relevant document, case-study, or vector-store knowledge was found for the search query.";

            var sb = new StringBuilder();
            sb.AppendLine("Knowledge search results for internal assistant reasoning.");
            sb.AppendLine("Use citations when this evidence is used in the final answer.");
            sb.AppendLine($"Query: {query.Trim()}");
            sb.AppendLine();

            foreach (var result in results)
            {
                sb.AppendLine($"- [{result.Citation}] {result.SourceType}: {result.SourceTitle} (score {result.Score:G3})");
                sb.AppendLine($"  {Trim(result.Text, 700)}");
            }

            return sb.ToString();
        }

        private static string Trim(string value, int maxLength)
        {
            var text = value.Replace("\r", " ").Replace("\n", " ").Trim();
            return text.Length <= maxLength ? text : text[..maxLength] + "...";
        }
    }
}
