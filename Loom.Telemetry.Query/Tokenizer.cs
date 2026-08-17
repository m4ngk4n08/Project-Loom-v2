namespace Loom.Telemetry.Query;

public enum TokenKind { Keyword, Identifier, Operator, Number, StringLiteral, Comma, LParen, RParen, End }

/// <summary>A token as a struct — no per-token heap allocation. Text is a slice
/// (start/length) into the original query string, not a copied substring.</summary>
public readonly struct Token(TokenKind kind, int start, int length)
{
    public TokenKind Kind { get; } = kind;
    public int Start { get; } = start;
    public int Length { get; } = length;

    public ReadOnlySpan<char> Slice(ReadOnlySpan<char> source) => source.Slice(Start, Length);
}

/// <summary>Lexes over ReadOnlySpan&lt;char&gt; — yields Token structs (no string allocation
/// for keywords/identifiers; the caller slices the original span when it needs text, e.g.
/// for identifier names that become AST leaf values in Step 10.4).</summary>
public ref struct Tokenizer(ReadOnlySpan<char> source)
{
    private readonly ReadOnlySpan<char> _source = source;
    private int _position;

    public Token Next()
    {
        SkipWhitespace();
        if (_position >= _source.Length) return new Token(TokenKind.End, _position, 0);

        var c = _source[_position];
        if (c == ',') return Single(TokenKind.Comma);
        if (c == '(') return Single(TokenKind.LParen);
        if (c == ')') return Single(TokenKind.RParen);
        if (c is '=' or '>' or '<') return ReadOperator();
        if (char.IsDigit(c)) return ReadNumber();
        if (c == '\'') return ReadStringLiteral();
        if (char.IsLetter(c) || c == '_' || c == '*') return ReadIdentifierOrKeyword();

        throw new QuerySyntaxException($"Unexpected character '{c}' at position {_position}");
    }

    private void SkipWhitespace() { while (_position < _source.Length && char.IsWhiteSpace(_source[_position])) _position++; }

    private Token Single(TokenKind kind) => new(kind, _position++, 1);

    private Token ReadOperator()
    {
        var start = _position;
        _position++;
        if (_position < _source.Length && _source[_position] == '=') _position++; // >=, <=
        return new Token(TokenKind.Operator, start, _position - start);
    }

    private Token ReadNumber()
    {
        var start = _position;
        while (_position < _source.Length && (char.IsDigit(_source[_position]) || _source[_position] == '.')) _position++;
        return new Token(TokenKind.Number, start, _position - start);
    }

    private Token ReadStringLiteral()
    {
        var start = ++_position; // skip opening quote
        while (_position < _source.Length && _source[_position] != '\'') _position++;
        var token = new Token(TokenKind.StringLiteral, start, _position - start);
        _position++; // skip closing quote
        return token;
    }

    private static readonly string[] Keywords =
        ["SELECT", "FROM", "WHERE", "AND", "GROUP", "BY", "ORDER", "DESC", "ASC", "LIMIT", "TELEMETRY"];

    private Token ReadIdentifierOrKeyword()
    {
        var start = _position;
        while (_position < _source.Length && (char.IsLetterOrDigit(_source[_position]) || _source[_position] is '_' or '*' or '.')) _position++;

        var length = _position - start;
        var isKeyword = false;

        // Check if text matches any keyword (can't use LINQ with ReadOnlySpan)
        for (int i = 0; i < Keywords.Length; i++)
        {
            if (length == Keywords[i].Length && _source.Slice(start, length).Equals(Keywords[i], StringComparison.OrdinalIgnoreCase))
            {
                isKeyword = true;
                break;
            }
        }

        return new Token(isKeyword ? TokenKind.Keyword : TokenKind.Identifier, start, length);
    }
}

public sealed class QuerySyntaxException(string message) : Exception(message);
