// ---------------------------------------------------------------------------------------
//                                   SpawnDev.ILGPU
//                        Copyright (c) 2026 SpawnDev (@LostBeard)
//
// File: SwarmIdentity.cs
//
// Cryptographic identity for swarm ownership. An owner is identified by their
// Ed25519 key pair - not by device, IP, or peer ID. The same key works from any
// device (desktop, browser, YubiKey, passkey).
// ---------------------------------------------------------------------------------------

using System.Text.Json;
using SpawnDev.SpawnJS.Cryptography;

namespace SpawnDev.ILGPU.P2P
{
    /// <summary>
    /// Represents a cryptographic identity for swarm ownership and role management.
    /// Uses Ed25519 via SpawnDev.SpawnJS.Cryptography for cross-platform signing
    /// (browser SubtleCrypto with managed fallback + desktop managed Ed25519).
    /// </summary>
    public class SwarmIdentity : IAsyncDisposable
    {
        private readonly IPortableCrypto _crypto;

        /// <summary>
        /// The Ed25519 key pair for this identity.
        /// </summary>
        public PortableEd25519Key Key { get; }

        /// <summary>
        /// The public key in SPKI format (for sharing with peers).
        /// Ed25519 SPKI is always 44 bytes (12-byte DER prefix + 32-byte raw key).
        /// </summary>
        public byte[] PublicKeySpki { get; private set; } = Array.Empty<byte>();

        /// <summary>
        /// Hex-encoded public key fingerprint (SHA-256 of SPKI bytes).
        /// Used as a compact identity string in protocols and logs.
        /// </summary>
        public string Fingerprint { get; private set; } = "";

        /// <summary>
        /// Optional human-readable label ("TJ's YubiKey", "Lab Desktop", etc.)
        /// </summary>
        public string Label { get; set; } = "";

        private SwarmIdentity(IPortableCrypto crypto, PortableEd25519Key key)
        {
            _crypto = crypto;
            Key = key;
        }

        /// <summary>
        /// Initialize async properties (public key export, fingerprint).
        /// Must be called after construction.
        /// </summary>
        private async Task InitializeAsync()
        {
            PublicKeySpki = await _crypto.ExportPublicKeySpki(Key);
            var hash = await _crypto.Digest("SHA-256", PublicKeySpki);
            Fingerprint = Convert.ToHexString(hash).ToLowerInvariant();
        }

        /// <summary>
        /// Creates a new identity with a fresh Ed25519 key pair.
        /// </summary>
        /// <param name="crypto">The cross-platform crypto provider.</param>
        /// <param name="label">Optional human-readable label.</param>
        public static async Task<SwarmIdentity> CreateAsync(
            IPortableCrypto crypto,
            string label = "")
        {
            var key = await crypto.GenerateEd25519Key(extractable: true);
            var identity = new SwarmIdentity(crypto, key) { Label = label };
            await identity.InitializeAsync();
            return identity;
        }

        /// <summary>
        /// Imports an identity from exported key material (SPKI public + PKCS8 private).
        /// </summary>
        /// <param name="crypto">The cross-platform crypto provider.</param>
        /// <param name="publicKeySpki">The public key in SPKI format (44 bytes).</param>
        /// <param name="privateKeyPkcs8">The private key in PKCS8 format (48 bytes).</param>
        /// <param name="label">Optional human-readable label.</param>
        public static async Task<SwarmIdentity> ImportAsync(
            IPortableCrypto crypto,
            byte[] publicKeySpki,
            byte[] privateKeyPkcs8,
            string label = "")
        {
            var key = await crypto.ImportEd25519Key(publicKeySpki, privateKeyPkcs8,
                extractable: true);
            var identity = new SwarmIdentity(crypto, key) { Label = label };
            await identity.InitializeAsync();
            return identity;
        }

        /// <summary>
        /// Imports a public-key-only identity (for verification, not signing).
        /// </summary>
        /// <param name="crypto">The cross-platform crypto provider.</param>
        /// <param name="publicKeySpki">The public key in SPKI format (44 bytes).</param>
        public static async Task<SwarmIdentity> ImportPublicKeyAsync(
            IPortableCrypto crypto,
            byte[] publicKeySpki)
        {
            var key = await crypto.ImportEd25519Key(publicKeySpki,
                extractable: false);
            var identity = new SwarmIdentity(crypto, key);
            await identity.InitializeAsync();
            return identity;
        }

