using System;
using System.Collections.Generic;
using System.Text;

namespace RpcWatsonTcp.SourceGenerator
{
    /// <summary>
    /// Emits an OpenAPI 3.0 YAML specification string from a <see cref="ProtoFile"/> model.
    /// Produces the same logical content as <see cref="OpenApiEmitter"/> but in YAML format.
    /// Hand-rolled to avoid external dependencies (targets netstandard2.0).
    /// </summary>
    internal static class YamlEmitter
    {
        // ── Proto3 → YAML type fragments ──────────────────────────────────────

        private static bool IsScalar(string protoType)
        {
            switch (protoType)
            {
                case "string":
                case "int32": case "sint32": case "sfixed32":
                case "int64": case "sint64": case "sfixed64":
                case "uint32": case "fixed32":
                case "uint64": case "fixed64":
                case "float":
                case "double":
                case "bool":
                case "bytes":
                    return true;
                default:
                    return false;
            }
        }

        // Appends "type: X" and optional "format: Y" lines at the given indent level.
        private static void AppendScalarTypeLines(StringBuilder sb, string protoType, string indent)
        {
            switch (protoType)
            {
                case "string":
                    sb.Append(indent); sb.AppendLine("type: string");
                    break;
                case "int32": case "sint32": case "sfixed32":
                    sb.Append(indent); sb.AppendLine("type: integer");
                    sb.Append(indent); sb.AppendLine("format: int32");
                    break;
                case "int64": case "sint64": case "sfixed64":
                    sb.Append(indent); sb.AppendLine("type: integer");
                    sb.Append(indent); sb.AppendLine("format: int64");
                    break;
                case "uint32": case "fixed32":
                    sb.Append(indent); sb.AppendLine("type: integer");
                    sb.Append(indent); sb.AppendLine("format: int32");
                    sb.Append(indent); sb.AppendLine("minimum: 0");
                    break;
                case "uint64": case "fixed64":
                    sb.Append(indent); sb.AppendLine("type: integer");
                    sb.Append(indent); sb.AppendLine("format: int64");
                    sb.Append(indent); sb.AppendLine("minimum: 0");
                    break;
                case "float":
                    sb.Append(indent); sb.AppendLine("type: number");
                    sb.Append(indent); sb.AppendLine("format: float");
                    break;
                case "double":
                    sb.Append(indent); sb.AppendLine("type: number");
                    sb.Append(indent); sb.AppendLine("format: double");
                    break;
                case "bool":
                    sb.Append(indent); sb.AppendLine("type: boolean");
                    break;
                case "bytes":
                    sb.Append(indent); sb.AppendLine("type: string");
                    sb.Append(indent); sb.AppendLine("format: byte");
                    break;
            }
        }

        // ── Public entry point ────────────────────────────────────────────────

        public static string Emit(ProtoFile file, string title)
        {
            var sb = new StringBuilder();
            sb.AppendLine("openapi: \"3.0.0\"");
            sb.AppendLine("info:");
            sb.Append("  title: "); AppendYamlString(sb, title); sb.AppendLine();
            sb.AppendLine("  version: \"1.0.0\"");

            // paths ──────────────────────────────────────────────────────────
            sb.AppendLine("paths:");
            bool hasPaths = false;
            foreach (var msg in file.Messages)
            {
                if (!msg.Name.EndsWith("Request", StringComparison.Ordinal)) continue;
                hasPaths = true;

                string prefix = msg.Name.Substring(0, msg.Name.Length - "Request".Length);
                string? replyName = FindReply(file, prefix);

                sb.Append("  /"); sb.Append(msg.Name); sb.AppendLine(":");
                sb.AppendLine("    post:");
                sb.Append("      operationId: "); sb.AppendLine(msg.Name);
                sb.AppendLine("      requestBody:");
                sb.AppendLine("        required: true");
                sb.AppendLine("        content:");
                sb.AppendLine("          application/json:");
                sb.AppendLine("            schema:");
                sb.Append("              $ref: \"#/components/schemas/"); sb.Append(msg.Name); sb.AppendLine("\"");
                sb.AppendLine("      responses:");
                sb.AppendLine("        \"200\":");
                sb.AppendLine("          description: Success");
                sb.AppendLine("          content:");
                sb.AppendLine("            application/json:");
                sb.AppendLine("              schema:");
                if (replyName != null)
                {
                    sb.Append("                $ref: \"#/components/schemas/"); sb.Append(replyName); sb.AppendLine("\"");
                }
                else
                {
                    sb.AppendLine("                type: object");
                }
            }
            if (!hasPaths) sb.AppendLine("  {}");

            // components/schemas ─────────────────────────────────────────────
            sb.AppendLine("components:");
            sb.AppendLine("  schemas:");
            bool hasSchemas = false;
            EmitMessageSchemas(sb, file.Messages, ref hasSchemas);
            EmitEnumSchemas(sb, file.Enums, ref hasSchemas);
            if (!hasSchemas) sb.AppendLine("    {}");

            return sb.ToString();
        }

