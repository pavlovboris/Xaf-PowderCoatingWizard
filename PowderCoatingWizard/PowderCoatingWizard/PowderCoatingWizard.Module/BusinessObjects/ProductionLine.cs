using DevExpress.Persistent.Base;
using DevExpress.Persistent.BaseImpl;
using DevExpress.Xpo;
using System.ComponentModel;

namespace PowderCoatingWizard.Module.BusinessObjects
{
    /// <summary>
    /// Represents a production coating line with its configuration,
    /// certification standards, and pre-treatment bath setup.
    /// </summary>
    [DefaultClassOptions]
    [DefaultProperty(nameof(Name))]
    [NavigationItem("Line Configuration")]
    [ImageName("BO_Organization")]
    public class ProductionLine : BaseObject
    {
        public ProductionLine(Session session) : base(session) { }

        string name;
        string location;
        LineOrientation orientation;
        PretreatmentMethod pretreatmentMethod;
        string description;
        bool isActive;
        int numberOfStages;
        string gridLayoutXml;
        string blazorLayoutJson;

        [Size(150)]
        [ToolTip("Name or identifier of the production line (e.g. Line 1, Vertical AA).")]
        public string Name
        {
            get => name;
            set => SetPropertyValue(nameof(Name), ref name, value);
        }

        [Size(200)]
        [ToolTip("Physical location or plant section of this line.")]
        public string Location
        {
            get => location;
            set => SetPropertyValue(nameof(Location), ref location, value);
        }

        [ToolTip("Orientation of the line — vertical (profiles hanging) or horizontal (flat parts).")]
        public LineOrientation Orientation
        {
            get => orientation;
            set => SetPropertyValue(nameof(Orientation), ref orientation, value);
        }

        [ToolTip("Pre-treatment method used on this line.")]
        public PretreatmentMethod PretreatmentMethod
        {
            get => pretreatmentMethod;
            set => SetPropertyValue(nameof(PretreatmentMethod), ref pretreatmentMethod, value);
        }

        [ToolTip("Total number of bath/tank stages in the pre-treatment tunnel.")]
        public int NumberOfStages
        {
            get => numberOfStages;
            set => SetPropertyValue(nameof(NumberOfStages), ref numberOfStages, value);
        }

        [Size(500)]
        [ToolTip("Additional notes about the line configuration or process.")]
        public string Description
        {
            get => description;
            set => SetPropertyValue(nameof(Description), ref description, value);
        }

        [ToolTip("Whether this line is currently active and in production.")]
        public bool IsActive
        {
            get => isActive;
            set => SetPropertyValue(nameof(IsActive), ref isActive, value);
        }

        /// <summary>Persisted BandedGridView / GridView layout XML for the Measurement Sheet. Saved per-line.</summary>
        [Size(-1)]
        [ToolTip("Grid layout XML for the Measurement Sheet — saved per production line.")]
        [Browsable(false)]
        public string GridLayoutXml
        {
            get => gridLayoutXml;
            set => SetPropertyValue(nameof(GridLayoutXml), ref gridLayoutXml, value);
        }

        /// <summary>Persisted DxGrid layout JSON for the Blazor Measurement Sheet. Saved per-line.</summary>
        [Size(-1)]
        [Browsable(false)]
        public string BlazorLayoutJson
        {
            get => blazorLayoutJson;
            set => SetPropertyValue(nameof(BlazorLayoutJson), ref blazorLayoutJson, value);
        }

        [Aggregated, Association("Line-Stages")]
        [ToolTip("Ordered list of bath/tank stages on this line.")]
        public XPCollection<LineStage> Stages
            => GetCollection<LineStage>(nameof(Stages));

        [Aggregated, Association("Line-Certifications")]
        [ToolTip("Quality certifications and standards applicable to this line.")]
        public XPCollection<LineCertification> Certifications
            => GetCollection<LineCertification>(nameof(Certifications));
    }

    public enum LineOrientation
    {
        [Description("Vertical — profiles/parts hanging vertically")]
        Vertical,
        [Description("Horizontal — flat parts on conveyor")]
        Horizontal
    }

    public enum PretreatmentMethod
    {
        [Description("Spray tunnel (струйно)")]
        Spray,
        [Description("Dip / immersion tanks (потапяне)")]
        Dip,
        [Description("Cascade rinse system (каскади)")]
        Cascade
    }
}
