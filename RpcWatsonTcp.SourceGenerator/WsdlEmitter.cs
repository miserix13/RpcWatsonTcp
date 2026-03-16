using System;
using System.Collections.Generic;
using System.Text;

namespace RpcWatsonTcp.SourceGenerator
{
    /// <summary>
    /// Emits a WSDL 1.1 XML specification string from a <see cref="ProtoFile"/> model.
    /// Each message whose name ends with "Request" becomes a SOAP operation.
    /// Message types become xs:complexType definitions; enum types become xs:simpleType.
    /// </summary>
    internal static class WsdlEmitter
    {
        private const string Tns = "tns";
        private const string XmlNsTns = "urn:rpcwatsontcp:generated";
        private const string SoapEnc = "http://schemas.xmlsoap.org/soap/encoding/";
        private const string SoapEnv = "http://schemas.xmlsoap.org/soap/envelope/";
        private const string Wsdl11 = "http://schemas.xmlsoap.org/wsdl/";
        private const string Soap11 = "http://schemas.xmlsoap.org/wsdl/soap/";
        private const string Xs = "http://www.w3.org/2001/XMLSchema";

        // ── Proto3 → XSD type mapping ─────────────────────────────────────────

        private static string XsdType(string protoType)
        {
            switch (protoType)
            {
                case "string":   return "xs:string";
                case "int32":
                case "sint32":
                case "sfixed32": return "xs:int";
                case "int64":
                case "sint64":
                case "sfixed64": return "xs:long";
                case "uint32":
                case "fixed32":  return "xs:unsignedInt";
                case "uint64":
                case "fixed64":  return "xs:unsignedLong";
                case "float":    return "xs:float";
                case "double":   return "xs:double";
                case "bool":     return "xs:boolean";
                case "bytes":    return "xs:base64Binary";
                default:         return Tns + ":" + SimpleName(protoType);
            }
        }

        // ── Public entry point ────────────────────────────────────────────────

        public static string Emit(ProtoFile file, string serviceName)
        {
            var sb = new StringBuilder();
            sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
            sb.Append("<definitions name=\""); sb.Append(XmlEsc(serviceName)); sb.Append('"');
            sb.Append(" targetNamespace=\""); sb.Append(XmlNsTns); sb.Append('"');
            sb.Append(" xmlns=\""); sb.Append(Wsdl11); sb.Append('"');
            sb.Append(" xmlns:"); sb.Append(Tns); sb.Append("=\""); sb.Append(XmlNsTns); sb.Append('"');
            sb.Append(" xmlns:xs=\""); sb.Append(Xs); sb.Append('"');
            sb.Append(" xmlns:soap=\""); sb.Append(Soap11); sb.Append('"');
            sb.AppendLine(">");

            EmitTypes(sb, file);
            EmitMessages(sb, file);
            EmitPortType(sb, file, serviceName);
            EmitBinding(sb, file, serviceName);
            EmitService(sb, serviceName);

            sb.Append("</definitions>");
            return sb.ToString();
        }

        // ── Types section ─────────────────────────────────────────────────────

        private static void EmitTypes(StringBuilder sb, ProtoFile file)
        {
            sb.AppendLine("  <types>");
            sb.Append("    <xs:schema targetNamespace=\""); sb.Append(XmlNsTns);
            sb.Append("\" xmlns:xs=\""); sb.Append(Xs); sb.AppendLine("\">");

            EmitXsdTypes(sb, file.Messages, "      ");
            EmitXsdEnums(sb, file.Enums, "      ");

            sb.AppendLine("    </xs:schema>");
            sb.AppendLine("  </types>");
        }

        private static void EmitXsdTypes(StringBuilder sb, List<ProtoMessage> messages, string indent)
        {
            foreach (var msg in messages)
            {
                sb.Append(indent); sb.Append("<xs:complexType name=\""); sb.Append(XmlEsc(msg.Name)); sb.AppendLine("\">");
                sb.Append(indent); sb.AppendLine("  <xs:sequence>");
                foreach (var field in msg.Fields)
                {
                    sb.Append(indent); sb.Append("    <xs:element name=\"");
                    sb.Append(XmlEsc(field.Name)); sb.Append("\" type=\"");
                    sb.Append(XsdType(field.TypeName)); sb.Append('"');
                    if (field.Repeated)
                        sb.Append(" minOccurs=\"0\" maxOccurs=\"unbounded\"");
                    else
                        sb.Append(" minOccurs=\"0\" maxOccurs=\"1\"");
                    sb.AppendLine("/>");
                }
                sb.Append(indent); sb.AppendLine("  </xs:sequence>");
                sb.Append(indent); sb.AppendLine("</xs:complexType>");

                // Recursively emit nested types
                EmitXsdTypes(sb, msg.NestedMessages, indent);
                EmitXsdEnums(sb, msg.NestedEnums, indent);
            }
        }

        private static void EmitXsdEnums(StringBuilder sb, List<ProtoEnum> enums, string indent)
        {
            foreach (var e in enums)
            {
                sb.Append(indent); sb.Append("<xs:simpleType name=\""); sb.Append(XmlEsc(e.Name)); sb.AppendLine("\">");
                sb.Append(indent); sb.AppendLine("  <xs:restriction base=\"xs:string\">");
                foreach (var val in e.Values)
                {
                    sb.Append(indent); sb.Append("    <xs:enumeration value=\"");
                    sb.Append(XmlEsc(CSharpEmitter.ScreamingToPascal(val.Name))); sb.AppendLine("\"/>");
                }
                sb.Append(indent); sb.AppendLine("  </xs:restriction>");
                sb.Append(indent); sb.AppendLine("</xs:simpleType>");
            }
        }

