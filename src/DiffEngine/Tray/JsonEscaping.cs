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

    static bool IsBrokenTailSurrogate(string src, int i, char c) =>
        c is
            >= '\uDC00' and
            <= '\uDFFF' &&
        (i == 0 || src[i - 1] < '\uD800' || src[i - 1] > '\uDBFF');

    static bool IsBrokenLeadSurrogate(string src, int i, char c) =>
        c is
            >= '\uD800' and
            <= '\uDBFF' &&
        (i == src.Length - 1 || src[i + 1] < '\uDC00' || src[i + 1] > '\uDFFF');

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
            switch (contents[i])
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
                    builder.Append(((int) contents[i]).ToString("x04"));
                    break;
            }

            start = i + 1;
        }

        builder.Append(contents, start, contents.Length - start);
        return builder.ToString();
    }
}