using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ReentrySync.Core
{
    public static class JsonHelper
    {
        public static Dictionary<string, string> ParseObject(string json)
        {
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(json)) return dict;

            int i = 0;
            SkipWhitespace(json, ref i);
            if (i >= json.Length || json[i] != '{') return dict;
            i++; // skip '{'

            while (i < json.Length)
            {
                SkipWhitespace(json, ref i);
                if (i >= json.Length || json[i] == '}') break;

                // Read Key
                string key = ReadString(json, ref i);
                SkipWhitespace(json, ref i);

                if (i >= json.Length || json[i] != ':') break;
                i++; // skip ':'
                SkipWhitespace(json, ref i);

                // Read Value (string, number, boolean, or nested)
                string val = ReadValue(json, ref i);
                dict[key] = val;

                SkipWhitespace(json, ref i);
                if (i < json.Length && json[i] == ',')
                {
                    i++; // skip ','
                }
            }

            return dict;
        }

        private static void SkipWhitespace(string s, ref int i)
        {
            while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
        }

        private static string ReadString(string s, ref int i)
        {
            if (i >= s.Length || s[i] != '"') return string.Empty;
            i++; // skip opening quote
            int start = i;
            var sb = new StringBuilder();

            while (i < s.Length)
            {
                char c = s[i];
                if (c == '\\' && i + 1 < s.Length)
                {
                    i++;
                    char next = s[i];
                    if (next == '"' || next == '\\' || next == '/') sb.Append(next);
                    else if (next == 'n') sb.Append('\n');
                    else if (next == 'r') sb.Append('\r');
                    else if (next == 't') sb.Append('\t');
                    else sb.Append(next);
                }
                else if (c == '"')
                {
                    i++; // skip closing quote
                    return sb.ToString();
                }
                else
                {
                    sb.Append(c);
                }
                i++;
            }

            return sb.ToString();
        }

        private static string ReadValue(string s, ref int i)
        {
            if (i >= s.Length) return string.Empty;

            if (s[i] == '"')
            {
                return ReadString(s, ref i);
            }

            if (s[i] == '{' || s[i] == '[')
            {
                char open = s[i];
                char close = open == '{' ? '}' : ']';
                int depth = 1;
                int start = i;
                i++;

                while (i < s.Length && depth > 0)
                {
                    if (s[i] == '"')
                    {
                        ReadString(s, ref i);
                        continue;
                    }
                    if (s[i] == open) depth++;
                    else if (s[i] == close) depth--;
                    i++;
                }

                return s.Substring(start, i - start);
            }

            // Primitive (number, bool, null)
            int valStart = i;
            while (i < s.Length && s[i] != ',' && s[i] != '}' && s[i] != ']' && !char.IsWhiteSpace(s[i]))
            {
                i++;
            }

            return s.Substring(valStart, i - valStart);
        }

        public static string Escape(string? s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r");
        }
    }
}
