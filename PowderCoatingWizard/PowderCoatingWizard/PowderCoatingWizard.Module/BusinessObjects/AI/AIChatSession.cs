using DevExpress.ExpressApp.Model;
using DevExpress.Persistent.Base;
using DevExpress.Persistent.BaseImpl;
using DevExpress.Xpo;
using PowderCoatingWizard.Module.Attributes;
using PowderCoatingWizard.Module.BusinessObjects;
using System.ComponentModel;

namespace PowderCoatingWizard.Module.BusinessObjects.AI
{
    /// <summary>
    /// Represents a saved AI chat session. Belongs to an <see cref="ApplicationUser"/>
    /// and is private by default. Can be shared by setting <see cref="IsPublic"/> = true.
    /// </summary>
    [DefaultClassOptions]
    [NavigationItem("AI")]
    [ImageName("BO_Scheduler_Appointment")]
    [DefaultProperty(nameof(Title))]
    [AIQueryable("A saved AI assistant chat session. Has a title, owner, agent, and messages.")]
    public class AIChatSession : BaseObject
    {
        public AIChatSession(Session session) : base(session) { }

        string _title;
        ApplicationUser _owner;
        AIAgent _agent;
        bool _isPublic;
        DateTime _createdAt;
        DateTime _updatedAt;

        /// <summary>Short descriptive title for the session (e.g. first user question or user-defined name).</summary>
        [Size(500)]
        public string Title
        {
            get => _title;
            set => SetPropertyValue(nameof(Title), ref _title, value);
        }

        /// <summary>The user who created this session.</summary>
        public ApplicationUser Owner
        {
            get => _owner;
            set => SetPropertyValue(nameof(Owner), ref _owner, value);
        }

        /// <summary>Optional agent profile that was active during this session.</summary>
        public AIAgent Agent
        {
            get => _agent;
            set => SetPropertyValue(nameof(Agent), ref _agent, value);
        }

        /// <summary>When false (default) only the owner can see this session; when true it is visible to all users.</summary>
        public bool IsPublic
        {
            get => _isPublic;
            set => SetPropertyValue(nameof(IsPublic), ref _isPublic, value);
        }

        /// <summary>UTC timestamp when the session was first created.</summary>
        [ModelDefault("DisplayFormat", "{0:dd MMM yyyy  HH:mm}")]
        [ModelDefault("EditMask", "dd MMM yyyy  HH:mm")]
        public DateTime CreatedAt
        {
            get => _createdAt;
            set => SetPropertyValue(nameof(CreatedAt), ref _createdAt, value);
        }

        /// <summary>UTC timestamp of the last message saved to this session.</summary>
        [ModelDefault("DisplayFormat", "{0:dd MMM yyyy  HH:mm}")]
        [ModelDefault("EditMask", "dd MMM yyyy  HH:mm")]
        public DateTime UpdatedAt
        {
            get => _updatedAt;
            set => SetPropertyValue(nameof(UpdatedAt), ref _updatedAt, value);
        }

        /// <summary>Ordered list of messages that belong to this session.</summary>
        [Association("Session-Messages")]
        [Aggregated]
        public XPCollection<AIChatSessionMessage> Messages => GetCollection<AIChatSessionMessage>(nameof(Messages));
    }
}
