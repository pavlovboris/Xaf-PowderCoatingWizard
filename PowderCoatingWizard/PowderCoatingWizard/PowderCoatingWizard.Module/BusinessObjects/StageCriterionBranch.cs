using DevExpress.Persistent.Base;
using DevExpress.Persistent.BaseImpl;
using DevExpress.Xpo;
using System.ComponentModel;

namespace PowderCoatingWizard.Module.BusinessObjects
{
    /// <summary>
    /// One IF-branch inside a <see cref="StageCriterion"/>.
    ///
    /// Branches are evaluated in ascending <see cref="SortOrder"/>.
    /// The first branch whose conditions are ALL satisfied (or ANY, depending on
    /// <see cref="ConditionMode"/>) returns its <see cref="ResultStatus"/> /
    /// <see cref="ResultMessage"/>.  If no branch matches the criterion's
    /// DefaultStatus / DefaultMessage is returned instead.
    /// </summary>
    [DefaultClassOptions]
    [DefaultProperty(nameof(DisplayName))]
    //[NavigationItem("Line Configuration")]
    [NavigationItem(false)]
    public class StageCriterionBranch : BaseObject
    {
        public StageCriterionBranch(Session session) : base(session) { }

        StageCriterion criterion;
        int sortOrder;
        CriterionConditionMode conditionMode;
        ParameterStatus resultStatus;
        string resultMessage;

        [Association("Criterion-Branches")]
        [ToolTip("The criterion this branch belongs to.")]
        public StageCriterion Criterion
        {
            get => criterion;
            set => SetPropertyValue(nameof(Criterion), ref criterion, value);
        }

        [ToolTip("Evaluation priority — lower number = checked first.")]
        public int SortOrder
        {
            get => sortOrder;
            set => SetPropertyValue(nameof(SortOrder), ref sortOrder, value);
        }

        [ToolTip("How multiple conditions in this branch are combined: All = AND, Any = OR.")]
        public CriterionConditionMode ConditionMode
        {
            get => conditionMode;
            set => SetPropertyValue(nameof(ConditionMode), ref conditionMode, value);
        }

        [ToolTip("Status returned when this branch's conditions are met.")]
        public ParameterStatus ResultStatus
        {
            get => resultStatus;
            set => SetPropertyValue(nameof(ResultStatus), ref resultStatus, value);
        }

        [Size(500)]
        [ToolTip("Message shown to the operator when this branch fires (e.g. 'ПЪЛНО ОБНОВЯВАНЕ').")]
        public string ResultMessage
        {
            get => resultMessage;
            set => SetPropertyValue(nameof(ResultMessage), ref resultMessage, value);
        }

        [Aggregated, Association("Branch-Conditions")]
        [ToolTip("Conditions that must be satisfied for this branch to fire.")]
        public XPCollection<StageCriterionCondition> Conditions
            => GetCollection<StageCriterionCondition>(nameof(Conditions));

        [NonPersistent]
        public string DisplayName =>
            $"[{SortOrder}] {(ResultMessage?.Length > 40 ? ResultMessage[..40] + "…" : ResultMessage ?? string.Empty)}";

        /// <summary>
        /// Returns true when all/any of this branch's conditions are satisfied.
        /// </summary>
        public bool IsTriggered(
            IReadOnlyDictionary<Guid, ParameterMeasurement> measurements,
            IReadOnlyDictionary<Guid, ParameterStatus>? criterionResults = null)
        {
            if (Conditions.Count == 0) return false;

            var results = Conditions
                .Select(c => c.IsMet(measurements, criterionResults))
                .ToList();

            return ConditionMode == CriterionConditionMode.All
                ? results.All(r => r)
                : results.Any(r => r);
        }
    }
}
