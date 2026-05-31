using DevExpress.Persistent.Base;
using DevExpress.Persistent.BaseImpl;
using DevExpress.Xpo;
using System.ComponentModel;

namespace PowderCoatingWizard.Module.BusinessObjects
{
    /// <summary>
    /// An operator who can perform measurements on a production line.
    /// Linked to a specific <see cref="ProductionLine"/> so each installation
    /// maintains its own roster of operators.
    /// </summary>
    [DefaultClassOptions]
    [DefaultProperty(nameof(Name))]
    [NavigationItem("Bath Configuration")]
    public class Operator : BaseObject
    {
        public Operator(Session session) : base(session) { }

        string name;
        string notes;
        bool isActive;
        ProductionLine line;

        [Size(150)]
        [ToolTip("Full name of the operator.")]
        public string Name
        {
            get => name;
            set => SetPropertyValue(nameof(Name), ref name, value);
        }

        [Size(500)]
        [ToolTip("Optional notes about this operator.")]
        public string Notes
        {
            get => notes;
            set => SetPropertyValue(nameof(Notes), ref notes, value);
        }

        [ToolTip("Whether this operator is active and selectable in measurement sheets.")]
        public bool IsActive
        {
            get => isActive;
            set => SetPropertyValue(nameof(IsActive), ref isActive, value);
        }

        [Association("ProductionLine-Operators")]
        [ToolTip("The production line this operator belongs to.")]
        public ProductionLine Line
        {
            get => line;
            set => SetPropertyValue(nameof(Line), ref line, value);
        }
    }
}
