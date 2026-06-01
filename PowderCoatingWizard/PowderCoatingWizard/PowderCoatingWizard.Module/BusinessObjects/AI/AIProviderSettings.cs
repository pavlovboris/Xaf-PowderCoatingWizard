using DevExpress.ExpressApp.Model;
using DevExpress.Persistent.Base;
using DevExpress.Persistent.BaseImpl;
using DevExpress.Persistent.Validation;
using DevExpress.Xpo;
using System.ComponentModel;

namespace PowderCoatingWizard.Module.BusinessObjects.AI
{
    /// <summary>
    /// Determines when the Prompt Shaper dialog is triggered in the AI Assistant.
    /// </summary>
    public enum PromptShaperTriggerMode
    {
        /// <summary>Prompt Shaper is never shown automatically; user opens it manually.</summary>
        Off = 0,
        /// <summary>Prompt Shaper is shown only when the user types /shape as their message.</summary>
        SlashCommand = 1,
        /// <summary>Prompt Shaper is shown before every message.</summary>
        Always = 2
    }

    [DefaultClassOptions]
    [NavigationItem("AI")]
    [ImageName("BO_Scheduler_Resource")]
    [DefaultProperty(nameof(DisplayName))]
    // Singleton: prevents creation of additional records and deletion
    [RuleObjectExists("AIProviderSettings_OnlyOne", DefaultContexts.Save, "True",
        InvertResult = true,
        CustomMessageTemplate = "AI Provider Settings already exist. Only one record is allowed.")]
    [RuleCriteria("AIProviderSettings_NoDeletion", DefaultContexts.Delete, "False",
        CustomMessageTemplate = "AI Provider Settings cannot be deleted.")]
    public class AIProviderSettings : BaseObject
    {
        public AIProviderSettings(Session session) : base(session) { }

        string _displayName;
        AIProviderType _providerType;
        string _endpoint;
        string _apiKeyEncrypted;
        string _modelId;
        string _embeddingModelId;
        bool _isEnabled;
        int _ragTopK = 5;
            float _ragMinScore = 0.4f;
            int _ragCandidatePool = 15;
            bool _ragRerankerEnabled = true;
            int _ragMaxSubqueries = 4;
            bool _dbQueryEnabled = true;
                int _dbQueryMaxRecords = 50;
                PromptShaperTriggerMode _promptShaperTrigger = PromptShaperTriggerMode.SlashCommand;
                int _plannerDecomposeMaxTokens = 300;
                float _plannerDecomposeTemperature = 0.2f;
                int _plannerHyDEMaxTokens = 200;
                float _plannerHyDETemperature = 0.3f;
                bool _openAIVectorStoreEnabled;
                string _openAIVectorStoreId;
                int _openAIVectorStoreMaxResults = 8;

        [Size(200)]
        public string DisplayName
        {
            get => _displayName;
            set => SetPropertyValue(nameof(DisplayName), ref _displayName, value);
        }

        public AIProviderType ProviderType
        {
            get => _providerType;
            set => SetPropertyValue(nameof(ProviderType), ref _providerType, value);
        }

        /// <summary>
        /// For Azure OpenAI: https://&lt;resource&gt;.openai.azure.com/
        /// For Ollama: http://localhost:11434
        /// For OpenAI: leave empty (uses default endpoint)
        /// </summary>
        [Size(500)]
        [ModelDefault("DisplayFormat", "{0}")]
        public string Endpoint
        {
            get => _endpoint;
            set => SetPropertyValue(nameof(Endpoint), ref _endpoint, value);
        }

        /// <summary>
        /// API key stored encrypted with DPAPI (Windows) or plain on server side.
        /// Use the ApiKey property to get/set the plain-text value.
        /// </summary>
        [Size(2000)]
        [Browsable(false)]
        public string ApiKeyEncrypted
        {
            get => _apiKeyEncrypted;
            set => SetPropertyValue(nameof(ApiKeyEncrypted), ref _apiKeyEncrypted, value);
        }

