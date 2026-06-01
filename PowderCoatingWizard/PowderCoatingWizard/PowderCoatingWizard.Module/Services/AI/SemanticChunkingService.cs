using System.Text;
using System.Text.RegularExpressions;

namespace PowderCoatingWizard.Module.Services.AI
{
    /// <summary>
    /// Creates embedding-ready chunks that preserve paragraph/sentence boundaries and source metadata.
    /// </summary>
    public static partial class SemanticChunkingService
    {
        public static IReadOnlyList<string> CreateChunks(
            string text,
            string sourceLabel,
            int targetSize = 1200,
            int overlapSize = 180)
        {
            var normalized = NormalizeText(text);
            if (string.IsNullOrWhiteSpace(normalized))
                return [];

            targetSize = Math.Max(400, targetSize);
            overlapSize = Math.Clamp(overlapSize, 0, targetSize / 3);

            var units = SplitIntoSemanticUnits(normalized);
            var chunks = new List<string>();
            var buffer = new StringBuilder();

            foreach (var unit in units)
            {
                if (unit.Length > targetSize)
                {
                    FlushBuffer(chunks, buffer, sourceLabel, overlapSize);
                    foreach (var part in SplitLongUnit(unit, targetSize, overlapSize))
                        chunks.Add(AddSourceHeader(part, sourceLabel));
                    continue;
                }

                if (buffer.Length > 0 && buffer.Length + unit.Length + 2 > targetSize)
                    FlushBuffer(chunks, buffer, sourceLabel, overlapSize);

                if (buffer.Length > 0)
                    buffer.AppendLine();
                buffer.Append(unit);
            }

            FlushBuffer(chunks, buffer, sourceLabel, overlapSize, keepOverlap: false);
            return chunks;
        }

        public static string NormalizeText(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return string.Empty;

            text = text.Replace("\r\n", "\n").Replace('\r', '\n');
            text = RepeatedSpacesRegex().Replace(text, " ");
            text = ExcessBlankLinesRegex().Replace(text, "\n\n");
            return text.Trim();
        }

        private static void FlushBuffer(List<string> chunks, StringBuilder buffer, string sourceLabel, int overlapSize, bool keepOverlap = true)
        {
            var chunk = buffer.ToString().Trim();
            if (chunk.Length == 0)
                return;

            chunks.Add(AddSourceHeader(chunk, sourceLabel));

            buffer.Clear();
            if (keepOverlap && overlapSize > 0)
            {
                var overlap = BuildOverlap(chunk, overlapSize);
                if (!string.IsNullOrWhiteSpace(overlap))
                    buffer.Append(overlap);
            }
        }

        private static string AddSourceHeader(string chunk, string sourceLabel)
        {
            return string.IsNullOrWhiteSpace(sourceLabel)
                ? chunk.Trim()
                : $"Source: {sourceLabel}\n\n{chunk.Trim()}";
        }

        private static IReadOnlyList<string> SplitIntoSemanticUnits(string text)
        {
            var paragraphs = text.Split(["\n\n"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var units = new List<string>();

            foreach (var paragraph in paragraphs)
            {
                if (paragraph.Length <= 700)
                {
                    units.Add(paragraph);
                    continue;
                }

                units.AddRange(SentenceBoundaryRegex()
                    .Split(paragraph)
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Select(s => s.Trim()));
            }

            return units;
        }

        private static IEnumerable<string> SplitLongUnit(string text, int targetSize, int overlapSize)
        {
            int step = Math.Max(1, targetSize - overlapSize);
            for (int pos = 0; pos < text.Length; pos += step)
            {
                int length = Math.Min(targetSize, text.Length - pos);
                yield return text.Substring(pos, length).Trim();
                if (pos + length >= text.Length) yield break;
            }
        }

        private static string BuildOverlap(string chunk, int overlapSize)
        {
            if (chunk.Length <= overlapSize) return chunk;

            var tail = chunk[^overlapSize..];
            var sentenceStart = tail.IndexOfAny(['.', '!', '?', '\n']);
            return sentenceStart >= 0 && sentenceStart + 1 < tail.Length
                ? tail[(sentenceStart + 1)..].Trim()
                : tail.Trim();
        }

        [GeneratedRegex("[ \\t]+")]
        private static partial Regex RepeatedSpacesRegex();

        [GeneratedRegex("\\n{3,}")]
        private static partial Regex ExcessBlankLinesRegex();

        [GeneratedRegex("(?<=[.!?])\\s+")]
        private static partial Regex SentenceBoundaryRegex();
    }
}
