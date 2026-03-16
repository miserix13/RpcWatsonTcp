using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace RpcWatsonTcp
{
    /// <summary>
    /// TLS configuration for <see cref="RpcServer"/>.
    /// A server certificate is required; provide it via <see cref="Certificate"/> or
    /// <see cref="PfxPath"/> + <see cref="PfxPassword"/>.
    /// </summary>
    public sealed class RpcServerTlsOptions
    {
        /// <summary>
        /// Server TLS certificate. Takes precedence over <see cref="PfxPath"/> when both are set.
        /// </summary>
        public X509Certificate2? Certificate { get; set; }

        /// <summary>Path to a PFX/PKCS#12 file containing the server certificate and private key.</summary>
        public string? PfxPath { get; set; }

        /// <summary>Password for the PFX file specified in <see cref="PfxPath"/>.</summary>
        public string? PfxPassword { get; set; }

        /// <summary>TLS protocol version. Defaults to <see cref="RpcTlsVersion.Tls12"/>.</summary>
        public RpcTlsVersion TlsVersion { get; set; } = RpcTlsVersion.Tls12;

        /// <summary>
        /// When <see langword="true"/>, clients must present a valid certificate (mutual TLS).
        /// Defaults to <see langword="false"/>.
        /// </summary>
        public bool RequireClientCertificate { get; set; }

        /// <summary>
        /// Optional callback to validate the client certificate when
        /// <see cref="RequireClientCertificate"/> is <see langword="true"/>.
        /// Return <see langword="true"/> to accept the certificate.
        /// </summary>
        public RemoteCertificateValidationCallback? ClientCertificateValidation { get; set; }
    }
}
