# Plough removal (Oct 2026)

Status: **done.** Generated code now uses plain Giraffe on the server and `HttpClient` with
System.Text.Json on the client. No Plough package remains. This file records why, what changed,
and how to migrate existing consumers.

## Why

The investigation inspected the cached packages, built against current dependencies, and
JIT-checked Plough's server package. It found:

- **No maintenance.** `Plough.ControlFlow` stopped at 1.1.0 (its 2.0 beta was never finished)
  and `Plough.WebApi.*` at 1.2.2.
- **Stale transitive dependencies.**
  - `Ply` 0.3.1 is deprecated.
  - `Fable.Core` 3.x is two majors behind current Fable.
  - `Thoth.Json.Net` 8 pulled in `Newtonsoft.Json` 11.0.2, which has a high-severity CVE.
- **Already broken binaries.** `Plough.WebApi.Server.Giraffe` was built against a Giraffe 6
  prerelease. Its `mustAccept` fails with `MissingMethodException` on every Giraffe from 6.4 to
  8.3.
- **Front ends inherited it.** The api contract exposed Plough types, so every front end took
  those dependencies too.

## Decision

Plain Giraffe + System.Text.Json, with a JSON api that any language can call. Fable.Remoting was
rejected because its RPC-style routes and F#-specific protocol make non-F# clients second-class.

## What generated code looks like now

| Before (Plough) | Now |
|---|---|
| `TaskEither<'T>` = `Task<Result<Success<'T>, FailureMessage>>` | `ApiResult<'T>` = `Task<Result<'T, ApiError>>` (in `<P>.Common`) |
| `FailureMessage` (Unknown/Database/Parse/Validation/Conflict/NotFound/ExceptionFailure) | `ApiError` = `NotFound \| Validation \| Conflict \| Unexpected` (each `of string`) |
| `Success.Warnings` | removed |
| `taskEither { }` in the service layer | `task { ... return Ok x }` |
| `Server.build<'ctx> : Api -> ApiServer<'ctx>` | `Server.build : Api -> HttpHandler` (plain Giraffe) |
| Plough `makeJSONHandler*` | generated `Api/ApiHandlers.fs` (`respond`, `withArg`, `withBody`, `withBatchOffset`) |
| `Client(ClientBuilder)`, `<P>(ApiClient)` | `Client(HttpClient, basePath)`, `<P>(HttpClient, ?basePath = "api")` |
| Thoth JSON | System.Text.Json + FSharp.SystemTextJson (`Common.Json.options`) |
| errors as Plough `ProblemReport` | `Problem` JSON `{Title, Status, Detail}` + HTTP status |
| delete returned `TaskEither<string>` | delete returns `ApiResult<unit>` (still a stub) |
| shared project: netstandard2.0 + Plough | shared project: net10.0, only FSharp.SystemTextJson |

The URLs are unchanged. `ApiHandlers.toApiError` maps exceptions to errors:

| Exception | `ApiError` |
|---|---|
| `PostgresException` 23505 / 23503 (unique / foreign key) | `Conflict` |
| other `PostgresException` 23xxx / 22xxx (constraint / data) | `Validation` |
| `JsonException` (bad request body) | `Validation` |
| anything else | logged; returns `Unexpected "Internal server error"` |

Expected failures should be returned from the service layer as `Error ...` values.

## Migrating an existing consumer

1. Regenerate. Delete leftover paket files if PgGen warns about them.
2. Server startup: `app.UseGiraffe(<P>.Api.Server.handler <P>.Api.Server.api)` (or mount
   `<S>.Server.build` per schema). Remove the Plough server setup and the Thoth serializer
   registration.
3. Hand-written service code: replace `taskEither { }` with `task { ... return Ok x }`, and
   replace `FailureMessage` cases with `ApiError` cases.
4. .NET callers: replace `ApiClient.init ...` with an `HttpClient` whose `BaseAddress` points at
   the server root, ending in `/`. Match on `Ok` / `Error` instead of the Plough active patterns.
5. Non-F# callers: the JSON shapes are in README's "The HTTP api". F# `option` values are `null`,
   enums are strings, and errors are `Problem` bodies.

## Downstream consumers: what is still open

- **Fable packaging.** Fable compiles F# source, so a Fable app must reference the shared project
  directly. The `HttpClient` client is .NET only; a Fable client would use `Fetch` against the
  same JSON. FSharp.SystemTextJson's encoding (unions as strings, options as null) is easy to
  mirror with Thoth.Json.
- **Precision.** `int64` (`Id64`) and `decimal` are JSON numbers, which lose precision in
  JavaScript above 2^53. Switch those to strings if a JS front end needs full range.
- **Time zones.** `Timestamp` maps to `DateTime` (UTC from Npgsql); `DateTimeOffset` would be less
  ambiguous.
- **No OpenAPI or TypeScript output yet.** The plain JSON api makes generating them
  straightforward.
- **FSharp.Data.Npgsql** comes from the same org as Plough. It builds and runs fine against Npgsql
  10 (verified), but has the same long-term maintenance risk.
