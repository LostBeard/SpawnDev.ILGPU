using System.Security.Cryptography;
using SpawnDev.SpawnJS;
using SpawnDev.SpawnJS.JSObjects;

namespace SpawnDev.ILGPU.P2P;

/// <summary>
/// Serverless WebAuthn provider for P2P swarm ownership.
/// Uses navigator.credentials (FIDO2/WebAuthn) to bind swarm ownership
/// to a hardware security key (YubiKey, passkey, Windows Hello, Touch ID).
///
/// Unlike traditional WebAuthn which requires a relying party server,
/// P2P uses self-generated challenges and stores credentials in the
/// owner-signed KeyRegistry (published via BEP 46).
///
/// Usage:
///   var provider = new HardwareKeyProvider();
///   var identity = await provider.RegisterAsync("My YubiKey");
///   // Later, on a different device:
///   var identity = await provider.AuthenticateAsync(credentialId);
/// </summary>
public class HardwareKeyProvider
{
    /// <summary>
    /// Relying party ID — used by WebAuthn to scope credentials.
    /// Must match the origin domain. Auto-detected from window.location.hostname
    /// in browser, defaults to "localhost" on desktop.
    /// Credentials created under one rpId cannot be used under another.
    /// </summary>
    public string RpId { get; set; } = GetDefaultRpId();

    private static string GetDefaultRpId()
    {
        if (!OperatingSystem.IsBrowser()) return "localhost";
        try
        {
            return SpawnJSRuntime.Instance.Get<string>("window.location.hostname");
        }
        catch { return "localhost"; }
    }

    /// <summary>
    /// Relying party display name shown in the browser's WebAuthn prompt.
    /// </summary>
    public string RpName { get; set; } = "SpawnDev P2P Compute";

    /// <summary>
    /// Timeout for WebAuthn ceremonies (ms). User must interact with the
    /// authenticator within this window. Default: 60 seconds.
    /// </summary>
    public int TimeoutMs { get; set; } = 60_000;

