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
- **Authentication** *(opt-in)* — configure a 16-character `PresharedKey` on server and client; the server raises `AuthenticationSucceeded`/`AuthenticationFailed` events and exposes `ClientConnected`/`ClientDisconnected` lifecycle events
- **Polly resilience** *(opt-in)* — attach any [Polly v8](https://github.com/App-vNext/Polly) `ResiliencePipeline` (retry, circuit breaker, timeout) to `RpcClientOptions`
- **Durable outbox** *(opt-in)* — `DurableRpcClient` persists requests to a [Stellar.FastDB](https://github.com/stonstad/Stellar.FastDB) outbox before sending; entries are removed on success and replayed on reconnect for at-least-once delivery

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
RpcClient.SendAsync<TRequest, TReply>(request)
  ↓  [optional] ResiliencePipeline wraps the call
  ↓  serializes RpcEnvelope { MessageId, TypeName, Payload }
  ↓  WatsonTcpClient ──── TCP ────► WatsonTcpServer
                                         ↓ RpcServer dispatches by TypeName
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
| `TypeName` | `string` | Assembly-qualified request/reply type name for deserialization |
| `Payload` | `byte[]` | MessagePack-serialized request or reply |
| `IsError` | `bool` | `true` when Payload contains an `RpcErrorReply` |

---

## Performance

Benchmarks run on a loopback connection using [BenchmarkDotNet](https://benchmarkdotnet.org/) in Release mode. Source is in `RpcWatsonTcp.Benchmarks/`.

```bash
dotnet run --project RpcWatsonTcp.Benchmarks -c Release -- --filter "*"
```

### Round-trip latency (single client ↔ server, loopback)

| Payload | Mean | Error |
|---|--:|--:|
| Small (16 B) | 1,635 µs | ±119 µs |
| Medium (256 B) | 1,934 µs | ±548 µs |
| Large (4 KB) | 1,899 µs | ±526 µs |

Latency is dominated by TCP round-trip overhead. Payload size has a negligible effect.

### Serializer throughput (Nerdbank.MessagePack, single-threaded)

| Operation | Mean | Error |
|---|--:|--:|
| Serialize small request | 1.4 µs | ±0.07 µs |
| Serialize large request (4 KB) | 2.3 µs | ±0.51 µs |
| Serialize RpcEnvelope | 2.4 µs | ±0.55 µs |
| Deserialize small request | 2.1 µs | ±0.12 µs |
| Deserialize large request (4 KB) | 3.7 µs | ±0.72 µs |
| Deserialize RpcEnvelope | 3.7 µs | ±0.16 µs |

### Concurrent throughput (N simultaneous round-trips)

| Concurrency | Total time | Mean per request |
|--:|--:|--:|
| 1 | 1,712 µs | 1,712 µs |
| 10 | 13,663 µs | ~1,366 µs |
| 50 | 58,041 µs | ~1,161 µs |

Throughput-per-request improves under concurrency as TCP round-trips overlap.

---

## Building & Testing

```bash
dotnet build
dotnet test RpcWatsonTcp.Tests/RpcWatsonTcp.Tests.csproj

# Run a single test by name fragment
dotnet test RpcWatsonTcp.Tests/RpcWatsonTcp.Tests.csproj --filter "DisplayName~EchoHandler"

# Produce a NuGet package
dotnet pack
```
