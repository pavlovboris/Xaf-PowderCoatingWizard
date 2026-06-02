using DevExpress.Persistent.Base;
using DevExpress.Persistent.BaseImpl;
using DevExpress.Xpo;
using System.ComponentModel;

namespace PowderCoatingWizard.Module.BusinessObjects.AI
{
    /// <summary>
    /// Flags enum controlling which AI tools are available to an agent.
    /// Individual values are stored as <see cref="AIAgentTool"/> rows associated with the agent.
    /// </summary>
    [Flags]
    public enum AgentTool
    {
        [Description("Live bath parameter readings for any stage")]
        BathData       = 1,
        [Description("Historical measurement trend analysis (avg, min, max, direction)")]
        Trend          = 2,
        [Description("Prioritised list of parameters currently outside threshold limits")]
        ThresholdAlert = 4,
        [Description("General database query tools (list / describe / query entities)")]
        DbQuery        = 8,
    }

    /// <summary>
    /// Runtime-configurable agent skills. Skills guide the workflow and answer structure;
    /// they are stored as <see cref="AIAgentSkill"/> rows associated with an agent.
    /// </summary>
    public enum AgentSkill
    {
        [Description("General assistant answers without specialized workflow")]
        GeneralAnswer = 0,
        [Description("Investigate coating defects using evidence, causes, missing data, and actions")]
        CoatingDefectInvestigation = 1,
        [Description("Analyze chemical bath parameters, limits, dosing, and quality risk")]
        ChemicalBathAnalysis = 2,
        [Description("Analyze measurement trends, drift, abnormal periods, and correlations")]
        ProcessTrendAnalysis = 3,
        [Description("Answer from standards, certificates, SOPs, TDS/SDS, and document evidence")]
        DocumentCompliance = 4,
        [Description("Find similar approved case studies and reusable lessons")]
        CaseStudyMatching = 5
    }
    /// <summary>
    /// A named AI agent profile that groups a specific set of instructions
    /// and optionally restricts which documents are visible in RAG search.
    /// When no documents are linked, the agent can search all documents.
    /// </summary>
    [DefaultClassOptions]
    [NavigationItem("AI")]
    [ImageName("BO_Scheduler_Resource")]
    [DefaultProperty(nameof(Name))]
    public class AIAgent : BaseObject
    {
        public AIAgent(Session session) : base(session) { }

        string _name;
        string _description;
        string _systemPromptOverride;
        bool _isActive;

        /// <summary>Short display name shown in the agent picker (e.g. "Quality Inspector", "Process Engineer").</summary>
        [Size(200)]
        public string Name
        {
            get => _name;
            set => SetPropertyValue(nameof(Name), ref _name, value);
        }

        /// <summary>Optional description shown in the agent picker to help the user choose.</summary>
        [Size(2000)]
        public string Description
        {
            get => _description;
            set => SetPropertyValue(nameof(Description), ref _description, value);
        }

        /// <summary>
        /// Optional free-text system prompt that completely replaces the default domain prompt
        /// for this agent. Leave empty to keep the default powder-coating domain prompt.
        /// </summary>
        [Size(SizeAttribute.Unlimited)]
        [ToolTip("Leave empty to use the default domain prompt. Fill in to fully override the agent identity.")]
        public string SystemPromptOverride
        {
            get => _systemPromptOverride;
            set => SetPropertyValue(nameof(SystemPromptOverride), ref _systemPromptOverride, value);
        }

        /// <summary>Only active agents appear in the assistant picker.</summary>
        public bool IsActive
        {
            get => _isActive;
            set => SetPropertyValue(nameof(IsActive), ref _isActive, value);
        }

        // ── Model parameters ──────────────────────────────────────────────────

        float _temperature = 0.7f;
        int _maxTokens;
        bool _ragRoutingPolicyEnabled = true;
        string _contextSkipTerms;
        string _databasePreferredTerms;
        string _knowledgePreferredTerms;
        string _hybridPreferredTerms;

        /// <summary>
        /// Sampling temperature for this agent (0.0 = deterministic, 1.0 = creative).
        /// Defaults to 0.7. Leave at 0 to use the model default.
        /// </summary>
        [ToolTip("Sampling temperature: 0.0 = deterministic / factual, 1.0 = creative. Default is 0.7.")]
        public float Temperature
        {
            get => _temperature;
            set => SetPropertyValue(nameof(Temperature), ref _temperature, value);
        }

        /// <summary>
        /// Maximum tokens the model may generate per response.
        /// Leave at 0 to use the model's default limit.
        /// </summary>
        [ToolTip("Max tokens per response. Set 0 to use the provider default.")]
        public int MaxTokens
        {
            get => _maxTokens;
            set => SetPropertyValue(nameof(MaxTokens), ref _maxTokens, value);
        }

