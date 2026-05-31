using DevExpress.Persistent.Base;
using DevExpress.Persistent.BaseImpl;
using DevExpress.Xpo;
using System.ComponentModel;

namespace PowderCoatingWizard.Module.BusinessObjects
{
    /// <summary>
    /// One condition within a StageCriterion rule.
    /// Compares the measured NumericValue (or TextValue) of a specific BathParameter
    /// against a configured threshold.
    /// </summary>
    [DefaultClassOptions]
    [DefaultProperty(nameof(DisplayName))]
    //[NavigationItem("Line Configuration")]
    [NavigationItem(false)]

    public class StageCriterionCondition : BaseObject
    {
        public StageCriterionCondition(Session session) : base(session) { }

        StageCriterionBranch branch;
        StageParameter stageParameter;
        StageCriterion criterionRef;
        CriterionOperator conditionOperator;
        double thresholdValue;
        double thresholdHigh;
        string textValue;
        PredefinedParameterValue predefinedValueRef;
        ParameterStatus criterionStatusValue;

        [Association("Branch-Conditions")]
        [ToolTip("The branch this condition belongs to.")]
        public StageCriterionBranch Branch
        {
            get => branch;
            set => SetPropertyValue(nameof(Branch), ref branch, value);
        }

        /// <summary>
        /// Links to a StageParameter so we are constrained to the parameters
        /// that are actually configured on the parent stage.
        /// </summary>
        [ToolTip("The stage parameter whose measured value will be checked.")]
        [DataSourceProperty(nameof(AvailableStageParameters))]
        public StageParameter StageParameter
        {
            get => stageParameter;
            set => SetPropertyValue(nameof(StageParameter), ref stageParameter, value);
        }

        [ToolTip("Comparison operator applied to the measured value.")]
        public CriterionOperator Operator
        {
            get => conditionOperator;
            set => SetPropertyValue(nameof(Operator), ref conditionOperator, value);
        }

        [ToolTip("Lower threshold value (or single value for non-Between operators).")]
        public double ThresholdValue
        {
            get => thresholdValue;
            set => SetPropertyValue(nameof(ThresholdValue), ref thresholdValue, value);
        }

        [ToolTip("Upper threshold value (used only for the Between operator).")]
        public double ThresholdHigh
        {
            get => thresholdHigh;
            set => SetPropertyValue(nameof(ThresholdHigh), ref thresholdHigh, value);
        }

        [Size(200)]
        [ToolTip("Expected text value (used only for = and != operators on text parameters).")]
        public string TextValue
        {
            get => textValue;
            set => SetPropertyValue(nameof(TextValue), ref textValue, value);
        }

        [ToolTip("The predefined option to compare against (used with PredefinedIs / PredefinedIsNot).")]
        [DataSourceProperty(nameof(AvailablePredefinedValues))]
        public PredefinedParameterValue PredefinedValueRef
        {
            get => predefinedValueRef;
            set => SetPropertyValue(nameof(PredefinedValueRef), ref predefinedValueRef, value);
        }

        [ToolTip("Another criterion on the same stage whose result status will be checked (used with CriterionIs / CriterionIsNot).")]
        [DataSourceProperty(nameof(AvailableCriteria))]
        public StageCriterion CriterionRef
        {
            get => criterionRef;
            set => SetPropertyValue(nameof(CriterionRef), ref criterionRef, value);
        }

        [ToolTip("The status value to compare against (used with CriterionIs / CriterionIsNot).")]
        public ParameterStatus CriterionStatusValue
        {
            get => criterionStatusValue;
            set => SetPropertyValue(nameof(CriterionStatusValue), ref criterionStatusValue, value);
        }

        // ── Transient helper ────────────────────────────────────────────────
        [Browsable(false)]
        public IList<StageParameter> AvailableStageParameters
        {
            get
            {
                var stage = Branch?.Criterion?.Stage;
                if (stage == null) return [];
                return [.. stage.Parameters];
            }
        }
        [Browsable(false)]
        public IList<StageCriterion> AvailableCriteria
        {
            get
            {
                var criterion = Branch?.Criterion;
                var stage = criterion?.Stage;
                if (stage == null) return [];
                // Exclude self to prevent direct self-reference cycles
                return [.. stage.Criteria.Where(c => c.Oid != criterion!.Oid)];
            }
        }

        [Browsable(false)]
        public IList<PredefinedParameterValue> AvailablePredefinedValues
        {
            get
            {
                var param = StageParameter?.Parameter;
                if (param == null) return [];
                return [.. param.PredefinedValues.OrderBy(v => v.SortOrder).ThenBy(v => v.Name)];
            }
        }

