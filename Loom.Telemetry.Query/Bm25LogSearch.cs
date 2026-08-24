using Loom.Telemetry;
using Loom.Web.Contracts.Dtos;

namespace Loom.Telemetry.Query;

/// <summary>
/// BM25 lexical search over LogRecord.Message. Human-triggered path (log search),
/// not a hot path - same standing as ILogStore.Query. Correctness over allocation
/// tricks; do not push tokenization into the write path.
/// </summary>
public static class Bm25LogSearch
{
    private const double K1 = 1.2;
    private const double B = 0.75;

    public static SearchResult[] Search(LogRecord[] corpus, string query, int maxResults, double minScore)
    {
        if (string.IsNullOrWhiteSpace(query) || corpus.Length == 0)
            return Array.Empty<SearchResult>();

        var queryTerms = Tokenize(query);
        if (queryTerms.Length == 0)
            return Array.Empty<SearchResult>();

        var docTermFrequencies = new Dictionary<string, int>[corpus.Length];
        var docLengths = new int[corpus.Length];
        var termDocFrequency = new Dictionary<string, int>();

        for (var i = 0; i < corpus.Length; i++)
        {
            var terms = Tokenize(corpus[i].Message);
            docLengths[i] = terms.Length;

            var frequencies = new Dictionary<string, int>();
            foreach (var term in terms)
            {
                frequencies.TryGetValue(term, out var count);
                frequencies[term] = count + 1;
            }
            docTermFrequencies[i] = frequencies;

            foreach (var term in frequencies.Keys)
            {
                termDocFrequency.TryGetValue(term, out var count);
                termDocFrequency[term] = count + 1;
            }
        }

        var n = corpus.Length;
        var avgDocLength = docLengths.Average();

        var idf = new Dictionary<string, double>();
        foreach (var term in queryTerms.Distinct())
        {
            termDocFrequency.TryGetValue(term, out var nq);
            idf[term] = Math.Log(1.0 + (n - nq + 0.5) / (nq + 0.5));
        }

        var scored = new List<(int Index, double Score)>(corpus.Length);
        for (var i = 0; i < corpus.Length; i++)
        {
            if (docLengths[i] == 0)
                continue;

            var frequencies = docTermFrequencies[i];
            double score = 0;
            foreach (var term in queryTerms.Distinct())
            {
                if (!frequencies.TryGetValue(term, out var termFrequency))
                    continue;

                var numerator = termFrequency * (K1 + 1);
                var denominator = termFrequency + K1 * (1 - B + B * (docLengths[i] / avgDocLength));
                score += idf[term] * (numerator / denominator);
            }

            if (score > 0 && score >= minScore)
                scored.Add((i, score));
        }

        return scored
            .OrderByDescending(s => s.Score)
            .Take(maxResults)
            .Select(s => new SearchResult
            {
                Content = corpus[s.Index].Message,
                Score = s.Score,
                Timestamp = corpus[s.Index].TimestampUtc,
                Source = corpus[s.Index].Category
            })
            .ToArray();
    }

    private static string[] Tokenize(string text)
    {
        var tokens = new List<string>();
        var start = -1;
        for (var i = 0; i < text.Length; i++)
        {
            if (char.IsLetterOrDigit(text[i]))
            {
                if (start < 0) start = i;
            }
            else if (start >= 0)
            {
                tokens.Add(text[start..i].ToLowerInvariant());
                start = -1;
            }
        }
        if (start >= 0)
            tokens.Add(text[start..].ToLowerInvariant());

        return tokens.ToArray();
    }
}
