using DevExpress.ExpressApp.DC;
using DevExpress.Persistent.Base;

namespace PowderCoatingWizard.Module.BusinessObjects
{
    /// <summary>
    /// Transient parameter object shown in the "Rebuild Archive" popup.
    /// </summary>
    [DomainComponent]
    [DefaultClassOptions]
    [NavigationItem(false)]
    public class ArchiveRebuildParams
    {
        [XafDisplayName("From date")]
        [ToolTip("Leave empty to rebuild from the very beginning.")]
        public DateTime? DateFrom { get; set; }

        [XafDisplayName("To date")]
        [ToolTip("Leave empty to rebuild up to the latest session.")]
        public DateTime? DateTo { get; set; }
    }
}