        [PersistentAlias("Concat([StageParameter.Parameter.Name], Concat(' ', [Operator]))")]
        public string DisplayName =>
            Operator is CriterionOperator.CriterionIs or CriterionOperator.CriterionIsNot
                ? $"{CriterionRef?.Name} {Operator} {CriterionStatusValue}"
                : Operator is CriterionOperator.StatusIs or CriterionOperator.StatusIsNot
                    ? $"{StageParameter?.Parameter?.Name} {Operator} {CriterionStatusValue}"
                    : Operator is CriterionOperator.PredefinedIs or CriterionOperator.PredefinedIsNot
                        ? $"{StageParameter?.Parameter?.Name} {Operator} '{PredefinedValueRef?.Name}'"
                        : $"{StageParameter?.Parameter?.Name} {Operator} {(Operator == CriterionOperator.Between ? $"{ThresholdValue}\u2013{ThresholdHigh}" : Operator == CriterionOperator.EqualText || Operator == CriterionOperator.NotEqualText ? $"'{TextValue}'" : ThresholdValue.ToString())}";

        // ── Evaluation ──────────────────────────────────────────────────────
        /// <summary>
        /// Returns true when this condition is satisfied.
        /// <paramref name="measurements"/>: BathParameter.Oid → ParameterMeasurement<br/>
        /// <paramref name="criterionResults"/>: StageCriterion.Oid → already-evaluated ParameterStatus
        ///   (pass null / empty when not needed).
        /// </summary>
        public bool IsMet(
            IReadOnlyDictionary<Guid, ParameterMeasurement> measurements,
            IReadOnlyDictionary<Guid, ParameterStatus>? criterionResults = null)
        {
            // ── Criterion-status operators ───────────────────────────────────
            if (conditionOperator is CriterionOperator.CriterionIs or CriterionOperator.CriterionIsNot)
            {
                if (CriterionRef == null) return false;
                var refStatus = criterionResults != null
                    && criterionResults.TryGetValue(CriterionRef.Oid, out var s)
                    ? s : ParameterStatus.OK;
                return conditionOperator == CriterionOperator.CriterionIs
                    ? refStatus == criterionStatusValue
                    : refStatus != criterionStatusValue;
            }

            // ── Parameter-value operators ────────────────────────────────────
            if (StageParameter?.Parameter == null) return false;
            if (!measurements.TryGetValue(StageParameter.Parameter.Oid, out var m)) return false;

            return conditionOperator switch
            {
                CriterionOperator.GreaterThan    => m.NumericValue.HasValue && m.NumericValue.Value > thresholdValue,
                CriterionOperator.GreaterOrEqual => m.NumericValue.HasValue && m.NumericValue.Value >= thresholdValue,
                CriterionOperator.LessThan       => m.NumericValue.HasValue && m.NumericValue.Value < thresholdValue,
                CriterionOperator.LessOrEqual    => m.NumericValue.HasValue && m.NumericValue.Value <= thresholdValue,
                CriterionOperator.Between        => m.NumericValue.HasValue
                                                    && m.NumericValue.Value >= thresholdValue
                                                    && m.NumericValue.Value <= thresholdHigh,
                CriterionOperator.EqualText      => string.Equals(
                                                        m.TextValue, textValue,
                                                        StringComparison.OrdinalIgnoreCase),
                CriterionOperator.NotEqualText   => !string.Equals(
                                                        m.TextValue, textValue,
                                                        StringComparison.OrdinalIgnoreCase),
                CriterionOperator.IsEmpty        => !m.NumericValue.HasValue
                                                    && string.IsNullOrWhiteSpace(m.TextValue),
                CriterionOperator.StatusIs       => m.EvaluatedStatus == criterionStatusValue,
                CriterionOperator.StatusIsNot    => m.EvaluatedStatus != criterionStatusValue,
                CriterionOperator.PredefinedIs   => predefinedValueRef != null
                                                    && m.SelectedValue?.Oid == predefinedValueRef.Oid,
                CriterionOperator.PredefinedIsNot => predefinedValueRef != null
                                                    && m.SelectedValue?.Oid != predefinedValueRef.Oid,
                _ => false
            };
        }
    }

    public enum CriterionOperator
    {
        [Description("> Greater than")]
        GreaterThan,
        [Description(">= Greater than or equal")]
        GreaterOrEqual,
        [Description("< Less than")]
        LessThan,
        [Description("<= Less than or equal")]
        LessOrEqual,
        [Description("Between (inclusive)")]
        Between,
        [Description("= Equal (text)")]
        EqualText,
        [Description("≠ Not equal (text)")]
        NotEqualText,
        [Description("Is empty / not measured")]
        IsEmpty,
        [Description("Status IS (Warning / Alarm / OK)")]
        StatusIs,
        [Description("Status IS NOT (Warning / Alarm / OK)")]
        StatusIsNot,
        [Description("Criterion result IS status")]
        CriterionIs,
        [Description("Criterion result IS NOT status")]
        CriterionIsNot,
        [Description("= Predefined value IS")]
        PredefinedIs,
        [Description("≠ Predefined value IS NOT")]
        PredefinedIsNot
    }
}