        /// <summary>
        /// Creates an identity backed by a hardware security key (YubiKey, passkey).
        /// The hardware key holds the private key - signing happens through WebAuthn.
        /// The public key from registration is used for KeyRegistry verification.
        /// </summary>
        /// <param name="crypto">The cross-platform crypto provider.</param>
        /// <param name="credential">The credential from HardwareKeyProvider.RegisterAsync().</param>
        /// <returns>An identity whose public key matches the hardware credential.</returns>
        public static async Task<SwarmIdentity> FromHardwareKeyAsync(
            IPortableCrypto crypto,
            HardwareKeyCredential credential)
        {
            // Import the public key for verification - the private key lives in the authenticator
            var key = await crypto.ImportEd25519Key(credential.PublicKeySpki,
                extractable: false);
            var identity = new SwarmIdentity(crypto, key)
            {
                Label = credential.Label,
                HardwareCredentialId = credential.CredentialId,
                IsHardwareBacked = true,
                // Set directly from the credential - no re-export needed
                // (the key is not extractable since the private key is in the authenticator)
                PublicKeySpki = credential.PublicKeySpki,
            };
            var hash = await crypto.Digest("SHA-256", credential.PublicKeySpki);
            identity.Fingerprint = Convert.ToHexString(hash).ToLowerInvariant();
            return identity;
        }

        /// <summary>
        /// If true, this identity is backed by a hardware security key.
        /// Signing must go through HardwareKeyProvider.AuthenticateAsync() instead of SignAsync().
        /// </summary>
        public bool IsHardwareBacked { get; private set; }

        /// <summary>
        /// The WebAuthn credential ID for hardware-backed identities.
        /// Used to restrict HardwareKeyProvider.AuthenticateAsync() to this specific key.
        /// </summary>
        public byte[]? HardwareCredentialId { get; private set; }

        /// <summary>
        /// Exports the private key material for storage.
        /// </summary>
        /// <returns>The private key in PKCS8 format (48 bytes for Ed25519).</returns>
        public Task<byte[]> ExportPrivateKeyAsync() =>
            _crypto.ExportPrivateKeyPkcs8(Key);

        /// <summary>
        /// Signs data using this identity's Ed25519 private key.
        /// Ed25519 uses SHA-512 internally per RFC 8032 - no hash parameter needed.
        /// </summary>
        /// <param name="data">The data to sign.</param>
        /// <returns>The Ed25519 signature (always 64 bytes).</returns>
        public Task<byte[]> SignAsync(byte[] data) =>
            _crypto.Sign(Key, data);

        /// <summary>
        /// Verifies a signature against this identity's Ed25519 public key.
        /// </summary>
        /// <param name="data">The signed data.</param>
        /// <param name="signature">The signature to verify (64 bytes).</param>
        /// <returns>True if the signature is valid.</returns>
        public Task<bool> VerifyAsync(byte[] data, byte[] signature) =>
            _crypto.Verify(Key, data, signature);

        /// <summary>
        /// Verifies a signature using a public key (static, no identity instance needed).
        /// </summary>
        /// <param name="crypto">The cross-platform crypto provider.</param>
        /// <param name="publicKeySpki">The signer's Ed25519 public key in SPKI format (44 bytes).</param>
        /// <param name="data">The signed data.</param>
        /// <param name="signature">The signature to verify (64 bytes).</param>
        /// <returns>True if the signature is valid.</returns>
        public static async Task<bool> VerifyAsync(
            IPortableCrypto crypto,
            byte[] publicKeySpki,
            byte[] data,
            byte[] signature)
        {
            using var key = await crypto.ImportEd25519Key(publicKeySpki,
                extractable: false);
            return await crypto.Verify(key, data, signature);
        }

        /// <summary>
        /// Signs a serializable object (JSON serialized, then signed).
        /// </summary>
        /// <typeparam name="T">The object type.</typeparam>
        /// <param name="obj">The object to sign.</param>
        /// <returns>The signature bytes.</returns>
        public async Task<byte[]> SignObjectAsync<T>(T obj)
        {
            var json = JsonSerializer.SerializeToUtf8Bytes(obj);
            return await SignAsync(json);
        }

        /// <summary>
        /// Verifies a signature on a serializable object.
        /// </summary>
        /// <typeparam name="T">The object type.</typeparam>
        /// <param name="obj">The object that was signed.</param>
        /// <param name="signature">The signature to verify.</param>
        /// <returns>True if the signature is valid.</returns>
        public async Task<bool> VerifyObjectAsync<T>(T obj, byte[] signature)
        {
            var json = JsonSerializer.SerializeToUtf8Bytes(obj);
            return await VerifyAsync(json, signature);
        }

