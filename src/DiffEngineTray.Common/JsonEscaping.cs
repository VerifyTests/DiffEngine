using System.Text;

static class JsonEscaping
{
    static bool NeedEscape(string src, int i)
    {
        var c = src[i];
        return c < 32 || c == '"' || c == '\\'

               ||
               // Broken lead surrogate
               c >= '\uD800' &&
               c <= '\uDBFF' &&
               (i == src.Length - 1 || src[i + 1] < '\uDC00' || src[i + 1] > '\uDFFF')
               ||
               // Broken tail surrogate
               c >= '\uDC00' &&
               c <= '\uDFFF' &&
               (i == 0 || src[i - 1] < '\uD800' || src[i - 1] > '\uDBFF')
               ||
               // To produce valid JavaScript
               c == '\u2028' ||
               c == '\u2029'
               ||
               // Escape "</" for <script> tags
               c == '/' && i > 0 &&
               src[i - 1] == '<';
    }

    public static string JsonEscape(this string contents)
    {
        StringBuilder builder = new StringBuilder();

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