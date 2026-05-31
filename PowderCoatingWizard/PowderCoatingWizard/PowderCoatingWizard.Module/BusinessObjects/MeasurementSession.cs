using DevExpress.Persistent.Base;
using DevExpress.Persistent.BaseImpl;
using DevExpress.Xpo;
using PowderCoatingWizard.Module.Attributes;
using System.ComponentModel;

namespace PowderCoatingWizard.Module.BusinessObjects
{
    /// <summary>
    /// One complete operator measurement round for an entire production line.
    /// Contains individual readings for every relevant bath/tank stage on that line.
    /// One session = one pass through the full line by the operator.
    /// </summary>
    [DefaultClassOptions]
    [DefaultProperty(nameof(DisplayName))]
    [NavigationItem("Measurements")]
    [ImageName("BO_SaleOrder")]
    [AIQueryable("One full operator measurement round for a production line — date, operator, line, and status. Parent of all individual parameter measurements.")]
    public class MeasurementSession : BaseObject
    {
        public MeasurementSession(Session session) : base(session) { }

        ProductionLine line;
        DateTime measuredOn;
        string operatorName;
        string notes;
        Operator @operator;

        [ToolTip("The production line this measurement round was performed on.")]
        public ProductionLine Line
        {
            get => line;
            set => SetPropertyValue(nameof(Line), ref line, value);
        }

        [ToolTip("Date and time when the measurement round was performed.")]
        public DateTime MeasuredOn
        {
            get => measuredOn;
            set => SetPropertyValue(nameof(MeasuredOn), ref measuredOn, value);
        }

        [Size(100)]
        [ToolTip("Name of the operator who performed the measurements.")]
        public string OperatorName
        {
            get => operatorName;
            set => SetPropertyValue(nameof(OperatorName), ref operatorName, value);
        }

        [ToolTip("Operator object from the line's roster. Selecting one auto-fills OperatorName.")]
        public Operator Operator
        {
            get => @operator;
            set
            {
                SetPropertyValue(nameof(Operator), ref @operator, value);
                if (value != null && OperatorName != value.Name)
                    OperatorName = value.Name;
            }
        }

        [Size(-1)]
        [ToolTip("Optional notes for this measurement round.")]
        public string Notes
        {
            get => notes;
            set => SetPropertyValue(nameof(Notes), ref notes, value);
        }

        [Aggregated, Association("Session-Measurements")]
        [ToolTip("Individual parameter readings recorded in this session.")]
        public XPCollection<ParameterMeasurement> Measurements
            => GetCollection<ParameterMeasurement>(nameof(Measurements));

        [Association("Session-AnalysisRecords")]
        [ToolTip("Quality analysis records linked to this measurement session.")]
        public XPCollection<AnalysisRecord> AnalysisRecords
            => GetCollection<AnalysisRecord>(nameof(AnalysisRecords));

        [Association("Session-ArchiveRows")]
        [Browsable(false)]
        public XPCollection<BathStageSheetArchiveRow> ArchiveRows
            => GetCollection<BathStageSheetArchiveRow>(nameof(ArchiveRows));

        [PersistentAlias("Concat([Line.Name], Concat(' – ', ToStr([MeasuredOn])))")] 
        public string DisplayName => $"{Line?.Name} – {MeasuredOn:dd.MM.yyyy HH:mm}";
    }
}
