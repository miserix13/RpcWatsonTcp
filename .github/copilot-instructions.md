# RpcWatsonTcp – Copilot Instructions

## Project Overview

`RpcWatsonTcp` is a .NET class library that layers an RPC abstraction over [WatsonTcp](https://github.com/jchristn/WatsonTcp) using [Nerdbank.MessagePack](https://github.com/AArnott/Nerdbank.MessagePack) for serialization. Target framework: **net10.0**.

## Build

```bash
dotnet build
```

## Architecture

The RPC pattern is built around three interfaces:

- **`IRequest`** – Marker interface. Every RPC request type implements this.
- **`IReply`** – Marker interface. Every RPC reply type implements this.
- **`IHandler<TRequest, TReply>`** – Typed handler contract. Implementations receive a request and return a reply asynchronously.

```
Client sends IRequest → transport (WatsonTcp) → IHandler<TRequest, TReply> → IReply → transport → Client
```

Serialization of `IRequest`/`IReply` types over the wire uses Nerdbank.MessagePack (MessagePack format). New types that flow over the wire must be annotated for MessagePack serialization (e.g., `[MessagePackObject]` / `[Key]` attributes, or source-generated serializers via `NerdBankMessagePackSerializer`).

## Key Conventions

- **Nullable reference types** are enabled (`<Nullable>enable</Nullable>`). All code must be null-safe; use `?` annotations where nulls are valid.
- **Implicit usings** are enabled; `System`, `System.Threading.Tasks`, etc. are available without explicit `using` directives.
- All handler methods are async and accept a `CancellationToken` with a default value of `default`.
- The namespace for all library types is `RpcWatsonTcp`.
