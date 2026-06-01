namespace PowderCoatingWizard.Module.Services.AI
{
    public sealed record RagSearchResult(
        string Text,
        string SourceType,
        string SourceTitle,
        int ChunkIndex,
        float Score)
    {
        public string Citation => $"[{SourceType}: {SourceTitle}#{ChunkIndex}]";
    }
}