        /// <summary>
        /// Model/deployment name.
        /// Azure OpenAI: deployment name (e.g. "gpt-4o-mini")
        /// OpenAI: model name (e.g. "gpt-4o-mini")
        /// Ollama: model tag (e.g. "llama3.2")
        /// </summary>
        [Size(200)]
        public string ModelId
        {
            get => _modelId;
            set => SetPropertyValue(nameof(ModelId), ref _modelId, value);
        }

        public bool IsEnabled
        {
            get => _isEnabled;
            set => SetPropertyValue(nameof(IsEnabled), ref _isEnabled, value);
        }

        /// <summary>
        /// Dedicated embedding model/deployment (optional).
        /// Leave empty to use the default "text-embedding-3-small".
        /// Azure OpenAI: deployment name; OpenAI: model name.
        /// </summary>
        [Size(200)]
        public string EmbeddingModelId
        {
            get => _embeddingModelId;
            set => SetPropertyValue(nameof(EmbeddingModelId), ref _embeddingModelId, value);
        }

        /// <summary>
        /// Maximum number of knowledge-base chunks returned per RAG search.
        /// </summary>
        [ModelDefault("DisplayFormat", "{0}")]
        public int RagTopK
        {
            get => _ragTopK;
            set => SetPropertyValue(nameof(RagTopK), ref _ragTopK, value);
        }

        /// <summary>
        /// Minimum cosine-similarity score (0.0 – 1.0) a chunk must reach to be included in RAG results.
        /// Lower values return more results; higher values return only the closest matches.
        /// </summary>
        [ModelDefault("DisplayFormat", "{0:F2}")]
        public float RagMinScore
        {
            get => _ragMinScore;
            set => SetPropertyValue(nameof(RagMinScore), ref _ragMinScore, value);
        }

        /// <summary>
        /// Number of candidate chunks retrieved by embedding similarity before reranking.
        /// Should be larger than RagTopK (default 15). Reranker then selects the best RagTopK.
        /// </summary>
        [ModelDefault("DisplayFormat", "{0}")]
        public int RagCandidatePool
        {
            get => _ragCandidatePool;
            set => SetPropertyValue(nameof(RagCandidatePool), ref _ragCandidatePool, value);
        }

        /// <summary>
        /// When enabled, an LLM reranker scores all candidate chunks and selects the best RagTopK.
        /// Produces significantly better retrieval quality at the cost of one extra LLM call.
        /// </summary>
        public bool RagRerankerEnabled
        {
            get => _ragRerankerEnabled;
            set => SetPropertyValue(nameof(RagRerankerEnabled), ref _ragRerankerEnabled, value);
        }

        /// <summary>
        /// Maximum number of focused subqueries the query planner generates for complex questions.
        /// More subqueries = higher recall at the cost of extra embedding calls (default 4).
        /// </summary>
        [ModelDefault("DisplayFormat", "{0}")]
        public int RagMaxSubqueries
        {
            get => _ragMaxSubqueries;
            set => SetPropertyValue(nameof(RagMaxSubqueries), ref _ragMaxSubqueries, value);
        }

        /// <summary>
        /// Controls when the Prompt Shaper dialog is shown before sending a message.
        /// </summary>
        public PromptShaperTriggerMode PromptShaperTrigger
        {
            get => _promptShaperTrigger;
            set => SetPropertyValue(nameof(PromptShaperTrigger), ref _promptShaperTrigger, value);
        }

        /// <summary>
        /// Enables or disables the AI database query tools (list_entities, describe_entity, query_entity).
        /// </summary>
        public bool DbQueryEnabled
        {
            get => _dbQueryEnabled;
            set => SetPropertyValue(nameof(DbQueryEnabled), ref _dbQueryEnabled, value);
        }

