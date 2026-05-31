using DevExpress.ExpressApp.Model;
using DevExpress.Persistent.Base;
using DevExpress.Persistent.BaseImpl;
using DevExpress.Persistent.Validation;
using DevExpress.Xpo;
using System.ComponentModel;

namespace PowderCoatingWizard.Module.BusinessObjects.AI
{
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
