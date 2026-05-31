using DevExpress.Persistent.Base;
using DevExpress.Persistent.BaseImpl;
using DevExpress.Xpo;
using System.ComponentModel;

namespace PowderCoatingWizard.Module.BusinessObjects
{
    public enum ParameterValueType
    {
        [Description("Numeric — measured number with optional unit")]
        Numeric,
        [Description("Free text — operator types any string")]
        FreeText,
        [Description("Predefined — operator picks from a fixed list")]
        Predefined
    }

    /// <summary>
    /// One option in the fixed pick-list for a <see cref="BathParameter"/>
    /// whose <see cref="BathParameter.ValueType"/> is <see cref="ParameterValueType.Predefined"/>.
    /// </summary>
    [DefaultClassOptions]
    [DefaultProperty(nameof(Name))]
    //[NavigationItem("Bath Configuration")]
    [NavigationItem(false)]

    public class PredefinedParameterValue : BaseObject
    {
        public PredefinedParameterValue(Session session) : base(session) { }

        BathParameter parameter;
        string name;
        ParameterStatus status;
        int sortOrder;

        [Association("Parameter-PredefinedValues")]
        [ToolTip("The bath parameter this option belongs to.")]
        public BathParameter Parameter
        {
            get => parameter;
            set => SetPropertyValue(nameof(Parameter), ref parameter, value);
        }

        [Size(200)]
        [ToolTip("Display text shown to the operator (e.g. 'OK', 'Lekkage', 'Niet gemeten').")]
        public string Name
        {
            get => name;
            set => SetPropertyValue(nameof(Name), ref name, value);
        }

        [ToolTip("Status that this option implies — used for alarm/warning colouring.")]
        public ParameterStatus Status
        {
            get => status;
            set => SetPropertyValue(nameof(Status), ref status, value);
        }

        [ToolTip("Order in which options appear in the drop-down (ascending).")]
        public int SortOrder
        {
            get => sortOrder;
            set => SetPropertyValue(nameof(SortOrder), ref sortOrder, value);
        }

        public override string ToString() => Name ?? base.ToString();
    }
}
