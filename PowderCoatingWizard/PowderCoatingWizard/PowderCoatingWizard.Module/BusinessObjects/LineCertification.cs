using DevExpress.Persistent.Base;
using DevExpress.Persistent.BaseImpl;
using DevExpress.Xpo;
using PowderCoatingWizard.Module.Attributes;
using System.ComponentModel;

namespace PowderCoatingWizard.Module.BusinessObjects
{
    /// <summary>
    /// Represents a quality certification or standard that applies to a production line.
    /// Examples: Qualicoat Class 1/2/3, Qualanot, Qualisteal, GSB, AAMA 2604/2605.
    /// </summary>
    [DefaultClassOptions]
    [DefaultProperty(nameof(DisplayName))]
    [NavigationItem("Line Configuration")]
    [ImageName("BO_Audit")]
    [AIQueryable("Quality certification or standard (Qualicoat, GSB, AAMA etc.) applied to a production line. Includes certificate number, issuing body, issue/expiry dates, and status.")]
    public class LineCertification : BaseObject
    {
        public LineCertification(Session session) : base(session) { }

        ProductionLine line;
        CertificationStandard standard;
        string customStandardName;
        string certificateNumber;
        string issuingBody;
        DateTime issuedDate;
        DateTime expiryDate;
        CertificationStatus status;
        string notes;

        [Association("Line-Certifications")]
        [ToolTip("The production line this certification applies to.")]
        public ProductionLine Line
        {
            get => line;
            set => SetPropertyValue(nameof(Line), ref line, value);
        }

        [ToolTip("Quality standard or certification scheme.")]
        public CertificationStandard Standard
        {
            get => standard;
            set => SetPropertyValue(nameof(Standard), ref standard, value);
        }

        [Size(150)]
        [ToolTip("Custom standard name if 'Other' is selected above.")]
        public string CustomStandardName
        {
            get => customStandardName;
            set => SetPropertyValue(nameof(CustomStandardName), ref customStandardName, value);
        }

        [Size(100)]
        [ToolTip("Certificate number as issued by the certification body.")]
        public string CertificateNumber
        {
            get => certificateNumber;
            set => SetPropertyValue(nameof(CertificateNumber), ref certificateNumber, value);
        }

        [Size(150)]
        [ToolTip("Name of the certification body that issued the certificate (e.g. Qualicoat, GSB International).")]
        public string IssuingBody
        {
            get => issuingBody;
            set => SetPropertyValue(nameof(IssuingBody), ref issuingBody, value);
        }

        [ToolTip("Date the certificate was issued.")]
        public DateTime IssuedDate
        {
            get => issuedDate;
            set => SetPropertyValue(nameof(IssuedDate), ref issuedDate, value);
        }

        [ToolTip("Date the certificate expires. Alerts will be generated before this date.")]
        public DateTime ExpiryDate
        {
            get => expiryDate;
            set => SetPropertyValue(nameof(ExpiryDate), ref expiryDate, value);
        }

        [ToolTip("Current status of the certification.")]
        public CertificationStatus Status
        {
            get => status;
            set => SetPropertyValue(nameof(Status), ref status, value);
        }

        [Size(500)]
        [ToolTip("Additional notes about this certification or audit findings.")]
        public string Notes
        {
            get => notes;
            set => SetPropertyValue(nameof(Notes), ref notes, value);
        }

        [PersistentAlias("Concat([Line.Name], Concat(' – ', [Standard]))")]
        public string DisplayName =>
            $"{Line?.Name} – {(standard == CertificationStandard.Other ? customStandardName : standard.ToString())} ({Status})";

        /// <summary>
        /// Returns the number of days until certificate expiry.
        /// Negative value means already expired.
        /// </summary>
        public int DaysToExpiry => (expiryDate.Date - DateTime.Today).Days;

        public bool IsExpired => DaysToExpiry < 0;
        public bool IsExpiringSoon => DaysToExpiry >= 0 && DaysToExpiry <= 60;
    }

    public enum CertificationStandard
    {
        [Description("Qualicoat — powder coating on aluminium, standard class")]
        Qualicoat,
        [Description("Qualicoat Seaside — enhanced durability for coastal / marine environments")]
        Qualicoat_SeaSide,
        [Description("Qualanod — anodising of aluminium")]
        Qualanod,
        [Description("Qualisteel — powder coating on steel")]
        Qualisteel,
        [Description("GSB International — aluminium powder coating")]
        GSB_AL,
        [Description("GSB International — steel powder coating")]
        GSB_ST,
        [Description("AAMA 2603 — architectural coating, standard performance")]
        AAMA_2603,
        [Description("AAMA 2604 — architectural coating, high performance")]
        AAMA_2604,
        [Description("AAMA 2605 — architectural coating, superior performance")]
        AAMA_2605,
        [Description("Qualicoat Seaside Plus — highest durability category for severe marine exposure")]
        Seaside_Plus,
        [Description("ISO 12944 — corrosion protection of steel structures by protective paint systems")]
        ISO_12944,
        [Description("EN 13438 — powder coatings for hot-dip galvanised steel")]
        EN_13438,
        [Description("Other / custom standard")]
        Other
    }

    public enum CertificationStatus
    {
        [Description("Certificate is valid and current")]
        Active,
        [Description("Certificate has passed its expiry date")]
        Expired,
        [Description("Certificate is temporarily suspended")]
        Suspended,
        [Description("Audit is in progress — renewal pending")]
        InAudit,
        [Description("Applied for but not yet issued")]
        Pending
    }
}
