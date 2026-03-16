using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace RpcWatsonTcp
{
    /// <summary>
    /// TLS configuration for <see cref="RpcClient"/>.
    /// For basic TLS (server-certificate-only), no client certificate is needed — just set
    /// <see cref="AcceptAnyCertificate"/> or provide a <see cref="ServerCertificateValidation"/>
    /// callback. For mutual TLS, provide a client certificate via <see cref="Certificate"/> or
    /// <see cref="PfxPath"/> + <see cref="PfxPassword"/>.
    /// </summary>
    public sealed class RpcClientTlsOptions
    {
        /// <summary>
        /// Optional client certificate for mutual TLS. Takes precedence over <see cref="PfxPath"/>
        /// when both are set.
        /// </summary>
        public X509Certificate2? Certificate { get; set; }

        /// <summary>Path to a PFX/PKCS#12 file containing the client certificate and private key.</summary>
        public string? PfxPath { get; set; }

        /// <summary>Password for the PFX file specified in <see cref="PfxPath"/>.</summary>
        public string? PfxPassword { get; set; }

        /// <summary>TLS protocol version. Defaults to <see cref="RpcTlsVersion.Tls12"/>.</summary>
        public RpcTlsVersion TlsVersion { get; set; } = RpcTlsVersion.Tls12;

        /// <summary>
        /// When <see langword="true"/>, accepts any server certificate without validation.
        /// <b>For development and testing only.</b> Do not use in production.
        /// Takes precedence over <see cref="ServerCertificateValidation"/>.
        /// </summary>
        public bool AcceptAnyCertificate { get; set; }

        /// <summary>
        /// Optional callback to validate the server's TLS certificate.
        /// Return <see langword="true"/> to accept the certificate.
        /// Ignored when <see cref="AcceptAnyCertificate"/> is <see langword="true"/>.
        /// </summary>
        public RemoteCertificateValidationCallback? ServerCertificateValidation { get; set; }
    }
}
