using DevExpress.Persistent.Base;
using DevExpress.Persistent.BaseImpl;
using DevExpress.Xpo;
using PowderCoatingWizard.Module.Attributes;
using System.ComponentModel;

namespace PowderCoatingWizard.Module.BusinessObjects
{
    [DefaultClassOptions]
    [DefaultProperty(nameof(Name))]
    [NavigationItem("Bath Configuration")]
    [ImageName("BO_Parameter")]
    [AIQueryable("Definition of a measurable bath parameter (e.g. pH, temperature, concentration). Includes unit, category, and value type.")]
    public class BathParameter : BaseObject
    {
        public BathParameter(Session session) : base(session) { }

        string name;
        ParameterUnit unit;
        ParameterCategory category;
        string description;
        bool isActive;
        ParameterValueType valueType;
        ThresholdEvaluationMode thresholdEvaluationMode;

        [Size(100)]
        [ToolTip("Name of the bath parameter.")]
        public string Name
        {
            get => name;
            set => SetPropertyValue(nameof(Name), ref name, value);
        }

        [ToolTip("Unit of measurement for this parameter.")]
        public ParameterUnit Unit
        {
            get => unit;
            set => SetPropertyValue(nameof(Unit), ref unit, value);
        }

        [ToolTip("Category this parameter belongs to.")]
        public ParameterCategory Category
        {
            get => category;
            set => SetPropertyValue(nameof(Category), ref category, value);
        }

        [Size(500)]
        [ToolTip("Additional description or operator instruction.")]
        public string Description
        {
            get => description;
            set => SetPropertyValue(nameof(Description), ref description, value);
        }

        [ToolTip("Whether this parameter is active and used for control evaluation.")]
        public bool IsActive
        {
            get => isActive;
            set => SetPropertyValue(nameof(IsActive), ref isActive, value);
        }

        [ToolTip("How values for this parameter are recorded: numeric, free text, or a fixed pick-list.")]
        public ParameterValueType ValueType
        {
            get => valueType;
            set => SetPropertyValue(nameof(ValueType), ref valueType, value);
        }

        [ToolTip("How breached thresholds are combined to produce the final EvaluatedStatus.\n" +
                 "Any = at least one breach triggers the status (default).\n" +
                 "All = ALL thresholds of that level must be breached.")]
        public ThresholdEvaluationMode ThresholdEvaluationMode
        {
            get => thresholdEvaluationMode;
            set => SetPropertyValue(nameof(ThresholdEvaluationMode), ref thresholdEvaluationMode, value);
        }

        [Aggregated, Association("Parameter-Thresholds")]
        public XPCollection<ParameterThreshold> Thresholds
            => GetCollection<ParameterThreshold>(nameof(Thresholds));

        [Aggregated, Association("Parameter-PredefinedValues")]
        [ToolTip("Fixed options available when ValueType is Predefined.")]
        public XPCollection<PredefinedParameterValue> PredefinedValues
            => GetCollection<PredefinedParameterValue>(nameof(PredefinedValues));

        /// <summary>
        /// Evaluates a numeric value against all defined thresholds.
        /// Returns the most critical status found.
        /// </summary>
        public ParameterStatus EvaluateStatus(double value)
        {
            var result = ParameterStatus.OK;
            foreach (var threshold in Thresholds)
            {
                var status = threshold.EvaluateStatus(value);
                if (status > result)
                    result = status;
            }
            return result;
        }

        /// <summary>
        /// Returns the status carried by the selected predefined value (for Predefined parameters).
        /// </summary>
        public static ParameterStatus EvaluateStatus(PredefinedParameterValue selected)
            => selected?.Status ?? ParameterStatus.OK;
    }

    public enum ParameterStatus
    {
        [Description("Parameter is within acceptable limits")]
        OK = 0,
        [Description("Parameter is outside recommended range — operator attention needed")]
        Warning = 1,
        [Description("Parameter is outside critical limits — immediate action required")]
        Alarm = 2
    }

    /// <summary>
    /// Controls how multiple breached thresholds at the same status level are combined
    /// when computing <see cref="ParameterMeasurement.EvaluatedStatus"/>.
    /// </summary>
    public enum ThresholdEvaluationMode
    {
        [Description("Any breach triggers the status (OR — most sensitive, default)")]
        Any = 0,
        [Description("ALL thresholds of that level must be breached to trigger the status (AND)")]
        All = 1
    }
}
