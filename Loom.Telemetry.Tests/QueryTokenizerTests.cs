using System;
using Xunit;
using Loom.Telemetry.Query;

namespace Loom.Telemetry.Tests;

public class QueryTokenizerTests
{
    [Fact]
    public void Tokenizer_ParsesKeywords()
    {
        var source = "SELECT FROM WHERE".AsSpan();
        var tokenizer = new Tokenizer(source);

        var token1 = tokenizer.Next();
        Assert.Equal(TokenKind.Keyword, token1.Kind);
        Assert.Equal("SELECT", token1.Slice(source).ToString());

        var token2 = tokenizer.Next();
        Assert.Equal(TokenKind.Keyword, token2.Kind);
        Assert.Equal("FROM", token2.Slice(source).ToString());

        var token3 = tokenizer.Next();
        Assert.Equal(TokenKind.Keyword, token3.Kind);
        Assert.Equal("WHERE", token3.Slice(source).ToString());

        var end = tokenizer.Next();
        Assert.Equal(TokenKind.End, end.Kind);
    }

    [Fact]
    public void Tokenizer_ParsesIdentifiers()
    {
        var source = "method duration value123".AsSpan();
        var tokenizer = new Tokenizer(source);

        var token1 = tokenizer.Next();
        Assert.Equal(TokenKind.Identifier, token1.Kind);
        Assert.Equal("method", token1.Slice(source).ToString());

        var token2 = tokenizer.Next();
        Assert.Equal(TokenKind.Identifier, token2.Kind);
        Assert.Equal("duration", token2.Slice(source).ToString());

        var token3 = tokenizer.Next();
        Assert.Equal(TokenKind.Identifier, token3.Kind);
        Assert.Equal("value123", token3.Slice(source).ToString());
    }

    [Fact]
    public void Tokenizer_ParsesNumbers()
    {
        var source = "123 45.67 0.99".AsSpan();
        var tokenizer = new Tokenizer(source);

        var token1 = tokenizer.Next();
        Assert.Equal(TokenKind.Number, token1.Kind);
        Assert.Equal("123", token1.Slice(source).ToString());

        var token2 = tokenizer.Next();
        Assert.Equal(TokenKind.Number, token2.Kind);
        Assert.Equal("45.67", token2.Slice(source).ToString());

        var token3 = tokenizer.Next();
        Assert.Equal(TokenKind.Number, token3.Kind);
        Assert.Equal("0.99", token3.Slice(source).ToString());
    }

    [Fact]
    public void Tokenizer_ParsesOperators()
    {
        var source = "= > < >= <=".AsSpan();
        var tokenizer = new Tokenizer(source);

        var token1 = tokenizer.Next();
        Assert.Equal(TokenKind.Operator, token1.Kind);
        Assert.Equal("=", token1.Slice(source).ToString());

        var token2 = tokenizer.Next();
        Assert.Equal(TokenKind.Operator, token2.Kind);
        Assert.Equal(">", token2.Slice(source).ToString());

        var token3 = tokenizer.Next();
        Assert.Equal(TokenKind.Operator, token3.Kind);
        Assert.Equal("<", token3.Slice(source).ToString());

        var token4 = tokenizer.Next();
        Assert.Equal(TokenKind.Operator, token4.Kind);
        Assert.Equal(">=", token4.Slice(source).ToString());

        var token5 = tokenizer.Next();
        Assert.Equal(TokenKind.Operator, token5.Kind);
        Assert.Equal("<=", token5.Slice(source).ToString());
    }

    [Fact]
    public void Tokenizer_ParsesStringLiterals()
    {
        var source = "'hello world' 'test'".AsSpan();
        var tokenizer = new Tokenizer(source);

        var token1 = tokenizer.Next();
        Assert.Equal(TokenKind.StringLiteral, token1.Kind);
        Assert.Equal("hello world", token1.Slice(source).ToString());

        var token2 = tokenizer.Next();
        Assert.Equal(TokenKind.StringLiteral, token2.Kind);
        Assert.Equal("test", token2.Slice(source).ToString());
    }

    [Fact]
    public void Tokenizer_ParsesSpecialCharacters()
    {
        var source = "( ) ,".AsSpan();
        var tokenizer = new Tokenizer(source);

        Assert.Equal(TokenKind.LParen, tokenizer.Next().Kind);
        Assert.Equal(TokenKind.RParen, tokenizer.Next().Kind);
        Assert.Equal(TokenKind.Comma, tokenizer.Next().Kind);
    }

    [Fact]
    public void Tokenizer_SkipsWhitespace()
    {
        var source = "   SELECT    FROM   ".AsSpan();
        var tokenizer = new Tokenizer(source);

        var token1 = tokenizer.Next();
        Assert.Equal(TokenKind.Keyword, token1.Kind);
        Assert.Equal("SELECT", token1.Slice(source).ToString());

        var token2 = tokenizer.Next();
        Assert.Equal(TokenKind.Keyword, token2.Kind);
        Assert.Equal("FROM", token2.Slice(source).ToString());
    }

    [Fact]
    public void Tokenizer_ThrowsOnUnexpectedCharacter()
    {
        // Can't use lambda with ref struct, so test directly
        var exception = Record.Exception(() =>
        {
            var source = "@invalid".AsSpan();
            var t = new Tokenizer(source);
            t.Next();
        });

        Assert.NotNull(exception);
        Assert.IsType<QuerySyntaxException>(exception);
    }

    [Fact]
    public void Tokenizer_HandlesCaseInsensitiveKeywords()
    {
        var source = "select SeLeCt SELECT".AsSpan();
        var tokenizer = new Tokenizer(source);

        Assert.Equal(TokenKind.Keyword, tokenizer.Next().Kind);
        Assert.Equal(TokenKind.Keyword, tokenizer.Next().Kind);
        Assert.Equal(TokenKind.Keyword, tokenizer.Next().Kind);
    }
}
