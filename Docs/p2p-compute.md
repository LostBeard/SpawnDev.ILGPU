# P2P Distributed GPU Compute

> **Status: in development, on hold.** The P2P backend has not been published to NuGet and its tests are switched off in the demo while the six core backends take priority. This page describes the design and the in-repo API ([SpawnDev.ILGPU.P2P](../SpawnDev.ILGPU.P2P)); reference the project directly to try it.

**AcceleratorType.P2P** — the 7th ILGPU backend. Distributes GPU kernels across connected devices via WebRTC. Create a compute swarm, share a link or QR code, and anyone can contribute GPU power — from any device with a browser.

## Overview

The P2P backend turns any collection of devices into a distributed GPU cluster:

1. **Create a swarm** — One device creates a compute swarm and becomes the coordinator
2. **Share the link** — QR code, URL, or magnet link. Anyone with the link can join
3. **Peers connect** — WebRTC data channels via tracker signaling at `hub.spawndev.com`
4. **Dispatch kernels** — Coordinator sends kernel code + buffer data to peers
5. **Peers execute** — Each peer compiles and runs the kernel on their local GPU
6. **Results return** — Computed data flows back to the coordinator

The same C# kernel code runs on 1 GPU or 100 GPUs — transparently.

## Installation

```bash
dotnet add package SpawnDev.ILGPU
# SpawnDev.ILGPU.P2P is not on NuGet yet: add a ProjectReference to SpawnDev.ILGPU.P2P/SpawnDev.ILGPU.P2P.csproj
```

## Quick Start

### Create a Swarm (Coordinator)

```csharp
using SpawnDev.SpawnJS.Cryptography;
using SpawnDev.ILGPU.P2P;
using SpawnDev.WebTorrent;

// DI: IPortableCrypto and WebTorrentClient should be injected
var compute = await P2PCompute.CreateSwarmAsync(crypto, client, "My Compute Swarm");

// Share the join link — anyone with this URL can join
Console.WriteLine($"Join link: {compute.JoinLink}");
Console.WriteLine($"Magnet: {compute.MagnetLink}");
Console.WriteLine($"Peers: {compute.PeerCount}");
```

### Join a Swarm (Worker)

```csharp
// Create the best local accelerator for executing kernels
var builder = Context.Create();
await builder.AllAcceleratorsAsync();
var context = builder.ToContext();
var accelerator = await context.CreatePreferredAcceleratorAsync();

// Join the swarm
var compute = await P2PCompute.JoinSwarmAsync(crypto, client, accelerator, joinLink);
```

### Dispatch a Kernel

```csharp
// From the coordinator, dispatch work to the swarm
compute.DispatchToSwarm(typeof(MyKernels), "VectorAdd", 1024,
    ("a", aData, 4), ("b", bData, 4), ("result", null, 4));
```

## Architecture

### Role Hierarchy

```
Owner (cryptographic identity — YubiKey, passkey, Ed25519 key)
  |
  +-- Can join from ANY device with their key
  +-- Can promote/demote coordinators, admins
  +-- Can add/revoke keys (multi-owner orgs)
  +-- Can kick/block peers
  +-- Authority lives in the KEY, not the device
  |
Admin (delegated by owner)
  +-- Can manage coordinators and workers
  +-- Cannot add/remove owners
  |
Coordinator (dispatches work)
  +-- Elected among peers, or assigned by owner
  +-- Dispatches kernels, manages peer scoring
  |
Worker (default role)
  +-- Executes kernels, reports results
```

### Components

| Component | Purpose |
|-----------|---------|
| `P2PCompute` | High-level API for creating/joining swarms |
| `P2PSwarmCoordinator` | Manages swarm state, peer connections, RBAC |
| `P2PDispatcher` | Routes kernel dispatch to healthy peers with fault tolerance |
| `P2PWorker` | Receives and executes kernels on local GPU |
| `P2PTransport` | Message routing over WebRTC via sd_compute extension |
| `P2PWebRtcBridge` | Wires WebTorrent peer connections to compute messages |
| `P2PKernelLauncher` | Reflection-based kernel compilation and async execution |
| `P2PKernelSerializer` | Secure kernel method resolution (allowlist-based) |
| `SwarmIdentity` | Ed25519 key pair for cryptographic identity |
| `KeyRegistry` | Owner-signed list of authorized keys and roles |
| `HardwareKeyProvider` | WebAuthn/FIDO2 for YubiKey/passkey hardware-backed ownership |
| `ComputeBoardClient` | Post/browse compute requests on hub.spawndev.com |

### Message Flow

```
Coordinator                          Worker
    |                                    |
    |-- KernelDispatch (signed) -------->|
    |   (method, gridDim, buffers)       |
    |                                    |-- Compile kernel
    |                                    |-- Allocate GPU buffers
    |                                    |-- Execute kernel
    |                                    |-- await SynchronizeAsync()
    |                                    |-- CopyToHostAsync results
    |<-- KernelResult -------------------|
    |   (success, duration, buffers)     |
```

