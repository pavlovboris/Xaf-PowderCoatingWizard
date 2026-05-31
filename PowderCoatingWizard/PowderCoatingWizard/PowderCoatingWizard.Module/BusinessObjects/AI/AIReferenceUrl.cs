using DevExpress.Persistent.Base;
using DevExpress.Persistent.BaseImpl;
using DevExpress.Xpo;
using System.ComponentModel;

namespace PowderCoatingWizard.Module.BusinessObjects.AI
{
    /// <summary>
    /// A URL reference linked to an <see cref="AIInstructionSet"/>.
    /// The AI assistant fetches the page content at runtime and uses it as grounding.
    /// </summary>
    [DefaultProperty(nameof(Url))]
    public class AIReferenceUrl : BaseObject
    {
        public AIReferenceUrl(Session session) : base(session) { }

        string _url;
        string _label;
        bool _fetchAtRuntime;
        AIInstructionSet _instructionSet;

        /// <summary>Full URL to the reference page (standard, datasheet, SOP, etc.)</summary>
        [Size(2000)]
        [ToolTip("e.g. https://www.qualicoat.net/...")]
        public string Url
        {
            get => _url;
            set => SetPropertyValue(nameof(Url), ref _url, value);
        }

        /// <summary>Human-readable label shown in the UI.</summary>
        [Size(300)]
        public string Label
        {
            get => _label;
            set => SetPropertyValue(nameof(Label), ref _label, value);
        }

        /// <summary>
        /// When true the assistant fetches and injects the page content at chat startup.
        /// Use for short reference pages (≤ ~4 000 chars).
        /// For large documents upload them as AIDocument instead.
        /// </summary>
        [ToolTip("Fetch and inject page content into the system prompt at startup. Best for short reference pages.")]
        public bool FetchAtRuntime
        {
            get => _fetchAtRuntime;
            set => SetPropertyValue(nameof(FetchAtRuntime), ref _fetchAtRuntime, value);
        }

        [Association("AIInstructionSet-ReferenceUrls")]
        [Browsable(false)]
        public AIInstructionSet InstructionSet
        {
            get => _instructionSet;
            set => SetPropertyValue(nameof(InstructionSet), ref _instructionSet, value);
        }
    }
}
