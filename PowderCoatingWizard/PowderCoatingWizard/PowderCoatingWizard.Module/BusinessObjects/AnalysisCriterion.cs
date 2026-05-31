using DevExpress.Persistent.Base;
using DevExpress.Persistent.BaseImpl;
using DevExpress.Xpo;
using System.ComponentModel;

namespace PowderCoatingWizard.Module.BusinessObjects
{
    /// <summary>
    /// A single measurable or assessable check within an AnalysisType.
    /// For example, in a Panel Analysis: Gloss, Film Thickness, Cross-Cut Adhesion, Impact.
    /// Each criterion can have one or more thresholds defining pass/warning/fail limits.
    /// </summary>
    [DefaultClassOptions]
    [DefaultProperty(nameof(DisplayName))]
    [NavigationItem("Analysis Configuration")]
    public class AnalysisCriterion : BaseObject
    {
        public AnalysisCriterion(Session session) : base(session) { }

        AnalysisType analysisType;
        string name;
        CriterionValueType valueType;
        ParameterUnit unit;
        string measurementMethod;
        string description;
        bool isRequired;
        int sortOrder;

        [Association("AnalysisType-Criteria")]
        [ToolTip("The analysis type this criterion belongs to.")]
        public AnalysisType AnalysisType
        {
            get => analysisType;
            set => SetPropertyValue(nameof(AnalysisType), ref analysisType, value);
        }

        [Size(150)]
        [ToolTip("Name of this criterion (e.g. Gloss 60°, Dry Film Thickness, Cross-Cut Grade).")]
        public string Name
        {
            get => name;
            set => SetPropertyValue(nameof(Name), ref name, value);
        }

        [ToolTip("Type of value recorded for this criterion.")]
        public CriterionValueType ValueType
        {
            get => valueType;
            set => SetPropertyValue(nameof(ValueType), ref valueType, value);
        }

        [ToolTip("Unit of measurement for numeric criteria (e.g. µm, GU, N/mm²).")]
        public ParameterUnit Unit
        {
            get => unit;
            set => SetPropertyValue(nameof(Unit), ref unit, value);
        }

        [Size(200)]
        [ToolTip("Standard or method used for this measurement (e.g. EN ISO 2813, EN ISO 2178, EN ISO 2409).")]
        public string MeasurementMethod
        {
            get => measurementMethod;
            set => SetPropertyValue(nameof(MeasurementMethod), ref measurementMethod, value);
        }

        [Size(400)]
        [ToolTip("Description or instructions for performing this check.")]
        public string Description
        {
            get => description;
            set => SetPropertyValue(nameof(Description), ref description, value);
        }

        [ToolTip("Whether this criterion is mandatory within the analysis.")]
        public bool IsRequired
        {
            get => isRequired;
            set => SetPropertyValue(nameof(IsRequired), ref isRequired, value);
        }

        [ToolTip("Display order within the analysis type.")]
        public int SortOrder
        {
            get => sortOrder;
            set => SetPropertyValue(nameof(SortOrder), ref sortOrder, value);
        }

        [Aggregated, Association("Criterion-Thresholds")]
        [ToolTip("Pass/warning/fail limits for this criterion.")]
        public XPCollection<AnalysisCriterionThreshold> Thresholds
            => GetCollection<AnalysisCriterionThreshold>(nameof(Thresholds));

        [PersistentAlias("Concat([AnalysisType.Name], Concat(' / ', [Name]))")]
        public string DisplayName => $"{AnalysisType?.Name} / {Name}";

        /// <summary>
        /// Evaluates a numeric value against all active thresholds and returns the most critical status.
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
    }

    public enum CriterionValueType
    {
        [Description("Single measured number (e.g. film thickness in µm)")]
        Numeric,
        [Description("Ordinal grade (e.g. cross-cut Gt0–Gt5, adhesion 0–5)")]
        Grade,
        [Description("Binary pass / fail result")]
        PassFail,
        [Description("Descriptive / free-text observation")]
        Text,
        [Description("Duration in hours (e.g. hours in salt spray before failure)")]
        Hours
    }
}
