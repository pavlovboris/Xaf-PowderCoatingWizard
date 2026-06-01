using DevExpress.Persistent.Base;
using DevExpress.Persistent.BaseImpl;
using DevExpress.Xpo;
using System.ComponentModel;

namespace PowderCoatingWizard.Module.BusinessObjects.AI
{
    /// <summary>
    /// One text chunk of an <see cref="AIDocument"/> with its embedding vector
    /// stored as a JSON-serialised float array (DSERPEvo-style XPO memory store).
    /// </summary>
    [DefaultClassOptions]
    [NavigationItem("AI")]
    [ImageName("Actions_Find")]
    [DefaultProperty(nameof(DisplayText))]
    public class KnowledgeChunk : BaseObject
    {
        public KnowledgeChunk(Session session) : base(session) { }

        AIDocument _document;
        AICaseStudy _caseStudy;
        int _chunkIndex;
        string _chunkText;
        string _embeddingJson;
        DateTime _createdAt;

        [Association("AIDocument-KnowledgeChunks")]
        public AIDocument Document
        {
            get => _document;
            set => SetPropertyValue(nameof(Document), ref _document, value);
        }

        /// <summary>
        /// Set when this chunk was generated from a <see cref="AICaseStudy"/> rather than an uploaded document.
        /// Exactly one of <see cref="Document"/> or <see cref="CaseStudy"/> should be non-null.
        /// </summary>
        [Association("AICaseStudy-KnowledgeChunks")]
        public AICaseStudy CaseStudy
        {
            get => _caseStudy;
            set => SetPropertyValue(nameof(CaseStudy), ref _caseStudy, value);
        }

        public int ChunkIndex
        {
            get => _chunkIndex;
            set => SetPropertyValue(nameof(ChunkIndex), ref _chunkIndex, value);
        }

        /// <summary>The raw text of this chunk (up to ~1 000 chars).</summary>
        [Size(SizeAttribute.Unlimited)]
        public string ChunkText
        {
            get => _chunkText;
            set => SetPropertyValue(nameof(ChunkText), ref _chunkText, value);
        }

        /// <summary>JSON-serialised float[] embedding vector.</summary>
        [Size(SizeAttribute.Unlimited)]
        [Browsable(false)]
        public string EmbeddingJson
        {
            get => _embeddingJson;
            set => SetPropertyValue(nameof(EmbeddingJson), ref _embeddingJson, value);
        }

        public DateTime CreatedAt
        {
            get => _createdAt;
            set => SetPropertyValue(nameof(CreatedAt), ref _createdAt, value);
        }

        [NonPersistent]
        public string DisplayText =>
            _chunkText?.Length > 80 ? _chunkText[..80] + "…" : _chunkText ?? string.Empty;

        /// <summary>Deserialises the stored JSON into a float array, or returns null if absent.</summary>
        public float[]? GetEmbedding()
        {
            if (string.IsNullOrWhiteSpace(_embeddingJson)) return null;
            return System.Text.Json.JsonSerializer.Deserialize<float[]>(_embeddingJson);
        }

        /// <summary>Serialises and stores the embedding vector.</summary>
        public void SetEmbedding(float[] vector)
        {
            EmbeddingJson = System.Text.Json.JsonSerializer.Serialize(vector);
        }
    }
}
