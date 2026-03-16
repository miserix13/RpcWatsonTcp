using System;
using System.Collections.Generic;
using System.Text;

namespace RpcWatsonTcp.SourceGenerator
{
    /// <summary>
    /// Emits an OpenAPI 3.0 JSON specification string from a <see cref="ProtoFile"/> model.
    /// Each message whose name ends with "Request" becomes a POST endpoint.
    /// All message and enum types appear in components/schemas.
    /// </summary>
    internal static class OpenApiEmitter
    {
        // ── Proto3 → JSON Schema type mapping ────────────────────────────────

        // Returns the inline JSON Schema fragment (without surrounding braces) for a scalar.
        // Returns null for non-scalar types (handled as $ref).
        private static string? ScalarSchema(string protoType)
        {
            switch (protoType)
            {
                case "string":   return "\"type\":\"string\"";
                case "int32":
                case "sint32":
                case "sfixed32": return "\"type\":\"integer\",\"format\":\"int32\"";
                case "int64":
                case "sint64":
                case "sfixed64": return "\"type\":\"integer\",\"format\":\"int64\"";
                case "uint32":
                case "fixed32":  return "\"type\":\"integer\",\"format\":\"int32\",\"minimum\":0";
                case "uint64":
                case "fixed64":  return "\"type\":\"integer\",\"format\":\"int64\",\"minimum\":0";
                case "float":    return "\"type\":\"number\",\"format\":\"float\"";
                case "double":   return "\"type\":\"number\",\"format\":\"double\"";
                case "bool":     return "\"type\":\"boolean\"";
                case "bytes":    return "\"type\":\"string\",\"format\":\"byte\"";
                default:         return null; // user-defined type → $ref
            }
        }

        // ── Public entry point ────────────────────────────────────────────────

        public static string Emit(ProtoFile file, string title)
        {
            var sb = new StringBuilder();
            sb.Append('{');
            sb.Append("\"openapi\":\"3.0.0\",");
            sb.Append("\"info\":{\"title\":"); AppendJsonString(sb, title); sb.Append(",\"version\":\"1.0.0\"},");

            // paths ──────────────────────────────────────────────────────────
            sb.Append("\"paths\":{");
            bool firstPath = true;
            EmitPaths(sb, file.Messages, file, ref firstPath);
            sb.Append("},");

            // components/schemas ─────────────────────────────────────────────
            sb.Append("\"components\":{\"schemas\":{");
            bool firstSchema = true;
            EmitSchemas(sb, file.Messages, file, ref firstSchema);
            EmitEnumSchemas(sb, file.Enums, ref firstSchema);
            sb.Append("}}");

            sb.Append('}');
            return sb.ToString();
        }

        // ── Paths ─────────────────────────────────────────────────────────────

        private static void EmitPaths(StringBuilder sb, List<ProtoMessage> messages, ProtoFile file, ref bool first)
        {
            foreach (var msg in messages)
            {
                if (!msg.Name.EndsWith("Request", StringComparison.Ordinal))
                    continue;

                // Find reply by convention: FooRequest → FooReply or FooResponse
                string prefix = msg.Name.Substring(0, msg.Name.Length - "Request".Length);
                string? replyRef = FindReply(file, prefix);

                if (!first) sb.Append(',');
                first = false;

                sb.Append("\"/"); sb.Append(msg.Name); sb.Append("\":{");
                sb.Append("\"post\":{");
                sb.Append("\"operationId\":"); AppendJsonString(sb, msg.Name); sb.Append(',');
                sb.Append("\"requestBody\":{\"required\":true,\"content\":{\"application/json\":{\"schema\":{\"$ref\":\"#/components/schemas/");
                sb.Append(msg.Name); sb.Append("\"}}}}");
                sb.Append(",\"responses\":{\"200\":{\"description\":\"Success\",\"content\":{\"application/json\":{\"schema\":{");
                if (replyRef != null)
                {
                    sb.Append("\"$ref\":\"#/components/schemas/"); sb.Append(replyRef); sb.Append('"');
                }
                else
                {
                    sb.Append("\"type\":\"object\"");
                }
                sb.Append("}}}}}");
                sb.Append("}}");
            }
        }

        private static string? FindReply(ProtoFile file, string prefix)
        {
            foreach (var msg in file.Messages)
            {
                if (msg.Name == prefix + "Reply" || msg.Name == prefix + "Response")
                    return msg.Name;
            }
            return null;
        }

        // ── Schemas ───────────────────────────────────────────────────────────

        private static void EmitSchemas(StringBuilder sb, List<ProtoMessage> messages, ProtoFile file, ref bool first)
        {
            foreach (var msg in messages)
            {
                EmitMessageSchema(sb, msg, ref first);
                // Recursively emit nested messages
                EmitSchemas(sb, msg.NestedMessages, file, ref first);
                EmitEnumSchemas(sb, msg.NestedEnums, ref first);
            }
        }

        private static void EmitMessageSchema(StringBuilder sb, ProtoMessage msg, ref bool first)
        {
            if (!first) sb.Append(',');
            first = false;

            AppendJsonString(sb, msg.Name);
            sb.Append(":{\"type\":\"object\",\"properties\":{");
            bool firstProp = true;
            foreach (var field in msg.Fields)
            {
                if (!firstProp) sb.Append(',');
                firstProp = false;
                AppendJsonString(sb, field.Name);
                sb.Append(':');
                EmitFieldSchema(sb, field);
            }
            sb.Append("}}");
        }

        private static void EmitFieldSchema(StringBuilder sb, ProtoField field)
        {
            sb.Append('{');
            if (field.Repeated)
            {
                sb.Append("\"type\":\"array\",\"items\":");
                sb.Append('{');
                AppendTypeSchema(sb, field.TypeName);
                sb.Append('}');
            }
            else
            {
                AppendTypeSchema(sb, field.TypeName);
            }
            sb.Append('}');
        }

        private static void AppendTypeSchema(StringBuilder sb, string typeName)
        {
            string? scalar = ScalarSchema(typeName);
            if (scalar != null)
            {
                sb.Append(scalar);
            }
            else
            {
                string simpleName = SimpleName(typeName);
                sb.Append("\"$ref\":\"#/components/schemas/"); sb.Append(simpleName); sb.Append('"');
            }
        }

        private static void EmitEnumSchemas(StringBuilder sb, List<ProtoEnum> enums, ref bool first)
        {
            foreach (var e in enums)
            {
                if (!first) sb.Append(',');
                first = false;

                AppendJsonString(sb, e.Name);
                sb.Append(":{\"type\":\"string\",\"enum\":[");
                bool firstVal = true;
                foreach (var val in e.Values)
                {
                    if (!firstVal) sb.Append(',');
                    firstVal = false;
                    AppendJsonString(sb, CSharpEmitter.ScreamingToPascal(val.Name));
                }
                sb.Append("]}");
            }
        }

        // ── Utilities ─────────────────────────────────────────────────────────

        private static string SimpleName(string typeName)
        {
            int dot = typeName.LastIndexOf('.');
            return dot >= 0 ? typeName.Substring(dot + 1) : typeName;
        }

        private static void AppendJsonString(StringBuilder sb, string value)
        {
            sb.Append('"');
            foreach (char c in value)
            {
                switch (c)
                {
                    case '"':  sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n");  break;
                    case '\r': sb.Append("\\r");  break;
                    case '\t': sb.Append("\\t");  break;
                    default:   sb.Append(c);      break;
                }
            }
            sb.Append('"');
        }
    }
}
