using DevExpress.Persistent.Base;
using DevExpress.Persistent.BaseImpl;
using DevExpress.Xpo;
using PowderCoatingWizard.Module.BusinessObjects;
using System.ComponentModel;

namespace PowderCoatingWizard.Module.BusinessObjects.AI
{
    /// <summary>Classifies what a media file depicts.</summary>
    public enum AIMediaCategory
    {
        ProductionPhoto = 0,
        DefectPhoto     = 1,
        EquipmentPhoto  = 2,
        ProcessDiagram  = 3,
        LabResult       = 4,
        Other           = 99
    }

    /// <summary>
    /// Stores a categorised image or document that can be attached to an AI chat session
    /// or included as visual context when querying the AI assistant.
    /// </summary>
    [DefaultClassOptions]
    [NavigationItem("AI")]
    [ImageName("BO_FileAttachment")]
    [DefaultProperty(nameof(Title))]
    public class AIMedia : BaseObject
    {
        public AIMedia(Session session) : base(session) { }

        string _title;
        AIMediaCategory _category;
        string _tags;
        LineStage _stage;
        string _description;
        string _notes;
        FileData _file;
        DateTime _capturedAt;
        DateTime _createdAt;

        [Size(500)]
        public string Title
        {
            get => _title;
            set => SetPropertyValue(nameof(Title), ref _title, value);
        }

        public AIMediaCategory Category
        {
            get => _category;
            set => SetPropertyValue(nameof(Category), ref _category, value);
        }

        /// <summary>Comma-separated tags for filtering (e.g. "zinc,phosphate,bath-3").</summary>
        [Size(1000)]
        public string Tags
        {
            get => _tags;
            set => SetPropertyValue(nameof(Tags), ref _tags, value);
        }

        /// <summary>Optional link to the production stage this image belongs to.</summary>
        public LineStage Stage
        {
            get => _stage;
            set => SetPropertyValue(nameof(Stage), ref _stage, value);
        }

        /// <summary>What is shown in this image / what happened.</summary>
        [Size(SizeAttribute.Unlimited)]
        public string Description
        {
            get => _description;
            set => SetPropertyValue(nameof(Description), ref _description, value);
        }

        /// <summary>Additional operator notes or observations.</summary>
        [Size(SizeAttribute.Unlimited)]
        public string Notes
        {
            get => _notes;
            set => SetPropertyValue(nameof(Notes), ref _notes, value);
        }

        /// <summary>The actual image / document file stored in the database.</summary>
        [Aggregated]
        [ExpandObjectMembers(ExpandObjectMembers.Never)]
        public FileData File
        {
            get => _file;
            set => SetPropertyValue(nameof(File), ref _file, value);
        }

        /// <summary>When the photo was taken / document was created (user-provided).</summary>
        public DateTime CapturedAt
        {
            get => _capturedAt;
            set => SetPropertyValue(nameof(CapturedAt), ref _capturedAt, value);
        }

        /// <summary>When this record was added to the system (auto-set on create).</summary>
        public DateTime CreatedAt
        {
            get => _createdAt;
            set => SetPropertyValue(nameof(CreatedAt), ref _createdAt, value);
        }

        protected override void OnSaving()
        {
            base.OnSaving();
            if (Session.IsNewObject(this) && _createdAt == default)
                CreatedAt = DateTime.UtcNow;
        }

        // ── Helpers ─────────────────────────────────────────────────────────

        /// <summary>
        /// Returns the MIME media type inferred from the file extension.
        /// Falls back to "application/octet-stream".
        /// </summary>
        public string GetMediaType()
        {
            var ext = System.IO.Path.GetExtension(File?.FileName ?? string.Empty)
                           .ToLowerInvariant();
            return ext switch
            {
                ".jpg" or ".jpeg" => "image/jpeg",
                ".png"            => "image/png",
                ".gif"            => "image/gif",
                ".webp"           => "image/webp",
                ".bmp"            => "image/bmp",
                ".tiff" or ".tif" => "image/tiff",
                ".pdf"            => "application/pdf",
                _                 => "application/octet-stream"
            };
        }

        /// <summary>
        /// Reads the file content and returns it as a Base64-encoded string,
        /// or <c>null</c> if no file is attached.
        /// </summary>
        public string? GetBase64()
        {
            if (File?.Content == null || File.Content.Length == 0)
                return null;
            return Convert.ToBase64String(File.Content);
        }

        /// <summary>
        /// Returns a data-URI string suitable for embedding in an AI message
        /// (e.g. "data:image/jpeg;base64,/9j/4AAQ…").
        /// Returns <c>null</c> if no file is attached.
        /// </summary>
        public string? GetDataUri()
        {
            var b64 = GetBase64();
            if (b64 == null) return null;
            return $"data:{GetMediaType()};base64,{b64}";
        }
    }
}
