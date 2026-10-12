# Removing Plough from generated code (investigation, Oct 2026)

Status: **investigated, not implemented.** The decision below changes the public surface of
generated code, so it needs sign-off. Update this file when that happens.

## Where generated code uses Plough

PgGen itself does not depend on Plough. Only the *generated* projects do.

| Package (version) | Used for | Generated files |
|---|---|---|
| `Plough.ControlFlow` 1.1.0 | `TaskEither<'T>` (= `Task<Result<Success<'T>, FailureMessage>>`) and the `taskEither {}` CE | `<S>Api.fs` (contract, client), `<S>Service.fs`, `<S>Storage.fs` (open only) |
| `Plough.WebApi.Core` 1.2.2 | `ClientBuilder`/`ApiClient` (client), `ApiServer<'ctx>`, `route`/`choose`/`subRoute`, `makeJSONHandler*Async` (server); JSON via Thoth | `<S>Api.fs`, `<P>ClientApi.fs`, `<S>ApiServer.fs` |
| `Plough.WebApi.Server.Giraffe` 1.2.2 | Giraffe implementation of the server builder | backend only |
| `Plough.WebApi.Client.Dotnet` 1.2.2 | `ApiClient.init` for .NET callers | backend reference, used by consumers |

## Why it has to go

Most of this was found by inspecting the cached packages and by building and JIT-checking against
current dependencies:

- **No maintenance.** ControlFlow's last stable release is 1.1.0, and its `2.0.0-beta001` was never
  finished. WebApi is at 1.2.2. Both come from demetrixbio (the same org as FSharp.Data.Npgsql).
- **Stale transitive dependencies.**
  - `Ply` 0.3.1 is deprecated; F# 6+ has `task {}` built in.
  - `Fable.Core` 3.x is two major versions behind current Fable.
  - `Thoth.Json` 7 / `Thoth.Json.Net` 8 pull in `Newtonsoft.Json` 11.0.2, which has a known
    high-severity CVE. The generated `Directory.Packages.props` now pins Newtonsoft 13.0.4
    transitively to work around this.
- **Binary incompatibility already present.** `Plough.WebApi.Server.Giraffe` targets net6.0 and
  was built against a Giraffe 6 prerelease. JIT-compiling all of its methods against Giraffe
  6.4.0, 7.0.2 and 8.3.0 gives the same result for each: 240 compile, and `mustAccept` fails with
  `MissingMethodException`. Generated code does not call `mustAccept`, but downstream code that
  does will fail at runtime. Paket previously resolved Giraffe to the latest version, so existing
  deployments very likely already run on Giraffe 8.
- **Front ends inherit the dependencies too.** The api contract exposes `TaskEither`, so every
  consumer of the shared project, including front ends, takes ControlFlow, Ply and Fable.Core 3.

## Options

### A. Fable.Remoting

The generated `Api` record-of-functions is exactly the Fable.Remoting model.

- Server: `Fable.Remoting.Giraffe`. Clients: `Fable.Remoting.Client` (Fable) and
  `Fable.Remoting.DotnetClient`.
- Serialization is guaranteed to match between .NET and Fable, with no hand-written client.
- Cons: the contract must use `Async<'T>` (not `Task`) to work under Fable. Routes become RPC
  style (`/IApi/ReadSample`), so the current REST URLs change, and non-F# clients (TypeScript,
  curl, other languages) are second-class with no OpenAPI.

### B. Plain Giraffe + System.Text.Json + generated client (recommended)

- Server: generate small Giraffe handlers directly. Giraffe 7+ already uses `FSharp.SystemTextJson`.
- Errors: replace `FailureMessage` and `Success<'T>` with a generated `ApiError` union, using
  `Result<'T, ApiError>`. Use `FsToolkit.ErrorHandling` for `taskResult {}`; it targets
  netstandard2.0 and supports Fable. A generated alias
  `type TaskEither<'T> = Task<Result<'T, ApiError>>` would reduce churn.
- .NET client: a generated `HttpClient` + System.Text.Json client, about 50 lines per schema.
- Fable client: `Thoth.Fetch`/`Thoth.Json`. Configure the server with
  `JsonFSharpOptions.ThothLike()` so unions, options, `int64` and `decimal` encode the same way on
  both sides.
- Pros: keeps today's REST URLs, so existing HTTP callers keep working. No unmaintained
  dependencies. Non-F# front ends work, and OpenAPI or TypeScript types could be generated later.
- Cons: the most generator work. It breaks code that pattern matches on `FailureMessage` or uses
  `Success.Warnings`.

### C. Vendor the parts of Plough we use

Copy `TaskEither` and the half-dozen handler and client helpers into generated `Common` files.

- Quickest, and mostly source compatible.
- But PgGen would then own an old design, and Thoth 7/8 + Newtonsoft would remain.

### What decides between A and B

Will anything other than F# (.NET or Fable) call the api? If yes, choose B. If every consumer is
F#, A is less code to own. Either way, removing Plough breaks consumers that use
`FailureMessage`, `Success` or `taskEither`. Ship it as a minor version bump with migration notes.

## Downstream consumers of generated code

Generated code is consumed by backend servers (the backend project) and potentially by front ends
and other services (the shared project).

### Already fine

Domain records and enums are plain F# records and unions that use only `System` types, so they
work in any .NET runtime and under Fable.

### Fixed in this change

- The "client" `Api` project used to compile the server routing too, which opened
  `Plough.WebApi.Server`, so any front end that referenced it inherited server dependencies. Routing
  now lives in `<S>ApiServer.fs`, which only the backend compiles.
- The shared project targets `netstandard2.0` and references only `Plough.ControlFlow` and
  `Plough.WebApi.Core`: no Npgsql, Giraffe or type providers.
- The backend references the shared project instead of recompiling its files, so server and client
  share one set of types (useful for integration tests and server-to-server calls).
- Generated `Common.fs` no longer opens Plough.
- Domain files now open `System` when a table has `Guid` columns. Before, they only did so for
  `Timestamp`, and such tables did not compile.
- Client fixes:
  - Read/Delete for composite-key tables took `int`; they now take the key type.
  - List built URLs like `?offset=Some(5)` and used `batch`, which did not bind to
    `BatchOffset.BatchSize`.

### Still open

- **The contract still exposes Plough types.** See the options above.
- **Fable packaging.** Fable compiles F# source, so a Fable app must either reference the shared
  project directly or receive a package with the `.fs` files under `fable/`. Neither is set up yet.
- **Wire types.**
  - `Jsonb` columns are `string` in F#, so clients receive JSON inside a string.
  - `Timestamp` maps to `DateTime`; `DateTimeOffset` would be less ambiguous across time zones.
  - `Id64`/`int64` and `decimal` lose precision as JSON numbers in JavaScript. Thoth encodes them as
    strings; any replacement must do the same.
- **The client root is hard-coded to `/api`** in `<P>ClientApi.fs`.
- **Non-F# consumers** have no OpenAPI or TypeScript output.
- **FSharp.Data.Npgsql** (compile-time-checked SQL) is from the same org as Plough. It is less
  urgent: it builds fine against Npgsql 10 (verified), but it has the same long-term maintenance
  risk.