        // ── Runtime RAG routing policy ────────────────────────────────────────

        /// <summary>
        /// Enables this agent's runtime keyword routing policy before automatic RAG/classification.
        /// </summary>
        [ToolTip("When enabled, this agent can route context, database, knowledge, and hybrid requests by editable keyword lists before automatic RAG runs.")]
        public bool RagRoutingPolicyEnabled
        {
            get => _ragRoutingPolicyEnabled;
            set => SetPropertyValue(nameof(RagRoutingPolicyEnabled), ref _ragRoutingPolicyEnabled, value);
        }

        /// <summary>Terms that should route directly to current context or tool policy without automatic RAG.</summary>
        [Size(SizeAttribute.Unlimited)]
        [ToolTip("Newline, comma, or semicolon separated terms that should use context/tool policy first and skip automatic RAG.")]
        public string ContextSkipTerms
        {
            get => _contextSkipTerms;
            set => SetPropertyValue(nameof(ContextSkipTerms), ref _contextSkipTerms, value);
        }

        /// <summary>Terms that should prefer live database/domain tools and skip automatic RAG.</summary>
        [Size(SizeAttribute.Unlimited)]
        [ToolTip("Newline, comma, or semicolon separated terms that should prefer database/domain tools and skip automatic RAG.")]
        public string DatabasePreferredTerms
        {
            get => _databasePreferredTerms;
            set => SetPropertyValue(nameof(DatabasePreferredTerms), ref _databasePreferredTerms, value);
        }

        /// <summary>Terms that should prefer document, SOP, certificate, standard, or case-study retrieval.</summary>
        [Size(SizeAttribute.Unlimited)]
        [ToolTip("Newline, comma, or semicolon separated terms that should prefer document, SOP, certificate, standard, or case-study retrieval.")]
        public string KnowledgePreferredTerms
        {
            get => _knowledgePreferredTerms;
            set => SetPropertyValue(nameof(KnowledgePreferredTerms), ref _knowledgePreferredTerms, value);
        }

        /// <summary>Terms that should prefer a hybrid database plus knowledge investigation.</summary>
        [Size(SizeAttribute.Unlimited)]
        [ToolTip("Newline, comma, or semicolon separated terms that should prefer a hybrid database plus knowledge investigation.")]
        public string HybridPreferredTerms
        {
            get => _hybridPreferredTerms;
            set => SetPropertyValue(nameof(HybridPreferredTerms), ref _hybridPreferredTerms, value);
        }

        // ── Tool enablement ───────────────────────────────────────────────────

        /// <summary>
        /// Tools enabled for this agent. Add or remove <see cref="AIAgentTool"/> rows to control
        /// which tools the agent may invoke. An empty collection means ALL tools are enabled.
        /// </summary>
        [Association("AIAgent-EnabledTools")]
        [Aggregated]
        public XPCollection<AIAgentTool> EnabledTools => GetCollection<AIAgentTool>(nameof(EnabledTools));

        /// <summary>
        /// Returns true when the given tool is in <see cref="EnabledTools"/>,
        /// OR when the collection is empty (= no restriction, all tools allowed).
        /// </summary>
        public bool HasTool(AgentTool tool)
        {
            var col = EnabledTools;
            if (col.Count == 0) return true;
            foreach (var t in col)
                if (t.ToolName == tool) return true;
            return false;
        }

        // ── Skill enablement ──────────────────────────────────────────────────

        /// <summary>
        /// Skills enabled for this agent. Add or remove <see cref="AIAgentSkill"/> rows to control
        /// which workflows the agent may use. An empty collection means ALL skills are enabled.
        /// </summary>
        [Association("AIAgent-EnabledSkills")]
        [Aggregated]
        public XPCollection<AIAgentSkill> EnabledSkills => GetCollection<AIAgentSkill>(nameof(EnabledSkills));

        public bool HasSkill(AgentSkill skill)
        {
            var col = EnabledSkills;
            if (col.Count == 0) return true;
            foreach (var s in col)
                if (s.SkillName == skill) return true;
            return false;
        }

        // ── Instruction sets assigned to this agent ───────────────────────────
        /// <summary>
        /// Instruction sets active for this agent.
        /// When empty the agent inherits all globally active instruction sets.
        /// </summary>
        [Association("AIAgent-Instructions")]
        public XPCollection<AIInstructionSet> Instructions => GetCollection<AIInstructionSet>(nameof(Instructions));

        // ── Documents visible to this agent ───────────────────────────────────
        /// <summary>
        /// Documents this agent can search in RAG.
        /// When empty the agent can search ALL documents (no restriction).
        /// </summary>
        [Association("AIAgent-Documents")]
        public XPCollection<AIDocument> Documents => GetCollection<AIDocument>(nameof(Documents));
    }
}
