using DevExpress.ExpressApp.Model;
using DevExpress.Persistent.Base;
using DevExpress.Persistent.BaseImpl;
using DevExpress.Xpo;
using PowderCoatingWizard.Module.Attributes;

namespace PowderCoatingWizard.Module.BusinessObjects.AI
{
    /// <summary>
    /// A single message (user, assistant, or system) belonging to an <see cref="AIChatSession"/>.
    /// </summary>
    [AIQueryable("A single message in an AI chat session. Has role (user/assistant/system), content, and timestamp.")]
    public class AIChatSessionMessage : BaseObject
    {
        public AIChatSessionMessage(Session session) : base(session) { }

        AIChatSession _chatSession;
        int _sortOrder;
        string _role;
        string _content;
        DateTime _sentAt;

        /// <summary>Parent chat session this message belongs to.</summary>
        [Association("Session-Messages")]
        public AIChatSession ChatSession
        {
            get => _chatSession;
            set => SetPropertyValue(nameof(ChatSession), ref _chatSession, value);
        }

        /// <summary>Zero-based order index used to reconstruct message order on restore.</summary>
        public int SortOrder
        {
            get => _sortOrder;
            set => SetPropertyValue(nameof(SortOrder), ref _sortOrder, value);
        }

        /// <summary>Message role: "user", "assistant", or "system".</summary>
        [Size(50)]
        public string Role
        {
            get => _role;
            set => SetPropertyValue(nameof(Role), ref _role, value);
        }

        /// <summary>Full text content of the message (may contain Markdown).</summary>
        [Size(SizeAttribute.Unlimited)]
        public string Content
        {
            get => _content;
            set => SetPropertyValue(nameof(Content), ref _content, value);
        }

        /// <summary>UTC time the message was produced.</summary>
        [ModelDefault("DisplayFormat", "{0:dd MMM yyyy  HH:mm}")]
        [ModelDefault("EditMask", "dd MMM yyyy  HH:mm")]
        public DateTime SentAt
        {
            get => _sentAt;
            set => SetPropertyValue(nameof(SentAt), ref _sentAt, value);
        }
    }
}
