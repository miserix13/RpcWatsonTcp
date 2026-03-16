using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace RpcWatsonTcp.SourceGenerator
{
    /// <summary>
    /// Parses a subset of proto3 syntax into a <see cref="ProtoFile"/> model.
    /// Supported: syntax, package, message, enum, scalar fields, repeated fields, nested definitions.
    /// Not supported (v1): oneof, map, import, extend, services, custom options.
    /// </summary>
    internal static class ProtoParser
    {
        // Matches a field line inside a message:
        //   [repeated] [optional] <type> <name> = <number> [options] ;
        private static readonly Regex FieldRegex = new Regex(
            @"^\s*(?:(repeated|optional|required)\s+)?(\w+(?:\.\w+)*)\s+(\w+)\s*=\s*(\d+)\s*(?:\[.*?\])?\s*;",
            RegexOptions.Compiled);

        // Matches enum value lines: <NAME> = <number> [options] ;
        private static readonly Regex EnumValueRegex = new Regex(
            @"^\s*(\w+)\s*=\s*(-?\d+)\s*(?:\[.*?\])?\s*;",
            RegexOptions.Compiled);

        private static readonly Regex PackageRegex = new Regex(
            @"^\s*package\s+([\w.]+)\s*;", RegexOptions.Compiled);

        public static ProtoFile Parse(string text)
        {
            var file = new ProtoFile();
            string clean = Normalize(StripComments(text));
            string[] lines = clean.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

            int i = 0;
            while (i < lines.Length)
            {
                string line = lines[i].Trim();

                var pkgMatch = PackageRegex.Match(line);
                if (pkgMatch.Success)
                {
                    file.Package = pkgMatch.Groups[1].Value;
                    i++;
                    continue;
                }

                if (line.StartsWith("message ", StringComparison.Ordinal))
                {
                    string name = ParseTypeName(line, "message");
                    var msg = new ProtoMessage(name);
                    i = ParseMessageBody(lines, i + 1, msg);
                    file.Messages.Add(msg);
                    continue;
                }

                if (line.StartsWith("enum ", StringComparison.Ordinal))
                {
                    string name = ParseTypeName(line, "enum");
                    var enumDef = new ProtoEnum(name);
                    i = ParseEnumBody(lines, i + 1, enumDef);
                    file.Enums.Add(enumDef);
                    continue;
                }

                i++;
            }

            return file;
        }

        // ── Private helpers ──────────────────────────────────────────────────

        /// <summary>
        /// Parses the body of a message, starting from the line that contains (or precedes) the
        /// opening brace. After normalization, braces are always on their own lines.
        /// </summary>
        private static int ParseMessageBody(string[] lines, int start, ProtoMessage msg)
        {
            int i = start;
            bool open = false;

            while (i < lines.Length)
            {
                string line = lines[i].Trim();

                if (!open)
                {
                    if (line == "{") { open = true; }
                    i++;
                    continue;
                }

                if (line == "}") { i++; break; }

                if (line.StartsWith("message ", StringComparison.Ordinal))
                {
                    string name = ParseTypeName(line, "message");
                    var nested = new ProtoMessage(name);
                    i = ParseMessageBody(lines, i + 1, nested);
                    msg.NestedMessages.Add(nested);
                    continue;
                }

                if (line.StartsWith("enum ", StringComparison.Ordinal))
                {
                    string name = ParseTypeName(line, "enum");
                    var enumDef = new ProtoEnum(name);
                    i = ParseEnumBody(lines, i + 1, enumDef);
                    msg.NestedEnums.Add(enumDef);
                    continue;
                }

                var m = FieldRegex.Match(line);
                if (m.Success)
                {
                    bool repeated = m.Groups[1].Value == "repeated";
                    string typeName = m.Groups[2].Value;
                    string fieldName = m.Groups[3].Value;
                    int number = int.TryParse(m.Groups[4].Value, out int n) ? n : 0;
                    msg.Fields.Add(new ProtoField(fieldName, typeName, number, repeated));
                }

                i++;
            }
            return i;
        }

        private static int ParseEnumBody(string[] lines, int start, ProtoEnum enumDef)
        {
            int i = start;
            bool open = false;

            while (i < lines.Length)
            {
                string line = lines[i].Trim();

                if (!open)
                {
                    if (line == "{") { open = true; }
                    i++;
                    continue;
                }

                if (line == "}") { i++; break; }

                var m = EnumValueRegex.Match(line);
                if (m.Success && !line.TrimStart().StartsWith("option", StringComparison.Ordinal))
                {
                    string name = m.Groups[1].Value;
                    int number = int.TryParse(m.Groups[2].Value, out int n) ? n : 0;
                    enumDef.Values.Add(new ProtoEnumValue(name, number));
                }

                i++;
            }
            return i;
        }

        private static string ParseTypeName(string line, string keyword)
        {
            int start = keyword.Length + 1;
            // After normalization, braces are on separate lines, so no brace on this line.
            // But handle both cases for robustness.
            int brace = line.IndexOf('{');
            string segment = brace >= 0 ? line.Substring(start, brace - start) : line.Substring(start);
            return segment.Trim();
        }

        /// <summary>Strips // line comments and /* block */ comments.</summary>
        private static string StripComments(string text)
        {
            text = Regex.Replace(text, @"/\*.*?\*/", " ", RegexOptions.Singleline);
            text = Regex.Replace(text, @"//[^\r\n]*", string.Empty);
            return text;
        }

        /// <summary>
        /// Ensures braces are always on their own lines so the line-based parser handles
        /// both single-line and multi-line message/enum declarations uniformly.
        /// </summary>
        private static string Normalize(string text)
        {
            var sb = new StringBuilder(text.Length + 64);
            foreach (char c in text)
            {
                if (c == '{') { sb.Append('\n'); sb.Append('{'); sb.Append('\n'); }
                else if (c == '}') { sb.Append('\n'); sb.Append('}'); sb.Append('\n'); }
                else sb.Append(c);
            }
            return sb.ToString();
        }
    }
}