        /// <inheritdoc/>
        public ValueTask DisposeAsync()
        {
            // PortableEd25519Key zeros seed memory on dispose (DotNetEd25519Key).
            Key.Dispose();
            return ValueTask.CompletedTask;
        }
    }

    /// <summary>
    /// Swarm role hierarchy. Higher values = more authority.
    /// </summary>
    public enum SwarmRole
    {
        /// <summary>Default — execute kernels only.</summary>
        Worker = 0,

        /// <summary>Dispatch work, manage peers. Authority granted by owner.</summary>
        Coordinator = 1,

        /// <summary>Manage coordinators and workers. Cannot manage owners.</summary>
        Admin = 2,

        /// <summary>Full control. Manage everything including other owners.</summary>
        Owner = 3,
    }

    /// <summary>
    /// A signed role assignment: "Identity X grants Role Y to Peer Z."
    /// </summary>
    public class RoleAssignment
    {
        /// <summary>The peer ID receiving the role.</summary>
        public string PeerId { get; set; } = "";

        /// <summary>The public key (SPKI, base64) of the peer receiving the role.</summary>
        public string PeerPublicKey { get; set; } = "";

        /// <summary>The granted role.</summary>
        public SwarmRole Role { get; set; }

        /// <summary>The public key (SPKI, base64) of the granter.</summary>
        public string GranterPublicKey { get; set; } = "";

        /// <summary>UTC ticks when this assignment was created.</summary>
        public long Timestamp { get; set; }

        /// <summary>Optional expiration (UTC ticks). Null = no expiration.</summary>
        public long? ExpiresAt { get; set; }

        /// <summary>Ed25519 signature of this assignment (base64, 64 bytes). Excludes this field when signing.</summary>
        public string Signature { get; set; } = "";

        /// <summary>
        /// Creates a signed role assignment.
        /// </summary>
        public static async Task<RoleAssignment> CreateAsync(
            SwarmIdentity granter,
            string peerId,
            byte[] peerPublicKeySpki,
            SwarmRole role,
            long? expiresAt = null)
        {
            var assignment = new RoleAssignment
            {
                PeerId = peerId,
                PeerPublicKey = Convert.ToBase64String(peerPublicKeySpki),
                Role = role,
                GranterPublicKey = Convert.ToBase64String(granter.PublicKeySpki),
                Timestamp = DateTimeOffset.UtcNow.Ticks,
                ExpiresAt = expiresAt,
            };

            var sigBytes = await granter.SignObjectAsync(new
            {
                assignment.PeerId,
                assignment.PeerPublicKey,
                assignment.Role,
                assignment.GranterPublicKey,
                assignment.Timestamp,
                assignment.ExpiresAt,
            });
            assignment.Signature = Convert.ToBase64String(sigBytes);
            return assignment;
        }

        /// <summary>
        /// Verifies this assignment's signature against the granter's public key.
        /// </summary>
        public async Task<bool> VerifyAsync(IPortableCrypto crypto)
        {
            var granterKey = Convert.FromBase64String(GranterPublicKey);
            var sigBytes = Convert.FromBase64String(Signature);
            var dataBytes = JsonSerializer.SerializeToUtf8Bytes(new
            {
                PeerId,
                PeerPublicKey,
                Role,
                GranterPublicKey,
                Timestamp,
                ExpiresAt,
            });
            return await SwarmIdentity.VerifyAsync(crypto, granterKey, dataBytes, sigBytes);
        }

        /// <summary>
        /// Checks if this assignment has expired.
        /// </summary>
        public bool IsExpired => ExpiresAt.HasValue &&
            DateTimeOffset.UtcNow.Ticks > ExpiresAt.Value;
    }

    /// <summary>
    /// An authorized key in the swarm's key registry.
    /// </summary>
    public class AuthorizedKey
    {
        /// <summary>The public key in SPKI format (base64).</summary>
        public string PublicKey { get; set; } = "";

        /// <summary>The role granted to this key.</summary>
        public SwarmRole Role { get; set; }

        /// <summary>Human-readable label.</summary>
        public string Label { get; set; } = "";

        /// <summary>UTC ticks when this key was added.</summary>
        public long AddedAt { get; set; }
    }

