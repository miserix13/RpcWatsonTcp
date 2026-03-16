# RpcWatsonTcp

A .NET RPC framework built on [WatsonTcp](https://github.com/jchristn/WatsonTcp) and [Nerdbank.MessagePack](https://github.com/AArnott/Nerdbank.MessagePack). It lets you define strongly-typed request/reply pairs, implement handlers for them, and call them from a client over TCP — with errors propagated cleanly back to the caller, optional Polly resilience pipelines, and opt-in durable delivery via a Stellar.FastDB outbox.

---

## Features

- **Strongly-typed RPC** — define your messages as plain C# classes; the framework handles routing and serialization
- **Handler-per-request pattern** — one `IHandler<TRequest, TReply>` implementation per operation
- **Structured error propagation** — server-side exceptions are wrapped and sent back as `RpcErrorReply`; the client receives an `RpcException`
- **Concurrent in-flight requests** — the client correlates replies to pending calls using a per-message GUID; multiple requests can be in flight simultaneously
- **DI-first** — integrates with `Microsoft.Extensions.DependencyInjection`
- **Efficient serialization** — [Nerdbank.MessagePack](https://github.com/AArnott/Nerdbank.MessagePack) (binary MessagePack format) via compile-time PolyType source generation
- **Authentication** *(opt-in)* — define any credential type with `[GenerateShape]`, implement `IRpcAuthenticator<TCredential>`, register with `AddRpcAuthentication<TCredential, TAuthenticator>()`; the client sends credentials over the application layer immediately after connecting; `SendAsync` gates on the handshake automatically
- **Polly resilience** *(opt-in)* — attach any [Polly v8](https://github.com/App-vNext/Polly) `ResiliencePipeline` (retry, circuit breaker, timeout) to `RpcClientOptions`
- **Durable outbox** *(opt-in)* — `DurableRpcClient` persists requests to a [Stellar.FastDB](https://github.com/stonstad/Stellar.FastDB) outbox before sending; entries are removed on success and replayed on reconnect for at-least-once delivery
- **TLS** *(opt-in)* — set `RpcServerOptions.Tls` and `RpcClientOptions.Tls` to encrypt the transport; supports TLS 1.2 and TLS 1.3, server-only or mutual TLS (mTLS), and custom certificate validation callbacks
- **Protobuf IDL source generator** *(opt-in)* — write `.proto` files; the `RpcWatsonTcp.SourceGenerator` Roslyn generator emits `[GenerateShape]` partial classes with `IRequest`/`IReply` by name convention; MessagePack remains the wire format

---

## Requirements

- .NET 10.0
- Request and reply types must be `partial` and annotated with `[GenerateShape]` from PolyType

---

## Quick Start

### 1. Define your messages

```csharp
using PolyType;
using RpcWatsonTcp;

[GenerateShape]
public partial class GetUserRequest : IRequest
{
    public int UserId { get; set; }
}

[GenerateShape]
public partial class GetUserReply : IReply
{
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
}
```

### 2. Implement a handler

```csharp
public class GetUserHandler : IHandler<GetUserRequest, GetUserReply>
{
    public Task<GetUserReply> HandleAsync(GetUserRequest request, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new GetUserReply
        {
            Name = "Alice",
            Email = "alice@example.com"
        });
    }
}
```

### 3. Start the server

```csharp
using Microsoft.Extensions.DependencyInjection;
using RpcWatsonTcp;

var services = new ServiceCollection();
services.AddRpcServer(opt =>
{
    opt.IpAddress = "0.0.0.0";
    opt.Port = 9000;
});
services.AddRpcHandler<GetUserRequest, GetUserReply, GetUserHandler>();

IServiceProvider sp = services.BuildServiceProvider();
sp.ApplyRpcHandlerRegistrations();

var server = sp.GetRequiredService<RpcServer>();
server.Start();
```

### 4. Call from a client

```csharp
using RpcWatsonTcp;

var client = new RpcClient(new RpcClientOptions
{
    ServerIpAddress = "127.0.0.1",
    ServerPort = 9000
});
client.Connect();

GetUserReply reply = await client.SendAsync<GetUserRequest, GetUserReply>(
    new GetUserRequest { UserId = 42 });

Console.WriteLine(reply.Name); // Alice
```

---

## Error Handling

When a handler throws, the exception is caught by the server and returned to the client as an `RpcException`. The original exception message and type name are preserved.

```csharp
try
{
    var reply = await client.SendAsync<GetUserRequest, GetUserReply>(request);
}
catch (RpcException ex)
{
    Console.WriteLine(ex.Message);             // server exception message
    Console.WriteLine(ex.RemoteExceptionType); // e.g. "System.InvalidOperationException"
}
```

---

## Authentication

RpcWatsonTcp provides a fully application-layer authentication extensibility point using generic credential types. This bypasses WatsonTcp's built-in preshared-key mechanism entirely, giving you complete control over credential shape and validation logic.

When authentication is configured, the client sends credentials immediately after the TCP connection is established. The server validates them and replies before any RPC calls are processed. `SendAsync` automatically gates on this handshake — no ordering concerns at the call site.

### 1. Define a credential type

```csharp
using PolyType;
using RpcWatsonTcp;

[GenerateShape]
public partial class ApiKeyCredential : ICredential
{
    public string ApiKey { get; set; } = string.Empty;
}
```

The type must implement `ICredential` and be annotated with `[GenerateShape]` (same as request/reply types).

### 2. Implement a server-side authenticator

```csharp
public class ApiKeyAuthenticator : IRpcAuthenticator<ApiKeyCredential>
{
    public Task<bool> AuthenticateAsync(ApiKeyCredential credential, CancellationToken ct = default)
        => Task.FromResult(credential.ApiKey == "my-secret-key");
}
```

### 3. Register on the server

```csharp
services.AddRpcServer(opt => { opt.IpAddress = "0.0.0.0"; opt.Port = 9000; });
services.AddRpcAuthentication<ApiKeyCredential, ApiKeyAuthenticator>();
services.AddRpcHandler<GetUserRequest, GetUserReply, GetUserHandler>();
```

When `AddRpcAuthentication` is called, any client that does not successfully authenticate will receive an `RpcException` for every RPC call it attempts.

### 4. Configure the client

```csharp
var client = new RpcClient(new RpcClientOptions
{
    ServerIpAddress = "127.0.0.1",
    ServerPort = 9000,
    CredentialProvider = new CredentialProvider<ApiKeyCredential>(
        () => new ApiKeyCredential { ApiKey = "my-secret-key" })
});

// ConnectAsync sends credentials and awaits server confirmation before returning.
await client.ConnectAsync();

// Safe to call — authentication has already completed.
var reply = await client.SendAsync<GetUserRequest, GetUserReply>(new GetUserRequest { UserId = 1 });
```

`Connect()` (non-async) also works — `SendAsync` automatically waits for the handshake before sending:

```csharp
client.Connect();
// SendAsync will block internally until the auth reply arrives.
var reply = await client.SendAsync<GetUserRequest, GetUserReply>(new GetUserRequest { UserId = 1 });
```

### Authentication events

`RpcServer` exposes four events:

| Event | Type | Raised when |
|---|---|---|
| `ClientConnected` | `RpcClientConnectedEventArgs` | A TCP connection is established (before auth completes) |
| `ClientDisconnected` | `RpcClientDisconnectedEventArgs` | A client disconnects |
| `AuthenticationSucceeded` | `RpcAuthenticationSucceededEventArgs` | A client's credentials were accepted |
| `AuthenticationFailed` | `RpcAuthenticationFailedEventArgs` | A client's credentials were rejected |

`RpcClient` exposes two events:

| Event | Raised when |
|---|---|
| `AuthenticationSucceeded` | The server accepted credentials |
| `AuthenticationFailed` | The server rejected credentials |

```csharp
server.AuthenticationSucceeded += (_, e) =>
    Console.WriteLine($"Client {e.IpPort} authenticated ({e.ClientGuid})");

server.AuthenticationFailed += (_, e) =>
    Console.WriteLine($"Auth failure from {e.IpPort}");
```

Leave `CredentialProvider` as `null` (the default) and omit `AddRpcAuthentication` to allow unauthenticated connections.

---

## Resilience with Polly

Attach any Polly v8 `ResiliencePipeline` to `RpcClientOptions.ResiliencePipeline`. Every `SendAsync` call is then wrapped in that pipeline, enabling automatic retries, circuit breakers, and timeouts.

```csharp
using Polly;
using Polly.Retry;

var pipeline = new ResiliencePipelineBuilder()
    .AddRetry(new RetryStrategyOptions
    {
        MaxRetryAttempts = 3,
        Delay = TimeSpan.FromMilliseconds(200),
        ShouldHandle = new PredicateBuilder().Handle<RpcException>()
    })
    .AddTimeout(TimeSpan.FromSeconds(5))
    .Build();

var client = new RpcClient(new RpcClientOptions
{
    ServerIpAddress = "127.0.0.1",
    ServerPort = 9000,
    ResiliencePipeline = pipeline   // attach here
});
client.Connect();
```

The pipeline is **opt-in** — when `ResiliencePipeline` is `null` (the default), requests are sent without any wrapping.

---

## Durable Delivery

`DurableRpcClient` adds an at-least-once outbox on top of a regular `RpcClient`. Requests sent with `SendOptions.Durable` are written to a [Stellar.FastDB](https://github.com/stonstad/Stellar.FastDB) file before transmission and removed only after a successful reply. On startup, any entries left in the outbox (from a previous crash or disconnect) are replayed.

> **Idempotency note:** Durable delivery is at-least-once — a server handler may receive the same request more than once if the client crashed after the server processed it but before the outbox entry was deleted. Design handlers to be idempotent where possible.

### Direct construction

```csharp
using RpcWatsonTcp;

await using var durableClient = new DurableRpcClient(
    inner: new RpcClient(new RpcClientOptions { ServerIpAddress = "127.0.0.1", ServerPort = 9000 }),
    options: new DurableRpcClientOptions
    {
        OutboxPath = "my_service_outbox",   // directory that will hold the FastDB files
        DrainOnConnect = true               // replay persisted messages on startup
    });

await durableClient.ConnectAsync();

// Non-durable call — same behaviour as RpcClient.SendAsync
GetUserReply reply1 = await durableClient.SendAsync<GetUserRequest, GetUserReply>(request);

// Durable call — persisted before send, deleted on success
GetUserReply reply2 = await durableClient.SendAsync<GetUserRequest, GetUserReply>(
    request, SendOptions.Durable);
```

### Dependency injection

```csharp
services.AddDurableRpcClient(
    configureClient: opt =>
    {
        opt.ServerIpAddress = "127.0.0.1";
        opt.ServerPort = 9000;
    },
    configureDurable: opt =>
    {
        opt.OutboxPath = "my_service_outbox";
        opt.DrainOnConnect = true;
    });
```

### Manual drain

```csharp
// Replay all pending outbox entries after re-establishing a connection
await durableClient.DrainOutboxAsync();
```

---

## Architecture

```
RpcClient.ConnectAsync()  (when CredentialProvider is set)
  ↓  WatsonTcpClient connects
  ↓  sends RpcEnvelope { IsAuth=true, TypeName=credentialType, Payload=serialized credential }
  ↑  server calls IRpcAuthenticator<TCredential>.AuthenticateAsync(credential)
  ↑  replies RpcEnvelope { IsAuth=true, IsError=false/true }
  ↓  client _authReady task completes → AuthenticationSucceeded/Failed event fires

RpcClient.SendAsync<TRequest, TReply>(request)
  ↓  awaits _authReady (no-op when no credentials configured)
  ↓  [optional] ResiliencePipeline wraps the call
  ↓  serializes RpcEnvelope { MessageId, TypeName, Payload }
  ↓  WatsonTcpClient ──── TCP ────► WatsonTcpServer
                                         ↓ RpcServer checks client auth state
                                         ↓ dispatches by TypeName
                                   HandlerDispatcher<TRequest, TReply>
                                         ↓ resolves IHandler via IServiceProvider
                                   IHandler<TRequest, TReply>.HandleAsync(request)
                                         ↓ reply or RpcErrorReply on exception
  ↑  WatsonTcpClient ◄─── TCP ───── WatsonTcpServer
  ↓  completes TaskCompletionSource keyed by MessageId
returns TReply  (or throws RpcException)


DurableRpcClient.SendAsync(..., SendOptions.Durable)
  ↓  persist RpcEnvelope → Stellar.FastDB outbox
  ↓  delegate to RpcClient.SendAsync  (pipeline applies here too)
  ↓  on success → delete outbox entry
  ↑  on failure → entry stays; DrainOutboxAsync replays on reconnect
```

Every message on the wire is an `RpcEnvelope` — a MessagePack-serialized wrapper containing:

| Field | Type | Purpose |
|---|---|---|
| `MessageId` | `Guid` | Correlates a reply to its pending request |
| `TypeName` | `string` | Assembly-qualified request/reply/credential type name for deserialization |
| `Payload` | `byte[]` | MessagePack-serialized request, reply, or credential |
| `IsError` | `bool` | `true` when Payload contains an `RpcErrorReply` |
| `IsAuth` | `bool` | `true` when this envelope is part of the authentication handshake |

---

## TLS

RpcWatsonTcp supports encrypted connections via TLS 1.2 and TLS 1.3. TLS is configured independently on the server (a certificate is required) and the client (certificate validation behaviour). Plain TCP is used by default when no TLS options are set.

### Server: require TLS

```csharp
services.AddRpcServer(opt =>
{
    opt.IpAddress = "0.0.0.0";
    opt.Port = 9000;
    opt.Tls = new RpcServerTlsOptions
    {
        PfxPath     = "/certs/server.pfx",
        PfxPassword = "secret",
        TlsVersion  = RpcTlsVersion.Tls12,   // or Tls13
    };
});
```

Or supply an `X509Certificate2` directly:

```csharp
opt.Tls = new RpcServerTlsOptions { Certificate = myCert };
```

### Client: connect over TLS

```csharp
var client = new RpcClient(new RpcClientOptions
{
    ServerIpAddress = "myserver.internal",
    ServerPort = 9000,
    Tls = new RpcClientTlsOptions
    {
        // Validate the server certificate against the system trust store (default):
        AcceptAnyCertificate = false,

        // Or provide a custom validation callback:
        // ServerCertificateValidation = (_, cert, chain, errors) => errors == SslPolicyErrors.None,
    }
});
```

> **Development shortcut** — set `AcceptAnyCertificate = true` to skip server certificate validation. **Never use this in production.**

### Mutual TLS (mTLS)

To require clients to present a certificate, set `RequireClientCertificate` on the server and provide a client certificate on the client:

```csharp
// Server
opt.Tls = new RpcServerTlsOptions
{
    Certificate             = serverCert,
    RequireClientCertificate = true,
    // Optional custom validation:
    ClientCertificateValidation = (_, cert, chain, errors) => MyValidate(cert),
};

// Client
opt.Tls = new RpcClientTlsOptions
{
    Certificate          = clientCert,   // the client's own certificate
    AcceptAnyCertificate = false,
};
```

### TLS + authentication

TLS and application-layer authentication compose naturally — configure both:

```csharp
// Server
services.AddRpcServer(opt => { opt.Tls = ...; });
services.AddRpcAuthentication<ApiKeyCredential, ApiKeyAuthenticator>();

// Client
var client = new RpcClient(new RpcClientOptions
{
    Tls = new RpcClientTlsOptions { AcceptAnyCertificate = false },
    CredentialProvider = new CredentialProvider<ApiKeyCredential>(
        () => new ApiKeyCredential { Key = Environment.GetEnvironmentVariable("API_KEY") })
});
await client.ConnectAsync(); // establishes TLS, then sends credentials
```

---

## IDL-first Workflow (Protobuf Source Generator)

The `RpcWatsonTcp.SourceGenerator` package provides a Roslyn incremental source generator that reads `.proto` files and emits the C# message classes for you — no hand-written boilerplate required.

> **Note:** Protobuf is used **only as an IDL** here. The wire serialization remains MessagePack; `.proto` files are never used at runtime.

### Setup

Add the source generator as an analyzer reference in your project file and declare your `.proto` files as `AdditionalFiles`:

```xml
<ItemGroup>
  <!-- Mark proto files so the generator can read them -->
  <AdditionalFiles Include="**/*.proto" />
</ItemGroup>

<ItemGroup>
  <!-- Reference the generator — ReferenceOutputAssembly=false means it's build-only -->
  <ProjectReference Include="..\RpcWatsonTcp.SourceGenerator\RpcWatsonTcp.SourceGenerator.csproj"
                    OutputItemType="Analyzer"
                    ReferenceOutputAssembly="false" />
</ItemGroup>

<!-- Optional: override the namespace for all generated types (defaults to RpcWatsonTcp.Generated) -->
<PropertyGroup>
  <RpcProtoNamespace>MyApp.Messages</RpcProtoNamespace>
</PropertyGroup>
```

### Write a `.proto` file

```proto
syntax = "proto3";
package users;

message GetUserRequest {
  string user_id = 1;
  int32  version = 2;
}

message GetUserReply {
  string name    = 1;
  string email   = 2;
  repeated string roles = 3;
}

enum UserStatus {
  UNKNOWN  = 0;
  ACTIVE   = 1;
  INACTIVE = 2;
}
```

### What gets generated

```csharp
// <auto-generated/>
#nullable enable
using System.Collections.Generic;
using PolyType;
using RpcWatsonTcp;

namespace MyApp.Messages
{
    [GenerateShape]
    public partial class GetUserRequest : IRequest
    {
        public string UserId { get; set; } = string.Empty;
        public int Version { get; set; } = 0;
    }

    [GenerateShape]
    public partial class GetUserReply : IReply
    {
        public string Name { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public List<string> Roles { get; set; } = new List<string>();
    }

    public enum UserStatus
    {
        Unknown  = 0,
        Active   = 1,
        Inactive = 2,
    }
}
```

### Role convention

The RPC interface a class implements is determined by the message name suffix:

| Suffix | Implements | Example |
|---|---|---|
| `Request` | `IRequest` | `GetUserRequest` |
| `Reply` or `Response` | `IReply` | `GetUserReply`, `CreateUserResponse` |
| *(anything else)* | neither | `Address`, `PageInfo` |

### Field type mapping

| Proto3 type | C# type | Default value |
|---|---|---|
| `string` | `string` | `string.Empty` |
| `int32` | `int` | `0` |
| `int64` | `long` | `0` |
| `uint32` | `uint` | `0` |
| `uint64` | `ulong` | `0` |
| `float` | `float` | `0f` |
| `double` | `double` | `0.0` |
| `bool` | `bool` | `false` |
| `bytes` | `byte[]` | `System.Array.Empty<byte>()` |
| enum type | enum name | `0` |
| message type | class name (nullable) | `null!` |
| `repeated T` | `List<T>` | `new List<T>()` |

### Proto3 subset supported (v1)

✅ Scalars, enums, nested messages, nested enums, `repeated` fields, `optional`/`required` annotations (ignored), line and block comments, `package` declaration

❌ Not yet: `oneof`, `map<K,V>`, `import`, `extend`, service definitions, custom options

### Notes

> **Using generated types in RPC calls:** Roslyn does not allow one source generator to see output from another in the same compilation pass. This means PolyType's `[GenerateShape]` processor does not produce the `IShapeable<T>` implementation for generator-emitted classes, so you cannot directly pass them to `SendAsync` or `AddRpcHandler`.
>
> **Workaround:** add a hand-written `partial` declaration alongside the generated file to satisfy the constraint:
>
> ```csharp
> // MyMessages.cs  — sits next to the .proto file in your project
> using PolyType;
>
> namespace MyApp.Messages;
>
> // These empty partial declarations let PolyType emit IShapeable<T>
> [GenerateShape] public partial class GetUserRequest;
> [GenerateShape] public partial class GetUserReply;
> ```
>
> The generator emits the properties and RPC interfaces; your partial adds the `[GenerateShape]` trigger that PolyType processes. Everything else (field generation, naming conventions) is still handled automatically.

---

## Performance

Benchmarks run on a loopback connection using [BenchmarkDotNet](https://benchmarkdotnet.org/) in Release mode. Source is in `RpcWatsonTcp.Benchmarks/`.

```bash
dotnet run --project RpcWatsonTcp.Benchmarks -c Release -- --filter "*"
```

### Round-trip latency (single client ↔ server, loopback)

| Payload | Mean | Error |
|---|--:|--:|
| Small (16 B) | 1,716 µs | ±123 µs |
| Medium (256 B) | 1,718 µs | ±86 µs |
| Large (4 KB) | 1,870 µs | ±251 µs |

Latency is dominated by TCP round-trip overhead. Payload size has a negligible effect.

### TLS overhead (TLS 1.2, post-handshake, small payload)

| Scenario | Mean | Error | vs. baseline |
|---|--:|--:|--:|
| Plain TCP (baseline) | 1,721 µs | ±98 µs | — |
| TLS 1.2 | 1,548 µs | ±52 µs | within noise |

TLS overhead on an **already-established** connection is indistinguishable from measurement noise. The one-time TLS handshake cost (certificate exchange + key derivation) occurs only at `Connect()` time.

### Authentication overhead

| Scenario | Mean | Error | vs. baseline |
|---|--:|--:|--:|
| No auth (baseline) | 1,552 µs | ±78 µs | — |
| With auth, post-handshake | 1,538 µs | ±66 µs | within noise |

The auth gate (`_authReady` task) adds negligible per-request overhead once the one-time credential handshake is complete at connect time.

### Serializer throughput (Nerdbank.MessagePack, single-threaded)

| Operation | Mean | Error |
|---|--:|--:|
| Serialize small request | 1.4 µs | ±0.05 µs |
| Serialize large request (4 KB) | 2.2 µs | ±0.07 µs |
| Serialize RpcEnvelope | 2.4 µs | ±0.08 µs |
| Deserialize small request | 2.1 µs | ±0.04 µs |
| Deserialize large request (4 KB) | 3.7 µs | ±0.53 µs |
| Deserialize RpcEnvelope | 4.0 µs | ±0.24 µs |

### Concurrent throughput (N simultaneous round-trips)

| Concurrency | Total time | Mean per request |
|--:|--:|--:|
| 1 | 1,759 µs | 1,759 µs |
| 10 | 13,615 µs | ~1,362 µs |
| 50 | 59,085 µs | ~1,182 µs |

Throughput-per-request improves under concurrency as TCP round-trips overlap.

---

## Building & Testing

```bash
dotnet build
dotnet test RpcWatsonTcp.Tests/RpcWatsonTcp.Tests.csproj
dotnet test RpcWatsonTcp.SourceGenerator.Tests/RpcWatsonTcp.SourceGenerator.Tests.csproj

# Run a single test by name fragment
dotnet test RpcWatsonTcp.Tests/RpcWatsonTcp.Tests.csproj --filter "DisplayName~EchoHandler"

# Produce a NuGet package
dotnet pack
```
