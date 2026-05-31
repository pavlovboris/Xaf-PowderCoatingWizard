namespace PowderCoatingWizard.Module.Attributes
{
    /// <summary>
    /// Marks a persistent business-object class as accessible to the AI assistant
    /// via the <c>list_entities</c>, <c>describe_entity</c>, and <c>query_entity</c> tools.
    /// Only classes decorated with this attribute are discoverable — all others are hidden.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class AIQueryableAttribute : Attribute
    {
        /// <summary>
        /// Optional human-readable description passed to the model in the schema prompt.
        /// </summary>
        public string Description { get; }

        public AIQueryableAttribute(string description = "")
        {
            Description = description;
        }
    }
}
