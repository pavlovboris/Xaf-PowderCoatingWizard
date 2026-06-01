using DevExpress.ExpressApp;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using PowderCoatingWizard.Module.BusinessObjects.AI;
using System.Text;
using UglyToad.PdfPig;

namespace PowderCoatingWizard.Module.Services.AI
{
    /// <summary>
    /// Splits <see cref="AIDocument"/> files into text chunks, generates embeddings
    /// and persists them as <see cref="KnowledgeChunk"/> rows (xafrag-style pipeline,
    /// DSERPEvo-style XPO storage).
    /// </summary>
    public class DocumentIngestionService
    {
        private const int ChunkSize = 1200;   // target characters per semantic chunk
        private const int ChunkOverlap = 180; // overlap between consecutive chunks

        private readonly IObjectSpaceFactory _osFactory;
        private readonly EmbeddingService _embedding;

        public DocumentIngestionService(IObjectSpaceFactory osFactory, EmbeddingService embedding)
        {
            _osFactory = osFactory;
            _embedding = embedding;
        }

        /// <summary>
        /// Ingests all un-synced <see cref="AIDocument"/> records.
        /// Returns the number of chunks written.
        /// </summary>
        public async Task<int> IngestPendingAsync(CancellationToken ct = default)
        {
            int total = 0;
            using var os = _osFactory.CreateObjectSpace(typeof(AIDocument));

            var pending = os.GetObjects<AIDocument>()
                            .Where(d => !d.IsSynced && d.File != null && !d.File.IsEmpty)
                            .ToList();

            foreach (var doc in pending)
            {
                total += await IngestDocumentAsync(os, doc, ct);
                doc.IsSynced = true;
            }

            os.CommitChanges();
            return total;
        }

        /// <summary>Re-ingests a single document (drops existing chunks first).</summary>
        public async Task<int> ReIngestAsync(Guid documentOid, CancellationToken ct = default)
        {
            using var os = _osFactory.CreateObjectSpace(typeof(AIDocument));
            var doc = os.GetObjectByKey<AIDocument>(documentOid);
            if (doc == null) return 0;

            // Delete old chunks
            foreach (var old in doc.KnowledgeChunks.ToList())
                os.Delete(old);

            doc.IsSynced = false;
            int total = await IngestDocumentAsync(os, doc, ct);
            doc.IsSynced = true;
            os.CommitChanges();
            return total;
        }

        // ── internals ──────────────────────────────────────────────────────────

        private async Task<int> IngestDocumentAsync(IObjectSpace os, AIDocument doc, CancellationToken ct)
        {
            string text = ExtractText(doc);
            if (string.IsNullOrWhiteSpace(text)) return 0;

            var sourceLabel = BuildSourceLabel(doc);
            var chunks = SemanticChunkingService.CreateChunks(text, sourceLabel, ChunkSize, ChunkOverlap).ToList();

            // Generate all embeddings in one batch when possible
            float[][]? vectors = _embedding.IsAvailable
                ? await _embedding.EmbedBatchAsync(chunks, ct)
                : null;

            for (int i = 0; i < chunks.Count; i++)
            {
                var chunk = os.CreateObject<KnowledgeChunk>();
                chunk.Document = doc;
                chunk.ChunkIndex = i;
                chunk.ChunkText = chunks[i];
                chunk.CreatedAt = DateTime.UtcNow;

                if (vectors != null && i < vectors.Length)
                    chunk.SetEmbedding(vectors[i]);
            }

            return chunks.Count;
        }

        private static string ExtractText(AIDocument doc)
        {
            if (doc.File == null || doc.File.IsEmpty) return string.Empty;

            var ms = new MemoryStream();
            doc.File.SaveToStream(ms);
            byte[] bytes = ms.ToArray();
            string fileName = doc.File.FileName ?? string.Empty;
            string ext = Path.GetExtension(fileName).ToLowerInvariant();

            return ext switch
            {
                ".pdf"  => ExtractPdf(bytes),
                ".docx" => ExtractDocx(bytes),
                ".xlsx" => ExtractXlsx(bytes),
                ".txt" or ".md" or ".csv" => Encoding.UTF8.GetString(bytes),
                _ => TryUtf8(bytes)
            };
        }

        private static string ExtractPdf(byte[] bytes)
        {
            var sb = new StringBuilder();
            using var pdf = PdfDocument.Open(bytes);
            foreach (var page in pdf.GetPages())
                sb.AppendLine(page.Text);
            return sb.ToString();
        }

        private static string ExtractDocx(byte[] bytes)
        {
            using var ms = new MemoryStream(bytes);
            using var doc = WordprocessingDocument.Open(ms, false);
            var body = doc.MainDocumentPart?.Document?.Body;
            return body?.InnerText ?? string.Empty;
        }

        private static string ExtractXlsx(byte[] bytes)
        {
            var sb = new StringBuilder();
            using var ms = new MemoryStream(bytes);
            using var doc = SpreadsheetDocument.Open(ms, false);
            var workbook = doc.WorkbookPart;
            if (workbook == null) return string.Empty;

            var sharedStrings = workbook.SharedStringTablePart?.SharedStringTable;
            foreach (var sheetPart in workbook.WorksheetParts)
            {
                foreach (var row in sheetPart.Worksheet.Descendants<Row>())
                {
                    var cells = row.Descendants<Cell>().Select(c =>
                    {
                        if (c.DataType?.Value == CellValues.SharedString && sharedStrings != null)
                            return sharedStrings.ElementAt(int.Parse(c.InnerText)).InnerText;
                        return c.InnerText;
                    });
                    sb.AppendLine(string.Join("\t", cells));
                }
            }
            return sb.ToString();
        }

        private static string TryUtf8(byte[] bytes)
        {
            try { return Encoding.UTF8.GetString(bytes); }
            catch { return string.Empty; }
        }

        private static string BuildSourceLabel(AIDocument doc)
        {
            var title = !string.IsNullOrWhiteSpace(doc.Title)
                ? doc.Title.Trim()
                : doc.File?.FileName ?? "Document";

            return !string.IsNullOrWhiteSpace(doc.Description)
                ? $"Document: {title}. Description: {doc.Description.Trim()}"
                : $"Document: {title}";
        }
    }
}
