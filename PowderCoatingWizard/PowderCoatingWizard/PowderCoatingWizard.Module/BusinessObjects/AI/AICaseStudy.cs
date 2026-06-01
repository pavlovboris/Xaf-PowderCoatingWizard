using DevExpress.Persistent.Base;
using DevExpress.Persistent.BaseImpl;
using DevExpress.Xpo;
using PowderCoatingWizard.Module.Attributes;
using System.ComponentModel;

namespace PowderCoatingWizard.Module.BusinessObjects.AI
{
    /// <summary>
    /// Lifecycle status of a case study.
    /// Only <see cref="Approved"/> records are embedded into the vector store and
    /// become searchable by the RAG pipeline.
    /// </summary>
    public enum CaseStudyStatus
    {
        [Description("Work-in-progress — not yet visible to the AI")]
        Draft    = 0,
        [Description("Reviewed and approved — embedded and searchable by all agents")]
        Approved = 1,
        [Description("Superseded or no longer relevant — excluded from RAG search")]
        Archived = 2
    }

    /// <summary>
    /// A documented production incident or best-practice example.
    /// Approved case studies are automatically embedded into <see cref="KnowledgeChunk"/> records
    /// and become part of the RAG knowledge base — this is the primary "training" mechanism
    /// for domain-specific agent knowledge without any model fine-tuning.
    /// </summary>
    [DefaultClassOptions]
    [NavigationItem("AI")]
    [ImageName("BO_Analysis")]
    [DefaultProperty(nameof(Title))]
    [AIQueryable("A documented production incident, best-practice example, or resolved quality issue used to train the AI agent.")]
    public class AICaseStudy : BaseObject
    {
        public AICaseStudy(Session session) : base(session) { }

        string _title;
        string _tags;
        string _problemDescription;
        string _rootCause;
        string _resolution;
        string _outcome;
        string _lessonsLearned;
        CaseStudyStatus _status = CaseStudyStatus.Draft;
        LineStage _stage;
        DateTime _occurredOn;
        DateTime _createdAt;
        bool _isEmbedded;

        // ── Identity ────────────────────────────────────────────────────────

        /// <summary>Short descriptive title shown in lists and RAG results.</summary>
        [Size(500)]
        public string Title
        {
            get => _title;
            set => SetPropertyValue(nameof(Title), ref _title, value);
        }

        /// <summary>
        /// Comma-separated keywords for filtering and search context
        /// (e.g. "zinc, pH, concentration, alarm").
        /// </summary>
        [Size(1000)]
        [ToolTip("Comma-separated keywords: chemistry type, parameter names, line identifiers, etc.")]
        public string Tags
        {
            get => _tags;
            set => SetPropertyValue(nameof(Tags), ref _tags, value);
        }

        // ── Content ────────────────────────────────────────────────────────

        /// <summary>Describe what happened — symptoms observed, parameters affected.</summary>
        [Size(SizeAttribute.Unlimited)]
        public string ProblemDescription
        {
            get => _problemDescription;
            set => SetPropertyValue(nameof(ProblemDescription), ref _problemDescription, value);
        }

        /// <summary>Identified root cause of the problem.</summary>
        [Size(SizeAttribute.Unlimited)]
        public string RootCause
        {
            get => _rootCause;
            set => SetPropertyValue(nameof(RootCause), ref _rootCause, value);
        }

        /// <summary>Steps taken to resolve the issue or implement the best practice.</summary>
        [Size(SizeAttribute.Unlimited)]
        public string Resolution
        {
            get => _resolution;
            set => SetPropertyValue(nameof(Resolution), ref _resolution, value);
        }

        /// <summary>Result after applying the resolution — measurable outcome if available.</summary>
        [Size(SizeAttribute.Unlimited)]
        public string Outcome
        {
            get => _outcome;
            set => SetPropertyValue(nameof(Outcome), ref _outcome, value);
        }

        /// <summary>Key takeaways and recommendations for the future.</summary>
        [Size(SizeAttribute.Unlimited)]
        public string LessonsLearned
        {
            get => _lessonsLearned;
            set => SetPropertyValue(nameof(LessonsLearned), ref _lessonsLearned, value);
        }

        // ── Metadata ──────────────────────────────────────────────────────

        /// <summary>
        /// Workflow status.
        /// Changing to <see cref="CaseStudyStatus.Approved"/> triggers automatic embedding.
        /// </summary>
        public CaseStudyStatus Status
        {
            get => _status;
            set => SetPropertyValue(nameof(Status), ref _status, value);
        }

        /// <summary>Optional link to the production stage where the incident occurred.</summary>
        public LineStage Stage
        {
            get => _stage;
            set => SetPropertyValue(nameof(Stage), ref _stage, value);
        }

        /// <summary>When the incident occurred (may differ from CreatedAt).</summary>
        public DateTime OccurredOn
        {
            get => _occurredOn;
            set => SetPropertyValue(nameof(OccurredOn), ref _occurredOn, value);
        }

        public DateTime CreatedAt
        {
            get => _createdAt;
            set => SetPropertyValue(nameof(CreatedAt), ref _createdAt, value);
        }

        /// <summary>True once <see cref="KnowledgeChunk"/> records have been generated for this case.</summary>
        [ToolTip("Set automatically when the case study is embedded into the vector store.")]
        public bool IsEmbedded
        {
            get => _isEmbedded;
            set => SetPropertyValue(nameof(IsEmbedded), ref _isEmbedded, value);
        }

        // ── RAG chunks ────────────────────────────────────────────────────

        /// <summary>
        /// Vector-store chunks generated from this case study.
        /// Created automatically when <see cref="Status"/> is set to
        /// <see cref="CaseStudyStatus.Approved"/>.
        /// </summary>
        [Association("AICaseStudy-KnowledgeChunks")]
        [Aggregated]
        public XPCollection<KnowledgeChunk> KnowledgeChunks => GetCollection<KnowledgeChunk>(nameof(KnowledgeChunks));

        // ── Helpers ───────────────────────────────────────────────────────

        /// <summary>
        /// Builds the full structured text that gets embedded into the vector store.
        /// Structured with labelled sections so RAG retrieval finds both keyword and semantic matches.
        /// </summary>
        public string BuildEmbeddingText()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"CASE STUDY: {Title}");

            if (!string.IsNullOrWhiteSpace(Tags))
                sb.AppendLine($"Tags: {Tags}");
            if (Stage != null)
                sb.AppendLine($"Stage: {Stage.Line?.Name} › {Stage.Name}");
            if (OccurredOn != default)
                sb.AppendLine($"Occurred: {OccurredOn:dd MMM yyyy}");

            sb.AppendLine();
            sb.AppendLine($"PROBLEM:\n{ProblemDescription}");

            if (!string.IsNullOrWhiteSpace(RootCause))
                sb.AppendLine($"\nROOT CAUSE:\n{RootCause}");

            if (!string.IsNullOrWhiteSpace(Resolution))
                sb.AppendLine($"\nRESOLUTION:\n{Resolution}");

            if (!string.IsNullOrWhiteSpace(Outcome))
                sb.AppendLine($"\nOUTCOME:\n{Outcome}");

            if (!string.IsNullOrWhiteSpace(LessonsLearned))
                sb.AppendLine($"\nLESSONS LEARNED:\n{LessonsLearned}");

            return sb.ToString();
        }
    }
}
