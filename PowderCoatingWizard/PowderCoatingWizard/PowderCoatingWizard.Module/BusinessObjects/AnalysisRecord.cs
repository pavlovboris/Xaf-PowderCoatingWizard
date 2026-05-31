using DevExpress.Persistent.Base;
using DevExpress.Persistent.BaseImpl;
using DevExpress.Xpo;
using System.ComponentModel;

namespace PowderCoatingWizard.Module.BusinessObjects
{
    /// <summary>
    /// Records a single performed quality analysis based on an AnalysisType template.
    /// Captures who performed it, when, on which line, and optionally links it
    /// to a MeasurementSession (bath control round) for full traceability.
    /// </summary>
    [DefaultClassOptions]
    [DefaultProperty(nameof(DisplayName))]
    [NavigationItem("Quality Analysis")]
    [ImageName("BO_Audit")]
    public class AnalysisRecord : BaseObject
    {
        public AnalysisRecord(Session session) : base(session) { }

        AnalysisType analysisType;
        ProductionLine line;
        MeasurementSession measurementSession;
        DateTime performedOn;
        string operatorName;
        string sampleReference;
        string notes;
        AnalysisRecordStatus status;

        [ToolTip("The analysis type (template) this record is based on.")]
        public AnalysisType AnalysisType
        {
            get => analysisType;
            set => SetPropertyValue(nameof(AnalysisType), ref analysisType, value);
        }

        [ToolTip("The production line this analysis was performed on.")]
        public ProductionLine Line
        {
            get => line;
            set => SetPropertyValue(nameof(Line), ref line, value);
        }

        [Association("Session-AnalysisRecords")]
        [ToolTip("Optional link to the bath measurement session this analysis is associated with.")]
        public MeasurementSession MeasurementSession
        {
            get => measurementSession;
            set => SetPropertyValue(nameof(MeasurementSession), ref measurementSession, value);
        }

        [ToolTip("Date and time when the analysis was performed.")]
        public DateTime PerformedOn
        {
            get => performedOn;
            set => SetPropertyValue(nameof(PerformedOn), ref performedOn, value);
        }

        [Size(100)]
        [ToolTip("Name of the operator who performed the analysis.")]
        public string OperatorName
        {
            get => operatorName;
            set => SetPropertyValue(nameof(OperatorName), ref operatorName, value);
        }

        [Size(150)]
        [ToolTip("Sample or panel reference identifier (e.g. panel number, part ID, batch).")]
        public string SampleReference
        {
            get => sampleReference;
            set => SetPropertyValue(nameof(SampleReference), ref sampleReference, value);
        }

        [Size(500)]
        [ToolTip("General notes or observations for this analysis.")]
        public string Notes
        {
            get => notes;
            set => SetPropertyValue(nameof(Notes), ref notes, value);
        }

        [ToolTip("Overall status of this analysis record.")]
        public AnalysisRecordStatus Status
        {
            get => status;
            set => SetPropertyValue(nameof(Status), ref status, value);
        }

        [Aggregated, Association("AnalysisRecord-Results")]
        [ToolTip("Individual criterion results recorded in this analysis.")]
        public XPCollection<CriterionResult> Results
            => GetCollection<CriterionResult>(nameof(Results));

        [PersistentAlias("Concat([AnalysisType.Name], Concat(' – ', ToStr([PerformedOn])))")]
        public string DisplayName => $"{AnalysisType?.Name} – {PerformedOn:dd.MM.yyyy HH:mm}";
    }

    public enum AnalysisRecordStatus
    {
        [Description("Not all criteria have been filled in yet")]
        InProgress,
        [Description("All criteria recorded")]
        Complete,
        [Description("All required criteria passed")]
        Passed,
        [Description("One or more required criteria failed")]
        Failed,
        [Description("Record invalidated / cancelled")]
        Voided
    }
}