        // ── Messages section ──────────────────────────────────────────────────

        private static void EmitMessages(StringBuilder sb, ProtoFile file)
        {
            EmitWsdlMessages(sb, file.Messages);
        }

        private static void EmitWsdlMessages(StringBuilder sb, List<ProtoMessage> messages)
        {
            foreach (var msg in messages)
            {
                sb.Append("  <message name=\""); sb.Append(XmlEsc(msg.Name)); sb.AppendLine("\">");
                sb.Append("    <part name=\"parameters\" type=\""); sb.Append(Tns); sb.Append(':');
                sb.Append(XmlEsc(msg.Name)); sb.AppendLine("\"/>");
                sb.AppendLine("  </message>");
                EmitWsdlMessages(sb, msg.NestedMessages);
            }
        }

        // ── PortType section ──────────────────────────────────────────────────

        private static void EmitPortType(StringBuilder sb, ProtoFile file, string serviceName)
        {
            sb.Append("  <portType name=\""); sb.Append(XmlEsc(serviceName)); sb.AppendLine("PortType\">");

            EmitOperations(sb, file.Messages, file);

            sb.AppendLine("  </portType>");
        }

        private static void EmitOperations(StringBuilder sb, List<ProtoMessage> messages, ProtoFile file)
        {
            foreach (var msg in messages)
            {
                if (!msg.Name.EndsWith("Request", StringComparison.Ordinal)) continue;

                string prefix = msg.Name.Substring(0, msg.Name.Length - "Request".Length);
                string opName = prefix;
                string? replyName = FindReply(file, prefix);

                sb.Append("    <operation name=\""); sb.Append(XmlEsc(opName)); sb.AppendLine("\">");
                sb.Append("      <input message=\""); sb.Append(Tns); sb.Append(':');
                sb.Append(XmlEsc(msg.Name)); sb.AppendLine("\"/>");
                if (replyName != null)
                {
                    sb.Append("      <output message=\""); sb.Append(Tns); sb.Append(':');
                    sb.Append(XmlEsc(replyName)); sb.AppendLine("\"/>");
                }
                sb.AppendLine("    </operation>");
            }
        }

        // ── Binding section ───────────────────────────────────────────────────

        private static void EmitBinding(StringBuilder sb, ProtoFile file, string serviceName)
        {
            sb.Append("  <binding name=\""); sb.Append(XmlEsc(serviceName)); sb.Append("Binding\" type=\"");
            sb.Append(Tns); sb.Append(':'); sb.Append(XmlEsc(serviceName)); sb.AppendLine("PortType\">");
            sb.AppendLine("    <soap:binding style=\"document\" transport=\"http://schemas.xmlsoap.org/soap/http\"/>");

            EmitBindingOperations(sb, file.Messages, file);

            sb.AppendLine("  </binding>");
        }

        private static void EmitBindingOperations(StringBuilder sb, List<ProtoMessage> messages, ProtoFile file)
        {
            foreach (var msg in messages)
            {
                if (!msg.Name.EndsWith("Request", StringComparison.Ordinal)) continue;

                string prefix = msg.Name.Substring(0, msg.Name.Length - "Request".Length);
                string? replyName = FindReply(file, prefix);

                sb.Append("    <operation name=\""); sb.Append(XmlEsc(prefix)); sb.AppendLine("\">");
                sb.Append("      <soap:operation soapAction=\""); sb.Append(XmlNsTns); sb.Append('/');
                sb.Append(XmlEsc(prefix)); sb.AppendLine("\"/>");
                sb.AppendLine("      <input><soap:body use=\"literal\"/></input>");
                if (replyName != null)
                    sb.AppendLine("      <output><soap:body use=\"literal\"/></output>");
                sb.AppendLine("    </operation>");
            }
        }

        // ── Service section ───────────────────────────────────────────────────

        private static void EmitService(StringBuilder sb, string serviceName)
        {
            sb.Append("  <service name=\""); sb.Append(XmlEsc(serviceName)); sb.AppendLine("Service\">");
            sb.Append("    <port name=\""); sb.Append(XmlEsc(serviceName)); sb.Append("Port\" binding=\"");
            sb.Append(Tns); sb.Append(':'); sb.Append(XmlEsc(serviceName)); sb.AppendLine("Binding\">");
            sb.AppendLine("      <soap:address location=\"http://localhost:9000/rpc\"/>");
            sb.AppendLine("    </port>");
            sb.AppendLine("  </service>");
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

        private static string XmlEsc(string value)
        {
            // Attribute values need &, <, >, ", ' escaped
            var sb = new StringBuilder(value.Length);
            foreach (char c in value)
            {
                switch (c)
                {
                    case '&':  sb.Append("&amp;");  break;
                    case '<':  sb.Append("&lt;");   break;
                    case '>':  sb.Append("&gt;");   break;
                    case '"':  sb.Append("&quot;"); break;
                    case '\'': sb.Append("&apos;"); break;
                    default:   sb.Append(c);        break;
                }
            }
            return sb.ToString();
        }
    }
}