        // ── Message schemas ───────────────────────────────────────────────────

        private static void EmitMessageSchemas(StringBuilder sb, List<ProtoMessage> messages, ref bool hasSchemas)
        {
            foreach (var msg in messages)
            {
                hasSchemas = true;
                sb.Append("    "); sb.Append(msg.Name); sb.AppendLine(":");
                sb.AppendLine("      type: object");
                if (msg.Fields.Count > 0)
                {
                    sb.AppendLine("      properties:");
                    foreach (var field in msg.Fields)
                        EmitFieldSchema(sb, field, "        ");
                }

                EmitMessageSchemas(sb, msg.NestedMessages, ref hasSchemas);
                EmitEnumSchemas(sb, msg.NestedEnums, ref hasSchemas);
            }
        }

        private static void EmitFieldSchema(StringBuilder sb, ProtoField field, string indent)
        {
            sb.Append(indent); sb.Append(field.Name); sb.AppendLine(":");
            string inner = indent + "  ";
            if (field.Repeated)
            {
                sb.Append(inner); sb.AppendLine("type: array");
                sb.Append(inner); sb.AppendLine("items:");
                string itemIndent = inner + "  ";
                if (IsScalar(field.TypeName))
                    AppendScalarTypeLines(sb, field.TypeName, itemIndent);
                else
                {
                    sb.Append(itemIndent); sb.Append("$ref: \"#/components/schemas/");
                    sb.Append(SimpleName(field.TypeName)); sb.AppendLine("\"");
                }
            }
            else if (IsScalar(field.TypeName))
            {
                AppendScalarTypeLines(sb, field.TypeName, inner);
            }
            else
            {
                sb.Append(inner); sb.Append("$ref: \"#/components/schemas/");
                sb.Append(SimpleName(field.TypeName)); sb.AppendLine("\"");
            }
        }

        // ── Enum schemas ──────────────────────────────────────────────────────

        private static void EmitEnumSchemas(StringBuilder sb, List<ProtoEnum> enums, ref bool hasSchemas)
        {
            foreach (var e in enums)
            {
                hasSchemas = true;
                sb.Append("    "); sb.Append(e.Name); sb.AppendLine(":");
                sb.AppendLine("      type: string");
                sb.AppendLine("      enum:");
                foreach (var val in e.Values)
                {
                    sb.Append("        - "); sb.AppendLine(CSharpEmitter.ScreamingToPascal(val.Name));
                }
            }
        }

        // ── Utilities ─────────────────────────────────────────────────────────

        private static string? FindReply(ProtoFile file, string prefix)
        {
            foreach (var msg in file.Messages)
            {
                if (msg.Name == prefix + "Reply" || msg.Name == prefix + "Response")
                    return msg.Name;
            }
            return null;
        }

        private static string SimpleName(string typeName)
        {
            int dot = typeName.LastIndexOf('.');
            return dot >= 0 ? typeName.Substring(dot + 1) : typeName;
        }

        /// <summary>
        /// Appends a YAML scalar string value. Quotes strings that start with special characters
        /// or contain characters that require quoting in YAML.
        /// </summary>
        private static void AppendYamlString(StringBuilder sb, string value)
        {
            if (NeedsQuoting(value))
            {
                sb.Append('"');
                foreach (char c in value)
                {
                    if (c == '"') sb.Append("\\\"");
                    else if (c == '\\') sb.Append("\\\\");
                    else sb.Append(c);
                }
                sb.Append('"');
            }
            else
            {
                sb.Append(value);
            }
        }

        private static bool NeedsQuoting(string value)
        {
            if (value.Length == 0) return true;
            char first = value[0];
            // Quote if starts with: { [ * : # | > ' " @ ` & ! % , ? -
            if ("{}[]|>&*!,'\"@`%#?-:".IndexOf(first) >= 0) return true;
            // Quote if contains : followed by space (would be parsed as mapping)
            if (value.Contains(": ")) return true;
            // Quote YAML reserved words
            if (value == "true" || value == "false" || value == "null" ||
                value == "yes" || value == "no" || value == "on" || value == "off")
                return true;
            return false;
        }
    }
}