        /// <summary>
        /// Maximum number of records the AI assistant can retrieve in a single query_entity call.
        /// Acts as a safety cap. Default is 50.
        /// </summary>
        [ModelDefault("DisplayFormat", "{0}")]
        public int DbQueryMaxRecords
        {
            get => _dbQueryMaxRecords;
            set => SetPropertyValue(nameof(DbQueryMaxRecords), ref _dbQueryMaxRecords, value);
        }

        // ── Query Planner tuning ───────────────────────────────────────────────

        /// <summary>
        /// Maximum output tokens for the Decompose LLM call (complex query → subqueries).
        /// Default 300.
        /// </summary>
        [ModelDefault("DisplayFormat", "{0}")]
        public int PlannerDecomposeMaxTokens
        {
            get => _plannerDecomposeMaxTokens;
            set => SetPropertyValue(nameof(PlannerDecomposeMaxTokens), ref _plannerDecomposeMaxTokens, value);
        }

        /// <summary>
        /// Temperature for the Decompose LLM call. Lower = more deterministic. Default 0.2.
        /// </summary>
        [ModelDefault("DisplayFormat", "{0:F2}")]
        public float PlannerDecomposeTemperature
        {
            get => _plannerDecomposeTemperature;
            set => SetPropertyValue(nameof(PlannerDecomposeTemperature), ref _plannerDecomposeTemperature, value);
        }

        /// <summary>
        /// Maximum output tokens for the HyDE LLM call (simple query → hypothetical answer paragraph).
        /// Default 200.
        /// </summary>
        [ModelDefault("DisplayFormat", "{0}")]
        public int PlannerHyDEMaxTokens
        {
            get => _plannerHyDEMaxTokens;
            set => SetPropertyValue(nameof(PlannerHyDEMaxTokens), ref _plannerHyDEMaxTokens, value);
        }

        /// <summary>
        /// Temperature for the HyDE LLM call. Slightly higher than Decompose to allow creative elaboration. Default 0.3.
        /// </summary>
        [ModelDefault("DisplayFormat", "{0:F2}")]
        public float PlannerHyDETemperature
        {
            get => _plannerHyDETemperature;
            set => SetPropertyValue(nameof(PlannerHyDETemperature), ref _plannerHyDETemperature, value);
        }

        // ── OpenAI Vector Store ───────────────────────────────────────────────

        /// <summary>
        /// Enables hybrid retrieval from an OpenAI Vector Store in addition to the local KnowledgeChunk index.
        /// Only used when ProviderType is OpenAI and OpenAIVectorStoreId is set.
        /// </summary>
        public bool OpenAIVectorStoreEnabled
        {
            get => _openAIVectorStoreEnabled;
            set => SetPropertyValue(nameof(OpenAIVectorStoreEnabled), ref _openAIVectorStoreEnabled, value);
        }

        /// <summary>OpenAI vector store id, for example vs_abc123.</summary>
        [Size(200)]
        public string OpenAIVectorStoreId
        {
            get => _openAIVectorStoreId;
            set => SetPropertyValue(nameof(OpenAIVectorStoreId), ref _openAIVectorStoreId, value);
        }

        /// <summary>Maximum number of hits to request from OpenAI vector store search.</summary>
        [ModelDefault("DisplayFormat", "{0}")]
        public int OpenAIVectorStoreMaxResults
        {
            get => _openAIVectorStoreMaxResults;
            set => SetPropertyValue(nameof(OpenAIVectorStoreMaxResults), ref _openAIVectorStoreMaxResults, value);
        }

        // ── Non-persistent helper ──────────────────────────────────────────────

        /// <summary>Plain-text API key — encrypts/decrypts via AICredentialProtector.</summary>
        [NonPersistent]
        [PasswordPropertyText(true)]
        [Size(500)]
        public string ApiKey
        {
            get => string.IsNullOrEmpty(ApiKeyEncrypted)
                ? string.Empty
                : AICredentialProtector.Decrypt(ApiKeyEncrypted);
            set => ApiKeyEncrypted = string.IsNullOrEmpty(value)
                ? string.Empty
                : AICredentialProtector.Encrypt(value);
        }
    }
}
