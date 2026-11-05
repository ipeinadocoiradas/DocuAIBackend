using System.Text;
using System.Text.RegularExpressions;
using DocuAI.Application.Abstractions.Services;
using Microsoft.ML.Tokenizers;

namespace DocuAI.Infrastructure.Text;

/// <summary>
/// Fragmentador semántico basado en tokens (tokenizador o200k_base).
/// Respeta los límites de párrafo y frase: agrupa frases completas hasta el tamaño
/// máximo y arrastra las últimas frases como solapamiento al fragmento siguiente.
/// Solo divide una frase cuando por sí sola supera el máximo de tokens.
/// </summary>
internal sealed partial class SemanticTextChunker : ITextChunker
{
    private static readonly Lazy<Tokenizer> SharedTokenizer =
        new(() => TiktokenTokenizer.CreateForModel("gpt-4o"), LazyThreadSafetyMode.ExecutionAndPublication);

    private static Tokenizer Tokenizer => SharedTokenizer.Value;

    public IReadOnlyList<TextChunk> Split(string text, int maxTokensPerChunk, int overlapTokens)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxTokensPerChunk, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(overlapTokens);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(overlapTokens, maxTokensPerChunk);

        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        var segments = BuildSegments(text, maxTokensPerChunk);
        var chunks = new List<TextChunk>();
        var window = new List<Segment>();
        var windowTokens = 0;
        var windowHasNewContent = false;

        foreach (var segment in segments)
        {
            if (windowTokens + segment.TokenCount > maxTokensPerChunk && windowHasNewContent)
            {
                chunks.Add(CreateChunk(chunks.Count, window));

                // Solapamiento: conserva las frases finales que quepan en overlapTokens.
                (window, windowTokens) = TakeOverlap(window, overlapTokens);
                windowHasNewContent = false;
            }

            // Garantiza que la frase entrante cabe junto al solapamiento arrastrado.
            while (window.Count > 0 && windowTokens + segment.TokenCount > maxTokensPerChunk)
            {
                windowTokens -= window[0].TokenCount;
                window.RemoveAt(0);
            }

            window.Add(segment);
            windowTokens += segment.TokenCount;
            windowHasNewContent = true;
        }

        if (windowHasNewContent)
        {
            chunks.Add(CreateChunk(chunks.Count, window));
        }

        return chunks;
    }

    /// <summary>Divide el texto en párrafos y frases, troceando las frases demasiado largas.</summary>
    private static List<Segment> BuildSegments(string text, int maxTokensPerChunk)
    {
        var segments = new List<Segment>();
        var paragraphs = ParagraphSeparatorRegex().Split(text.Replace("\r\n", "\n", StringComparison.Ordinal));

        foreach (var paragraph in paragraphs)
        {
            var sentences = SentenceSeparatorRegex()
                .Split(paragraph.Trim())
                .Where(sentence => !string.IsNullOrWhiteSpace(sentence))
                .ToArray();

            for (var i = 0; i < sentences.Length; i++)
            {
                var endsParagraph = i == sentences.Length - 1;
                var sentence = sentences[i].Trim();
                var tokenCount = Tokenizer.CountTokens(sentence);

                if (tokenCount <= maxTokensPerChunk)
                {
                    segments.Add(new Segment(sentence, tokenCount, endsParagraph));
                    continue;
                }

                segments.AddRange(SplitOversizedSentence(sentence, maxTokensPerChunk, endsParagraph));
            }
        }

        return segments;
    }

    /// <summary>Trocea por tokens una frase que excede el máximo (p. ej. tablas o listados sin puntuación).</summary>
    private static IEnumerable<Segment> SplitOversizedSentence(string sentence, int maxTokensPerChunk, bool endsParagraph)
    {
        var remaining = sentence;

        while (remaining.Length > 0)
        {
            var cutIndex = Tokenizer.GetIndexByTokenCount(remaining, maxTokensPerChunk, out _, out var tokenCount);

            // Salvaguarda ante índices degenerados para garantizar el avance.
            if (cutIndex <= 0)
            {
                cutIndex = Math.Min(remaining.Length, maxTokensPerChunk);
                tokenCount = Tokenizer.CountTokens(remaining[..cutIndex]);
            }

            var piece = remaining[..cutIndex].Trim();
            remaining = remaining[cutIndex..];

            if (piece.Length > 0)
            {
                yield return new Segment(piece, tokenCount, endsParagraph && remaining.Trim().Length == 0);
            }
        }
    }

    private static (List<Segment> Window, int Tokens) TakeOverlap(List<Segment> window, int overlapTokens)
    {
        var overlap = new List<Segment>();
        var tokens = 0;

        for (var i = window.Count - 1; i >= 0; i--)
        {
            if (tokens + window[i].TokenCount > overlapTokens)
            {
                break;
            }

            overlap.Insert(0, window[i]);
            tokens += window[i].TokenCount;
        }

        return (overlap, tokens);
    }

    private static TextChunk CreateChunk(int index, List<Segment> segments)
    {
        var builder = new StringBuilder();

        foreach (var segment in segments)
        {
            builder.Append(segment.Text).Append(segment.EndsParagraph ? "\n\n" : " ");
        }

        var text = builder.ToString().Trim();
        return new TextChunk(index, text, Tokenizer.CountTokens(text));
    }

    [GeneratedRegex(@"\n\s*\n")]
    private static partial Regex ParagraphSeparatorRegex();

    // Separa tras signos de cierre de frase seguidos de espacio o salto de línea simple.
    [GeneratedRegex(@"(?<=[\.\!\?…;])\s+|\n")]
    private static partial Regex SentenceSeparatorRegex();

    private readonly record struct Segment(string Text, int TokenCount, bool EndsParagraph);
}
