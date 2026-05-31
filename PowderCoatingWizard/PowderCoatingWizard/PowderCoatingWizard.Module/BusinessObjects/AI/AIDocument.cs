using DevExpress.Persistent.Base;
using DevExpress.Persistent.BaseImpl;
using DevExpress.Xpo;
using System.ComponentModel;

namespace PowderCoatingWizard.Module.BusinessObjects.AI
{
    [DefaultClassOptions]
    [NavigationItem("AI")]
    [ImageName("Actions_OpenFile")]
    [DefaultProperty(nameof(Title))]
    public class AIDocument : BaseObject
    {
        public AIDocument(Session session) : base(session) { }

        string _title;
        string _description;
        FileData _file;
        string _assistantFileId;
        DateTime _uploadedAt;
        bool _isSynced;

        [Size(500)]
        public string Title
        {
            get => _title;
            set => SetPropertyValue(nameof(Title), ref _title, value);
        }

        [Size(2000)]
        public string Description
        {
            get => _description;
            set => SetPropertyValue(nameof(Description), ref _description, value);
        }

        [Aggregated]
        [ExpandObjectMembers(ExpandObjectMembers.Never)]
        public FileData File
        {
            get => _file;
            set => SetPropertyValue(nameof(File), ref _file, value);
        }

        /// <summary>OpenAI Assistants file ID after upload (e.g. "file-abc123...").</summary>
        [Size(200)]
        [Browsable(false)]
        public string AssistantFileId
        {
            get => _assistantFileId;
            set => SetPropertyValue(nameof(AssistantFileId), ref _assistantFileId, value);
        }

        public DateTime UploadedAt
        {
            get => _uploadedAt;
            set => SetPropertyValue(nameof(UploadedAt), ref _uploadedAt, value);
        }

        /// <summary>True when the file has been ingested into KnowledgeChunks.</summary>
        public bool IsSynced
        {
            get => _isSynced;
            set => SetPropertyValue(nameof(IsSynced), ref _isSynced, value);
        }

        [Association("AIDocument-KnowledgeChunks")]
        [Aggregated]
        public XPCollection<KnowledgeChunk> KnowledgeChunks => GetCollection<KnowledgeChunk>(nameof(KnowledgeChunks));

        /// <summary>Agents that have explicit access to this document. Managed from the AIAgent side.</summary>
        [Association("AIAgent-Documents")]
        [Browsable(false)]
        public XPCollection<AIAgent> Agents => GetCollection<AIAgent>(nameof(Agents));
    }
}
