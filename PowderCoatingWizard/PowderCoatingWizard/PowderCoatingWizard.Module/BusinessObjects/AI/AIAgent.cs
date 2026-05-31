using DevExpress.Persistent.Base;
using DevExpress.Persistent.BaseImpl;
using DevExpress.Xpo;
using System.ComponentModel;

namespace PowderCoatingWizard.Module.BusinessObjects.AI
{
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
