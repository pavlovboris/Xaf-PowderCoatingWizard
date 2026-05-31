using DevExpress.Persistent.Base;
using DevExpress.Persistent.BaseImpl;
using DevExpress.Xpo;
using PowderCoatingWizard.Module.BusinessObjects;
using System.ComponentModel;

namespace PowderCoatingWizard.Module.BusinessObjects.AI
{
    /// <summary>
    /// A named set of instructions, standards and reference URLs that are injected
    /// into the AI assistant's system prompt.  Can be global or scoped to a specific
    /// production line.
    /// </summary>
    [DefaultClassOptions]
    [NavigationItem("AI")]
    [ImageName("BO_Task")]
    [DefaultProperty(nameof(Name))]
    public class AIInstructionSet : BaseObject
    {
        public AIInstructionSet(Session session) : base(session) { }

        string _name;
        string _instructions;
        ProductionLine _productionLine;
        bool _isActive;
        int _priority;

        [Size(200)]
        [ToolTip("Short descriptive name, e.g. 'Qualicoat Class 1' or 'Interpon Global SOP'.")]
        public string Name
        {
            get => _name;
            set => SetPropertyValue(nameof(Name), ref _name, value);
        }

        /// <summary>
        /// Free-text instructions / standards text. Write in natural language.
        /// The AI will treat this as authoritative guidance.
        /// </summary>
        [Size(SizeAttribute.Unlimited)]
        [EditorAlias("RichText")]
        [ToolTip("Paste or type the full standard text, SOP, or any domain instructions here.")]
        public string Instructions
        {
            get => _instructions;
            set => SetPropertyValue(nameof(Instructions), ref _instructions, value);
        }

        /// <summary>
        /// Optional: scope to one production line.
        /// Leave blank to make this instruction set apply globally to all lines.
        /// </summary>
        [ToolTip("Scope to a specific line, or leave empty for global application.")]
        public ProductionLine ProductionLine
        {
            get => _productionLine;
            set => SetPropertyValue(nameof(ProductionLine), ref _productionLine, value);
        }

        [ToolTip("Only active instruction sets are included in the AI system prompt.")]
        public bool IsActive
        {
            get => _isActive;
            set => SetPropertyValue(nameof(IsActive), ref _isActive, value);
        }

        /// <summary>
        /// Higher priority = listed first in the system prompt.
        /// Use this to ensure the most critical standards are seen first by the AI.
        /// </summary>
        [ToolTip("Higher = injected earlier in the system prompt (0 = lowest).")]
        public int Priority
        {
            get => _priority;
            set => SetPropertyValue(nameof(Priority), ref _priority, value);
        }

        [Association("AIInstructionSet-ReferenceUrls")]
        [Aggregated]
        public XPCollection<AIReferenceUrl> ReferenceUrls => GetCollection<AIReferenceUrl>(nameof(ReferenceUrls));

        /// <summary>Agents that include this instruction set. Managed from the AIAgent side.</summary>
        [Association("AIAgent-Instructions")]
        [Browsable(false)]
        public XPCollection<AIAgent> Agents => GetCollection<AIAgent>(nameof(Agents));
    }
}