All authority messages (Kick, Block, Transfer, RoleAssign, RegistryUpdate, KernelDispatch) are signed with the sender's Ed25519 key and verified by the recipient against the KeyRegistry.

## Security

### Cryptographic Ownership

Swarm ownership is a cryptographic identity, not a device. A YubiKey holder IS the owner regardless of which device they use.

```csharp
// Register a hardware key as swarm owner
var provider = new HardwareKeyProvider();
var credential = await provider.RegisterAsync("My YubiKey");

// Create identity from hardware credential
var identity = await SwarmIdentity.FromHardwareKeyAsync(crypto, credential);

// Add to KeyRegistry as Owner
registry.AddKey(identity.PublicKeySpki, SwarmRole.Owner, "My YubiKey");
await registry.SignAsync(identity);
```

### Key Management

- **Last-owner protection** — Cannot remove the last owner key
- **Key rotation** — Add new key, revoke old one in a single signed update
- **Offline owner** — Swarm continues under delegated authority
- **Compromised key** — Revoke via any other owner key
- **Replay protection** — Monotonic sequence numbers on KeyRegistry

### Message Signing

Every authority message includes:
- `SenderPublicKey` — base64 SPKI
- `SenderFingerprint` — SHA-256 hash of the public key
- `Signature` — Ed25519 signature over the message payload

Workers verify the coordinator's authority before executing any kernel.

## P2P Demo

The [Compute Swarm demo](https://lostbeard.github.io/SpawnDev.ILGPU/compute) demonstrates the full P2P pipeline:

- **Create Swarm** — Generate a compute swarm with one click
- **QR Code** — Scannable QR code for mobile devices to join
- **Camera Scanner** — Scan a QR code to join a swarm
- **Peer Management** — Kick, block, approve/reject peers
- **Hardware Keys** — Register YubiKey/passkey as swarm owner
- **Key Management** — Assign roles, revoke keys, view registry
- **Swarm Settings** — Join mode (open/approval/invite-only), max peers

## DI Registration

```csharp
// Program.cs — register platform crypto and WebTorrent
builder.Services.AddSpawnJSRuntime();
builder.Services.AddPlatformCrypto();
builder.Services.AddSingleton<WebTorrentClient>();

// Inject in Blazor components
@inject IPortableCrypto Crypto
@inject WebTorrentClient WebTorrentClient
```

## QR Code Support

The `SpawnDev.ILGPU.QR` namespace provides GPU-accelerated QR code generation and decoding:

```csharp
// Generate a QR code (CPU)
var (pixels, w, h) = QRCode.Generate("https://hub.spawndev.com/compute/join?compute=abc123");

// GPU-accelerated render
var (pixels, w, h) = await QRCode.GenerateAsync(accelerator, text);

// With logo overlay (auto EC level H)
var (pixels, w, h) = await QRCode.GenerateWithLogoAsync(accelerator, text, logo, logoW, logoH);

// Decode a QR code from camera frame
var decoded = QRDecoder.Decode(framePixels, width, height);
```

## API Reference

### P2PCompute

| Method | Description |
|--------|-------------|
| `CreateSwarmAsync(crypto, client, name)` | Create a new swarm as coordinator |
| `JoinSwarmAsync(crypto, client, accelerator, magnetOrLink)` | Join an existing swarm as worker |
| `DispatchToSwarm(kernelType, method, gridDim, buffers)` | Dispatch kernel to the swarm |
| `PeerCount` | Number of connected peers |
| `TotalTflops` | Aggregate compute power |
| `MagnetLink` | Magnet URI for peer-to-peer joining |
| `JoinLink` | HTTP URL for browser joining |

### HardwareKeyProvider

| Method | Description |
|--------|-------------|
| `RegisterAsync(label)` | Register a new hardware key (YubiKey tap) |
| `AuthenticateAsync(allowedCredentials)` | Authenticate with an existing key |
| `VerifyAssertionAsync(assertion, publicKey, crypto)` | Verify a WebAuthn assertion |
| `IsAvailable` | True if WebAuthn is supported |
| `IsPlatformAuthenticatorAvailable()` | True if Windows Hello/Touch ID available |

### GpuTestVerify

| Method | Description |
|--------|-------------|
| `VerifyDescendingSort(accel, keys, values, n)` | GPU sort verification |
| `VerifyAscendingSort(accel, keys, values, n)` | GPU sort verification |
| `CompareBuffers(accel, actual, expected, n)` | GPU float comparison |

### QRCode

| Method | Description |
|--------|-------------|
| `Generate(text, ecLevel, moduleSize)` | CPU encode + render |
| `GenerateAsync(accel, text, ecLevel, moduleSize)` | GPU-accelerated render |
| `GenerateWithLogoAsync(accel, text, logo, w, h)` | With logo overlay (EC=H) |
| `Decode(pixels, width, height)` | CPU decode from RGBA pixels |
| `DecodeAsync(accel, pixels, width, height)` | GPU-accelerated decode |