    /// <summary>
    /// Register a new hardware key as a P2P swarm owner.
    /// Prompts the user to insert/tap their authenticator (YubiKey, passkey, etc.).
    /// Returns a HardwareKeyCredential containing the public key and credential ID.
    /// </summary>
    /// <param name="label">Human-readable label for this key (e.g., "TJ's YubiKey").</param>
    /// <param name="userId">Unique user identifier. Defaults to a random ID.</param>
    /// <returns>The registered credential, or null if the user cancelled.</returns>
    public async Task<HardwareKeyCredential?> RegisterAsync(string label, byte[]? userId = null)
    {
        if (!OperatingSystem.IsBrowser())
            return null; // WebAuthn requires a browser

        userId ??= RandomNumberGenerator.GetBytes(32);
        var challenge = RandomNumberGenerator.GetBytes(32);

        using var creds = CredentialsContainer.GetDefaultCredentialsContainer();

        var options = new CredentialCreatePublicKeyOptions
        {
            PublicKey = new CredentialCreatePublicKey
            {
                Rp = new RelyingParty
                {
                    Id = RpId,
                    Name = RpName,
                },
                User = new CredentialUser
                {
                    Id = new Uint8Array(userId).Buffer,
                    Name = label,
                    DisplayName = label,
                },
                Challenge = new Uint8Array(challenge).Buffer,
                PubKeyCredParams = new List<PublicKeyCredentialParameter>
                {
                    new() { Alg = -7, Type = "public-key" },   // ES256 (ECDSA P-256)
                    new() { Alg = -257, Type = "public-key" },  // RS256 (RSA, fallback)
                },
                AuthenticatorSelection = new AuthenticatorSelection
                {
                    AuthenticatorAttachment = "cross-platform", // YubiKey, not platform
                    UserVerification = "preferred",
                    ResidentKey = "preferred",
                },
                Attestation = "direct",
                Timeout = (uint)TimeoutMs,
            },
        };

        PublicKeyCredential<AuthenticatorAttestationResponse>? credential;
        try
        {
            credential = await creds.Create(options);
        }
        catch
        {
            return null; // User cancelled or authenticator not available
        }

        if (credential == null) return null;

        // Extract the public key (SPKI format) from the attestation response
        using var publicKeyBuffer = credential.Response.GetPublicKey();
        var publicKeySpki = publicKeyBuffer.ReadBytes();
        var algorithm = credential.Response.GetPublicKeyAlgorithm();
        var transports = credential.Response.GetTransports();
        using var rawId = credential.RawId;
        var credentialId = rawId.ReadBytes();

        return new HardwareKeyCredential
        {
            CredentialId = credentialId,
            PublicKeySpki = publicKeySpki,
            Algorithm = algorithm,
            Transports = transports,
            Label = label,
            AuthenticatorAttachment = credential.AuthenticatorAttachment,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    /// <summary>
    /// Authenticate with an existing hardware key.
    /// Prompts the user to insert/tap their authenticator.
    /// Returns the assertion containing the signature for verification.
    /// </summary>
    /// <param name="allowedCredentials">
    /// Credential IDs to accept. Pass the CredentialId from registration.
    /// If null, any discoverable credential for this rpId is accepted.
    /// </param>
    /// <returns>The assertion result, or null if the user cancelled.</returns>
    public async Task<HardwareKeyAssertion?> AuthenticateAsync(
        IEnumerable<HardwareKeyAllowedCredential>? allowedCredentials = null)
    {
        if (!OperatingSystem.IsBrowser())
            return null;

        var challenge = RandomNumberGenerator.GetBytes(32);

        using var creds = CredentialsContainer.GetDefaultCredentialsContainer();

        var getOptions = new CredentialGetPublicKeyOptions
        {
            PublicKey = new CredentialGetPublicKey
            {
                Challenge = new Uint8Array(challenge).Buffer,
                RpId = RpId,
                Timeout = (uint)TimeoutMs,
                UserVerification = "preferred",
            },
        };

        // If specific credentials are allowed, restrict to those
        if (allowedCredentials != null)
        {
            getOptions.PublicKey.AllowCredentials = allowedCredentials
                .Select(c =>
                {
                    var allowed = new CredentialGetPublicKeyAllowedCredential
                    {
                        Id = new Uint8Array(c.CredentialId).Buffer,
                        Type = "public-key",
                    };
                    if (c.Transports?.Count > 0)
                        allowed.Transports = c.Transports;
                    return allowed;
                })
                .ToList();
        }

        PublicKeyCredential<AuthenticatorAssertionResponse>? assertion;
        try
        {
            assertion = await creds.Get(getOptions);
        }
        catch
        {
            return null; // User cancelled
        }

        if (assertion == null) return null;

        using var rawId = assertion.RawId;
        return new HardwareKeyAssertion
        {
            CredentialId = rawId.ReadBytes(),
            AuthenticatorData = assertion.Response.AuthenticatorData.ReadBytes(),
            ClientDataJson = assertion.Response.ClientDataJSON.ReadBytes(),
            Signature = assertion.Response.Signature.ReadBytes(),
            UserHandle = assertion.Response.UserHandle?.ReadBytes(),
            Challenge = challenge,
        };
    }

    /// <summary>
    /// Verify a WebAuthn assertion signature against a known public key.
    /// This is how peers verify that someone proving ownership actually holds
    /// the private key stored in the hardware authenticator.
    ///
    /// WebAuthn signatures are computed over: authenticatorData || SHA-256(clientDataJSON)
    /// </summary>
    /// <param name="assertion">The assertion from AuthenticateAsync().</param>
    /// <param name="publicKeySpki">The public key from registration (stored in KeyRegistry).</param>
    /// <param name="crypto">Crypto provider for signature verification.</param>
    /// <returns>True if the assertion is cryptographically valid.</returns>
    public async Task<bool> VerifyAssertionAsync(
        HardwareKeyAssertion assertion,
        byte[] publicKeySpki,
        SpawnDev.SpawnJS.Cryptography.IPortableCrypto crypto)
    {
        // 1. Reconstruct what was signed: authenticatorData || SHA-256(clientDataJSON)
        var clientDataHash = System.Security.Cryptography.SHA256.HashData(assertion.ClientDataJson);
        var signedData = new byte[assertion.AuthenticatorData.Length + clientDataHash.Length];
        assertion.AuthenticatorData.CopyTo(signedData, 0);
        clientDataHash.CopyTo(signedData, assertion.AuthenticatorData.Length);

        // 2. Verify signature against the public key
        if (!await SwarmIdentity.VerifyAsync(crypto, publicKeySpki, signedData, assertion.Signature))
            return false;

        // 3. Validate challenge is present in clientDataJson
        try
        {
            var clientData = System.Text.Json.JsonDocument.Parse(assertion.ClientDataJson);
            var challengeInJson = clientData.RootElement.GetProperty("challenge").GetString();
            // WebAuthn uses base64url encoding for the challenge
            if (string.IsNullOrEmpty(challengeInJson))
                return false;
        }
        catch
        {
            return false;
        }

        // 4. Validate user presence flag in authenticatorData
        if (assertion.AuthenticatorData.Length < 33)
            return false;
        var flags = assertion.AuthenticatorData[32];
        if ((flags & 0x01) == 0) // UserPresent bit not set
            return false;

        return true;
    }

    /// <summary>
    /// Check if WebAuthn is available in the current environment.
    /// </summary>
    public static bool IsAvailable => OperatingSystem.IsBrowser();

    /// <summary>
    /// Check if a platform authenticator (Windows Hello, Touch ID) is available.
    /// </summary>
    public static async Task<bool> IsPlatformAuthenticatorAvailable()
    {
        if (!IsAvailable) return false;
        try
        {
            return await PublicKeyCredential<AuthenticatorAttestationResponse>
                .IsUserVerifyingPlatformAuthenticatorAvailable();
        }
        catch { return false; }
    }
}

/// <summary>
/// Result of a hardware key registration ceremony.
/// Contains everything needed to add this key to a P2P KeyRegistry.
/// </summary>
public class HardwareKeyCredential
{
    /// <summary>Unique credential ID from the authenticator.</summary>
    public byte[] CredentialId { get; set; } = System.Array.Empty<byte>();

    /// <summary>Public key in SPKI format — use this for KeyRegistry.</summary>
    public byte[] PublicKeySpki { get; set; } = System.Array.Empty<byte>();

    /// <summary>COSE algorithm identifier (-7 = ES256, -257 = RS256).</summary>
    public int Algorithm { get; set; }

    /// <summary>Supported transport methods (usb, nfc, ble, internal).</summary>
    public List<string> Transports { get; set; } = new();

    /// <summary>Human-readable label.</summary>
    public string Label { get; set; } = "";

    /// <summary>How the authenticator is attached (cross-platform, platform).</summary>
    public string? AuthenticatorAttachment { get; set; }

    /// <summary>When this credential was created.</summary>
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>
/// Result of a hardware key authentication ceremony.
/// Contains the signature and data needed to verify the owner's identity.
/// </summary>
public class HardwareKeyAssertion
{
    /// <summary>Credential ID that was used.</summary>
    public byte[] CredentialId { get; set; } = System.Array.Empty<byte>();

    /// <summary>Authenticator data (contains rpIdHash, flags, counter).</summary>
    public byte[] AuthenticatorData { get; set; } = System.Array.Empty<byte>();

    /// <summary>Client data JSON (contains challenge, origin, type).</summary>
    public byte[] ClientDataJson { get; set; } = System.Array.Empty<byte>();

    /// <summary>Signature over authenticatorData + SHA-256(clientDataJson).</summary>
    public byte[] Signature { get; set; } = System.Array.Empty<byte>();

    /// <summary>User handle (opaque user ID from registration).</summary>
    public byte[]? UserHandle { get; set; }

    /// <summary>The challenge that was signed (for verification).</summary>
    public byte[] Challenge { get; set; } = System.Array.Empty<byte>();
}

/// <summary>
/// An allowed credential for authentication (credential ID + transports).
/// </summary>
public class HardwareKeyAllowedCredential
{
    /// <summary>Credential ID from registration.</summary>
    public byte[] CredentialId { get; set; } = System.Array.Empty<byte>();

    /// <summary>Transport hints (usb, nfc, ble, internal).</summary>
    public List<string>? Transports { get; set; }
}
