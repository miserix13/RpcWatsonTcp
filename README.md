# RpcWatsonTcp

A .NET RPC framework built on [WatsonTcp](https://github.com/jchristn/WatsonTcp) and [Nerdbank.MessagePack](https://github.com/AArnott/Nerdbank.MessagePack). It lets you define strongly-typed request/reply pairs, implement handlers for them, and call them from a client over TCP — with errors propagated cleanly back to the caller.

---

## Features

- **Strongly-typed RPC** — define your messages as plain C# classes; the framework handles routing and serialization
- **Handler-per-request pattern** — one `IHandler<TRequest, TReply>` implementation per operation
- **Structured error propagation** — server-side exceptions are wrapped and sent back as `RpcErrorReply`; the client receives an `RpcException`
- **Concurrent in-flight requests** — the client correlates replies to pending calls using a per-message GUID; multiple requests can be in flight simultaneously
- **DI-first** — integrates with `Microsoft.Extensions.DependencyInjection`
- **Efficient serialization** — [Nerdbank.MessagePack](https://github.com/AArnott/Nerdbank.MessagePack) (binary MessagePack format) via compile-time PolyType source generation

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
    Console.WriteLine(ex.Message);           // server exception message
    Console.WriteLine(ex.RemoteExceptionType); // e.g. "System.InvalidOperationException"
}
```

---

## Architecture

```
RpcClient.SendAsync<TRequest, TReply>(request)
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
```

Every message on the wire is an `RpcEnvelope` — a MessagePack-serialized wrapper containing:

| Field | Type | Purpose |
|---|---|---|
| `MessageId` | `Guid` | Correlates a reply to its pending request |
| `TypeName` | `string` | Assembly-qualified request/reply type name for deserialization |
| `Payload` | `byte[]` | MessagePack-serialized request or reply |
| `IsError` | `bool` | `true` when Payload contains an `RpcErrorReply` |

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