    /// <summary>
    /// A revoked key in the swarm's key registry.
    /// </summary>
    public class RevokedKey
    {
        /// <summary>The revoked public key in SPKI format (base64).</summary>
        public string PublicKey { get; set; } = "";

        /// <summary>Reason for revocation.</summary>
        public string Reason { get; set; } = "";

        /// <summary>UTC ticks when revoked.</summary>
        public long RevokedAt { get; set; }
    }

    /// <summary>
    /// Owner-signed registry of authorized keys and revocations.
    /// Published via BEP 46. All peers cache and verify against this.
    /// </summary>
    public class KeyRegistry
    {
        /// <summary>Authorized keys and their roles.</summary>
        public List<AuthorizedKey> Keys { get; set; } = new();

        /// <summary>Revoked keys.</summary>
        public List<RevokedKey> Revocations { get; set; } = new();

        /// <summary>Monotonic sequence number (prevents replay).</summary>
        public long Sequence { get; set; }

        /// <summary>Owner's signature of this registry (base64). Excludes this field when signing.</summary>
        public string OwnerSignature { get; set; } = "";

        /// <summary>
        /// Checks if a public key has a given minimum role.
        /// </summary>
        /// <param name="publicKeyBase64">The public key to check (base64 SPKI).</param>
        /// <param name="minimumRole">The minimum required role.</param>
        /// <returns>True if the key has at least the minimum role and is not revoked.</returns>
        public bool HasRole(string publicKeyBase64, SwarmRole minimumRole)
        {
            if (IsRevoked(publicKeyBase64)) return false;
            var key = Keys.FirstOrDefault(k => k.PublicKey == publicKeyBase64);
            return key != null && key.Role >= minimumRole;
        }

        /// <summary>
        /// Checks if a public key has been revoked.
        /// </summary>
        public bool IsRevoked(string publicKeyBase64) =>
            Revocations.Any(r => r.PublicKey == publicKeyBase64);

        /// <summary>
        /// Signs this registry with the owner's identity.
        /// </summary>
        public async Task SignAsync(SwarmIdentity owner)
        {
            var sigBytes = await owner.SignObjectAsync(new
            {
                Keys,
                Revocations,
                Sequence,
            });
            OwnerSignature = Convert.ToBase64String(sigBytes);
        }

        /// <summary>
        /// Verifies this registry's signature against the owner's public key.
        /// </summary>
        /// <param name="crypto">The crypto provider.</param>
        /// <param name="ownerPublicKeySpki">The owner's public key (SPKI bytes).</param>
        public async Task<bool> VerifyAsync(IPortableCrypto crypto,
            byte[] ownerPublicKeySpki)
        {
            var sigBytes = Convert.FromBase64String(OwnerSignature);
            var dataBytes = JsonSerializer.SerializeToUtf8Bytes(new
            {
                Keys,
                Revocations,
                Sequence,
            });
            return await SwarmIdentity.VerifyAsync(crypto, ownerPublicKeySpki,
                dataBytes, sigBytes);
        }

        /// <summary>
        /// Adds an authorized key to the registry.
        /// Does not re-sign — call SignAsync after modifications.
        /// </summary>
        public void AddKey(byte[] publicKeySpki, SwarmRole role, string label = "")
        {
            var base64 = Convert.ToBase64String(publicKeySpki);
            if (Keys.Any(k => k.PublicKey == base64)) return; // already exists

            Keys.Add(new AuthorizedKey
            {
                PublicKey = base64,
                Role = role,
                Label = label,
                AddedAt = DateTimeOffset.UtcNow.Ticks,
            });
            Sequence++;
        }

        /// <summary>
        /// Revokes a key. Cannot revoke the last owner.
        /// Does not re-sign — call SignAsync after modifications.
        /// </summary>
        /// <returns>True if revoked, false if it was the last owner.</returns>
        public bool RevokeKey(byte[] publicKeySpki, string reason = "")
        {
            var base64 = Convert.ToBase64String(publicKeySpki);

            // Prevent removing the last owner
            var ownerCount = Keys.Count(k => k.Role == SwarmRole.Owner &&
                !IsRevoked(k.PublicKey));
            var isOwner = Keys.Any(k => k.PublicKey == base64 &&
                k.Role == SwarmRole.Owner);
            if (isOwner && ownerCount <= 1) return false;

            Revocations.Add(new RevokedKey
            {
                PublicKey = base64,
                Reason = reason,
                RevokedAt = DateTimeOffset.UtcNow.Ticks,
            });
            Sequence++;
            return true;
        }
    }
}
