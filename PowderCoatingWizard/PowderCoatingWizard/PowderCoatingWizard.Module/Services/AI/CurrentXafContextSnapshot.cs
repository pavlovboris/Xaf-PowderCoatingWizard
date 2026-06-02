namespace PowderCoatingWizard.Module.Services.AI
{
    /// <summary>
    /// Immutable snapshot of the originating XAF view context captured before the custom AI chat window is opened.
    /// </summary>
    public sealed class CurrentXafContextSnapshot
    {
        public string ViewId { get; init; } = string.Empty;
        public string ViewType { get; init; } = string.Empty;
        public string ObjectTypeName { get; init; } = string.Empty;
        public string ObjectTypeFullName { get; init; } = string.Empty;
        public CurrentXafObjectSnapshot? CurrentObject { get; init; }
        public IReadOnlyList<CurrentXafObjectSnapshot> SelectedObjects { get; init; } = [];
        public int SelectedObjectCount { get; init; }
        public bool IsListView { get; init; }
        public bool IsDetailView { get; init; }

        public static CurrentXafContextSnapshot Empty { get; } = new();
    }

    public sealed class CurrentXafObjectSnapshot
    {
        public string EntityName { get; init; } = string.Empty;
        public string EntityFullName { get; init; } = string.Empty;
        public string Key { get; init; } = string.Empty;
        public string DisplayText { get; init; } = string.Empty;
    }
}
