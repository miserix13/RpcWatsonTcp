# RpcWatsonTcp – Copilot Instructions

## Project Overview

`RpcWatsonTcp` is a .NET class library that layers an RPC abstraction over [WatsonTcp](https://github.com/jchristn/WatsonTcp) using [Nerdbank.MessagePack](https://github.com/AArnott/Nerdbank.MessagePack) for serialization. Target framework: **net10.0**.

## Build

```bash
dotnet build
dotnet pack   # produces NuGet package
```

## Architecture

### RPC Flow (client → server only)

```
RpcClient.SendAsync<TRequest, TReply>(request)
  → serializes RpcEnvelope (MessageId + TypeName + Payload)
  → WatsonTcpClient ──TCP──► WatsonTcpServer
                                    ↓ RpcServer.OnMessageReceived
                              DependencyInjectionHandlerRegistry
                                    ↓ resolves HandlerDispatcher<TRequest, TReply> from DI
                              IHandler<TRequest, TReply>.HandleAsync(request)
                                    ↓ reply or RpcErrorReply
  ← WatsonTcpClient ◄─TCP── WatsonTcpServer
RpcClient completes TaskCompletionSource → returns TReply (or throws RpcException)
```

### Key Types

| Type | Role |
|---|---|
| `RpcEnvelope` | Wire wrapper: `MessageId` (Guid), `TypeName` (assembly-qualified), `Payload` (bytes), `IsError` (bool) |
| `IRequest` / `IReply` | Marker interfaces for user-defined message types |
| `IHandler<TRequest, TReply>` | Handler contract; implement this per RPC operation |
| `HandlerDispatcher<TRequest, TReply>` | Internal; bridges non-generic dispatch → typed handler |
| `DependencyInjectionHandlerRegistry` | Internal; maps `Type` → dispatcher via `IServiceProvider` |
| `RpcServer` | Wraps `WatsonTcpServer`; dispatches incoming envelopes |
| `RpcClient` | Wraps `WatsonTcpClient`; manages in-flight requests via `ConcurrentDictionary<Guid, TCS>` |
| `RpcErrorReply` | Server-side exception wrapped as a reply; client receives it as `RpcException` |

## Serialization Constraint

All user-defined `IRequest` and `IReply` types **must** have `[GenerateShape]` (from PolyType) so Nerdbank.MessagePack can serialize them. This constraint is enforced at compile-time on `RpcClient.SendAsync<TRequest, TReply>` and `ServiceCollectionExtensions.AddRpcHandler<>`.

```csharp
[GenerateShape]
public partial class GetUserRequest : IRequest { ... }

[GenerateShape]
public partial class GetUserReply : IReply { ... }
```

## DI Registration Pattern

```csharp
services.AddRpcServer(opt => { opt.IpAddress = "0.0.0.0"; opt.Port = 9000; });
services.AddRpcHandler<GetUserRequest, GetUserReply, GetUserHandler>();

// After building IServiceProvider:
serviceProvider.ApplyRpcHandlerRegistrations();
serviceProvider.GetRequiredService<RpcServer>().Start();
```

## Key Conventions

- **Nullable reference types** enabled; all code must be null-safe.
- **Implicit usings** enabled; `System`, `System.Threading.Tasks`, etc. are always available.
- Handler methods are async and accept `CancellationToken` with a `default` value.
- The namespace for all library types is `RpcWatsonTcp`.
- Internal types (`IHandlerDispatcher`, `IHandlerRegistry`, `DependencyInjectionHandlerRegistry`, `HandlerDispatcher<,>`, `RpcSerializer`) are not part of the public API.
- `RpcServer` and `RpcClient` implement `IAsyncDisposable`.
- `RpcEnvelope` and `RpcErrorReply` are `partial` classes (required by the PolyType `[GenerateShape]` source generator).
