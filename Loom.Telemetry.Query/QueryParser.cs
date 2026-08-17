namespace Loom.Telemetry.Query;

/// <summary>Hand-written recursive-descent parser producing a QueryAst. No Type.GetType()/
/// reflection anywhere — every keyword is matched by an ordinal string comparison against
/// a fixed, known set (the Tokenizer's Keywords array).</summary>
public static class QueryParser
{
    public static QueryAst Parse(string queryText)
    {
        var source = queryText.AsSpan();
        var tokenizer = new Tokenizer(source);
        var current = tokenizer.Next();

        Expect(ref tokenizer, ref current, source, "SELECT");
        var columns = ParseSelectColumns(ref tokenizer, ref current, source);

        Expect(ref tokenizer, ref current, source, "FROM");
        Expect(ref tokenizer, ref current, source, "TELEMETRY"); // only table this phase supports

        var conditions = new List<WhereCondition>();
        if (IsKeyword(current, source, "WHERE"))
        {
            current = tokenizer.Next();
            conditions.Add(ParseCondition(ref tokenizer, ref current, source));
            while (IsKeyword(current, source, "AND"))
            {
                current = tokenizer.Next();
                conditions.Add(ParseCondition(ref tokenizer, ref current, source));
            }
        }

        string? groupBy = null;
        if (IsKeyword(current, source, "GROUP"))
        {
            current = tokenizer.Next(); Expect(ref tokenizer, ref current, source, "BY");
            groupBy = current.Slice(source).ToString();
            current = tokenizer.Next();
        }

        string? orderBy = null;
        var descending = false;
        if (IsKeyword(current, source, "ORDER"))
        {
            current = tokenizer.Next(); Expect(ref tokenizer, ref current, source, "BY");
            orderBy = current.Slice(source).ToString();
            current = tokenizer.Next();

            // Handle aggregate functions in ORDER BY (e.g., ORDER BY AVG(duration))
            if (current.Kind == TokenKind.LParen)
            {
                current = tokenizer.Next(); // column inside parens
                current = tokenizer.Next(); // consume RParen
                current = tokenizer.Next(); // move past the function
            }

            if (IsKeyword(current, source, "DESC") || IsKeyword(current, source, "ASC"))
            {
                descending = current.Slice(source).Equals("DESC", StringComparison.OrdinalIgnoreCase);
                current = tokenizer.Next();
            }
        }

        int? limit = null;
        if (IsKeyword(current, source, "LIMIT"))
        {
            current = tokenizer.Next();
            limit = int.Parse(current.Slice(source));
        }

        return new QueryAst(columns, conditions, groupBy, orderBy, descending, limit);
    }

    private static bool IsKeyword(Token token, ReadOnlySpan<char> source, string keyword) =>
        token.Kind == TokenKind.Keyword && token.Slice(source).Equals(keyword, StringComparison.OrdinalIgnoreCase);

    private static void Expect(ref Tokenizer tokenizer, ref Token current, ReadOnlySpan<char> source, string expected)
    {
        if (!IsKeyword(current, source, expected))
            throw new QuerySyntaxException($"Expected '{expected}' but found '{current.Slice(source)}' at position {current.Start}");
        current = tokenizer.Next();
    }

    private static List<SelectColumn> ParseSelectColumns(ref Tokenizer tokenizer, ref Token current, ReadOnlySpan<char> source)
    {
        var columns = new List<SelectColumn>();
        while (true)
        {
            var name = current.Slice(source).ToString();
            current = tokenizer.Next();

            if (current.Kind == TokenKind.LParen) // AVG(duration), COUNT(*), P99(duration)
            {
                current = tokenizer.Next(); // the column inside the parens
                var innerColumn = current.Slice(source).ToString();
                current = tokenizer.Next(); // consume RParen
                var aggregate = Enum.Parse<AggregateFunction>(name, ignoreCase: true);
                columns.Add(new SelectColumn(innerColumn, aggregate));
                current = tokenizer.Next();
            }
            else
            {
                columns.Add(new SelectColumn(name, AggregateFunction.None));
            }

            if (current.Kind == TokenKind.Comma) { current = tokenizer.Next(); continue; }
            break;
        }
        return columns;
    }

    private static WhereCondition ParseCondition(ref Tokenizer tokenizer, ref Token current, ReadOnlySpan<char> source)
    {
        var column = current.Slice(source).ToString();
        current = tokenizer.Next();
        var op = current.Slice(source).ToString();
        current = tokenizer.Next();
        var value = current.Slice(source).ToString();
        current = tokenizer.Next();
        return new WhereCondition(column, op, value);
    }
}
