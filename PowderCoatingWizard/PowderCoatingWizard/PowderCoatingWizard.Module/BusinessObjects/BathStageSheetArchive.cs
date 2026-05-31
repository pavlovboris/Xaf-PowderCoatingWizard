using DevExpress.Persistent.Base;
using DevExpress.Persistent.BaseImpl;
using DevExpress.Xpo;
using System.ComponentModel;

namespace PowderCoatingWizard.Module.BusinessObjects
{
    /// <summary>
    /// One archived row in the Bath Stage Sheet — corresponds to one MeasurementSession for one LineStage.
    /// Created / updated by the "Archive" actions; read directly without any recalculation.
    /// </summary>
    [DefaultProperty(nameof(DisplayName))]
    [NavigationItem("Archive")]  

    //[Browsable(false)]
    public class BathStageSheetArchiveRow : BaseObject
    {
        public BathStageSheetArchiveRow(Session session) : base(session) { }

        LineStage stage;
        MeasurementSession measurementSession;
        DateTime archivedOn;
        DateTime sessionDate;
        string operatorName;

        /// <summary>The stage this row belongs to.</summary>
        [Association("Stage-ArchiveRows")]
        [Indexed]
        public LineStage Stage
        {
            get => stage;
            set => SetPropertyValue(nameof(Stage), ref stage, value);
        }

        /// <summary>The measurement session this row represents.</summary>
        [Association("Session-ArchiveRows")]
        [Indexed]
        public MeasurementSession MeasurementSession
        {
            get => measurementSession;
            set => SetPropertyValue(nameof(MeasurementSession), ref measurementSession, value);
        }

        /// <summary>When this row was last archived / recalculated.</summary>
        public DateTime ArchivedOn
        {
            get => archivedOn;
            set => SetPropertyValue(nameof(ArchivedOn), ref archivedOn, value);
        }

        /// <summary>Denormalised copy of MeasurementSession.MeasuredOn — allows ordering without a join.</summary>
        [Indexed]
        public DateTime SessionDate
        {
            get => sessionDate;
            set => SetPropertyValue(nameof(SessionDate), ref sessionDate, value);
        }

        /// <summary>Denormalised copy of MeasurementSession.OperatorName.</summary>
        [Size(100)]
        public string OperatorName
        {
            get => operatorName;
            set => SetPropertyValue(nameof(OperatorName), ref operatorName, value);
        }

        /// <summary>All cell values for this row (one per column key).</summary>
        [Aggregated, Association("ArchiveRow-Cells")]
        public XPCollection<BathStageSheetArchiveCell> Cells
            => GetCollection<BathStageSheetArchiveCell>(nameof(Cells));

        [PersistentAlias(nameof(sessionDate))]
        public string DisplayName => $"{SessionDate:dd.MM.yyyy HH:mm}  {OperatorName}";
    }

    /// <summary>
    /// One archived cell — the computed value for a single (ArchiveRow, ColumnKey) pair.
    /// ColumnKey matches the keys produced by BathStageSheetService:
    ///   V__paramOid  — parameter value text
    ///   S__paramOid  — parameter status
    ///   C__critOid   — criterion message / status
    ///   F__fieldOid  — calculated field result
    ///   X__tmplOid__outMapOid — Excel template output
    /// </summary>
    //[Browsable(false)]
    public class BathStageSheetArchiveCell : BaseObject
    {
        public BathStageSheetArchiveCell(Session session) : base(session) { }

        BathStageSheetArchiveRow archiveRow;
        string columnKey;
        string textValue;
        int? statusValue;

        /// <summary>Parent archive row.</summary>
        [Association("ArchiveRow-Cells")]
        [Indexed]
        public BathStageSheetArchiveRow ArchiveRow
        {
            get => archiveRow;
            set => SetPropertyValue(nameof(ArchiveRow), ref archiveRow, value);
        }

        /// <summary>Column key as produced by BathStageSheetService (e.g. "C__guid", "F__guid").</summary>
        [Size(100)]
        [Indexed]
        public string ColumnKey
        {
            get => columnKey;
            set => SetPropertyValue(nameof(ColumnKey), ref columnKey, value);
        }

        /// <summary>
        /// String representation of the cell value.
        /// For criterion cells this is the message text.
        /// For parameter value/calculated field cells this is the display string.
        /// For status cells this is the status name (OK / Warning / Alarm).
        /// </summary>
        [Size(-1)]
        public string TextValue
        {
            get => textValue;
            set => SetPropertyValue(nameof(TextValue), ref textValue, value);
        }

        /// <summary>
        /// Integer representation of ParameterStatus for quick filtering.
        /// Null for non-status columns.
        /// </summary>
        public int? StatusValue
        {
            get => statusValue;
            set => SetPropertyValue(nameof(StatusValue), ref statusValue, value);
        }
    }
}
