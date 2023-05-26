static class JsonEscaping
{
    static bool NeedEscape(string src, int i)
    {
        var c = src[i];
        return c < 32 || c is '"' or '\\'
                      ||
                      IsBrokenLeadSurrogate(src, i, c)
                      ||
                      IsBrokenTailSurrogate(src, i, c)
                      ||
                      IsValidJson(c)
                      ||
                      IsStartOfScriptTag(src, i, c);
    }

    static bool IsValidJson(char c) =>
        c is
            '\u2028' or
            '\u2029';

    static bool IsStartOfScriptTag(string src, int i, char c) =>
        // Escape "</" for <script> tags
        c == '/' &&
        i > 0 &&
        src[i - 1] == '<';

    static bool IsBrokenTailSurrogate(string src, int i, char c)
    {
        if (c is
            < '\uDC00' or
            > '\uDFFF')
        {
            return false;
        }

        if (i == 0)
        {
            return true;
        }

        var l = src[i - 1];
        return l is < '\uD800' or > '\uDBFF';
    }

    static bool IsBrokenLeadSurrogate(string src, int i, char c)
    {
        if (c is
            < '\uD800' or
            > '\uDBFF')
        {
            return false;
        }

        if (i == src.Length - 1)
        {
            return true;
        }

        var l = src[i + 1];
        return l is < '\uDC00' or > '\uDFFF';
    }

    public static string JsonEscape(this string contents)
    {
        var builder = new StringBuilder();

        var start = 0;
        for (var i = 0; i < contents.Length; i++)
        {
            if (!NeedEscape(contents, i))
            {
                continue;
            }

            builder.Append(contents, start, i - start);
            var content = contents[i];
            switch (content)
            {
                case '\b':
                    builder.Append("\\b");
                    break;
                case '\f':
                    builder.Append("\\f");
                    break;
                case '\n':
                    builder.Append("\\n");
                    break;
                case '\r':
                    builder.Append("\\r");
                    break;
                case '\t':
                    builder.Append("\\t");
                    break;
                case '\"':
                    builder.Append("\\\"");
                    break;
                case '\\':
                    builder.Append("\\\\");
                    break;
                case '/':
                    builder.Append("\\/");
                    break;
                default:
                    builder.Append("\\u");
                    builder.Append(((int) content).ToString("x04"));
                    break;
            }

            start = i + 1;
        }

        builder.Append(contents, start, contents.Length - start);
        return builder.ToString();
    }
}