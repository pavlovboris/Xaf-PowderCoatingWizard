using DevExpress.Persistent.Base;
using DevExpress.Persistent.BaseImpl;
using DevExpress.Xpo;
using System.ComponentModel;

namespace PowderCoatingWizard.Module.BusinessObjects
{
    [DefaultClassOptions]
    [DefaultProperty(nameof(Name))]
    [NavigationItem("Bath Configuration")]
    public class ParameterUnit : BaseObject
    {
        public ParameterUnit(Session session) : base(session) { }

        string name;
        string symbol;
        string description;

        [Size(100)]
        [ToolTip("Full name of the unit (e.g. pH unit, Microsiemens per centimetre).")]
        public string Name
        {
            get => name;
            set => SetPropertyValue(nameof(Name), ref name, value);
        }

        [Size(30)]
        [ToolTip("Symbol shown in UI and reports (e.g. pH, uS/cm, C, mg/L, mg/m2, sec).")]
        public string Symbol
        {
            get => symbol;
            set => SetPropertyValue(nameof(Symbol), ref symbol, value);
        }

        [Size(300)]
        [ToolTip("Additional description of the unit.")]
        public string Description
        {
            get => description;
            set => SetPropertyValue(nameof(Description), ref description, value);
        }

        public override string ToString() => string.IsNullOrWhiteSpace(symbol) ? name : symbol;
    }
}