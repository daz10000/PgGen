# PgGen platform guide (for agents and new maintainers)

> **Keep this file current.** If a change alters the architecture, the generated output layout,
> package management, the build or release process, or the gotchas listed here, update this file in
> the same change. Stale guidance is worse than none.

## What PgGen is

PgGen is an F# library, published to NuGet as **`FSharp.PgGen`** (assembly and namespace `PgGen`).

1. Users describe a Postgres database in an F# DSL, usually in an `.fsx` script.
2. `Generate.emitDatabase` turns the spec into SQL DDL.
3. `CodeGen.generate` turns it into F# projects: a backend and a shared api/client project.
4. `ExtractSchema` does the reverse: it reads a live database and writes a DSL spec (`schema2fs.fsx`).

There is no CLI app. Scripts are the entry points.

## Repository layout

The F# compile order in `PgGen.fsproj` matters.

| File | Role |
|---|---|
| `Types.fs` | Core model: `Db`, `Schema`, `Table`, `Column`, `ColumnType` (with `.Sql()` / `.FSharpType()`), `ForeignRef`, `EnumRef`, `PKey`, plus the DSL attribute unions (`ColumnMatters`, `TableAttr`, …) |
| `Reserved.fs` | Postgres and F# reserved words (data compiled in; `postgres_reserved.csv` is the source). `isReserved`, `quoteFSharpReserved` |
| `Common.fs` | Naming helpers (`toFSharp`, `toFSharpLower`, `titleCase`, `deReservifyFSharp`), `dotnetVersion` / `sharedTargetFramework` for generated projects |
| `StringBuilder.fs` | `stringBuffer { yield ... }` computation expression, used by all generators |
| `Generate.fs` | SQL emission: sequences, tables, enums, FKs, uniques, comments, owner, RLS policies |
| `CodeStatic.fs` | Fixed templates for generated code: `Db.fs` (connection, `TenantRls`), shared `Common.fs` (`ApiError`, `ApiResult`, json options, `ApiClient` HttpClient helpers), backend `ApiHandlers.fs` (Giraffe handlers, exception → `ApiError` mapping), and the **generated package catalogue** (`generatedPackageVersions`, `sharedPackages`, `backendPackages`) |
| `CodeGen.fs` | F# generation: `emitDomain` (per schema: Domain, Storage, Service, Api contract + client, ApiServer routes, Wireup), master api files, `.fsproj` files, `Directory.Packages.props` |
| `Build.fs` | The DSL functions users call: `db`, `schema`, `table`, `col`, `frefId`, `enum`, `enumDef`, `unique`, `pKey`, `rlsTenant`, `rlsOrganization` |
| `Schema.fs` | `PgGen.ExtractSchema`: reverse engineering via `information_schema` / `pg_enum`, and emits a DSL `.fsx` |
| `protein_demo.fsx` | Example spec that prints SQL (loads `bin/Debug/net10.0/PgGen.dll`) |
| `schema2fs.fsx` | Argu CLI around `ExtractSchema` |
| `tests/smoke/` | End-to-end test: `run.sh` generates a project, loads it into a fresh database, compiles it and runs a live HTTP round trip with the generated client (`rt/Program.fs`) |
| `docs/plough-removal.md` | Why and how Plough was replaced by plain Giraffe, and the downstream-consumer analysis |

## How generation works

- The DSL builders in `Build.fs` return wrapper union cases (`TableItem`, `ColumnItem`, …). The
  parent builder unpacks them into the records in `Types.fs`.
- Foreign keys (`frefId`) and enum columns (`enum`) are not stored in `Table.Cols`. They are in
  `FRefs` / `ERefs` and are materialised as columns by `Table.FullCols()`. Generators that iterate
  `t.Cols` must handle `FRefs`/`ERefs` separately. Several places do this by hand.
- Naming conventions:
  - SQL names are snake_case.
  - FK columns are `id_<table>`.
  - F# names come from `toFSharp` (`sample_tag` becomes `SampleTag`). F# reserved words get an `X`
    appended in some places and backtick quoting in others (enum values in Storage).
- Nullable columns in Storage use a sentinel hack: the type provider cannot take `option`
  parameters, so `None` is sent as e.g. `-1` or `''`, and the SQL turns it back into `NULL`. A
  real value equal to the sentinel also becomes `NULL`. Only some types have sentinels; the others
  `failwith` at generation time. The SQL-side sentinel fixes the parameter type that Postgres
  infers, so `Id64` needs `-1::bigint`.
- Storage uses the type provider's `TaskAsyncExecute`. The synchronous `Execute` blocks a thread,
  and it wraps database errors in `AggregateException`, which hides `PostgresException` from the
  error mapping.

## Generated output (what downstream projects get)

```
<folder>/
  Directory.Packages.props   CPM versions (only if no non-PgGen props file exists up the tree)
  compile_time_db.txt        connection string for the FSharp.Data.Npgsql type provider
  <P>.Backend.fsproj         net10.0; ProjectReference -> Api/<P>Api.fsproj
  Common/Common.fs           [shared]  BatchOffset, ApiError, ApiResult, Problem, Json options, ApiClient
  Common/Db.fs               [backend] connection, transactions, TenantRls
  <P>.Domain.<S>/<S>Domain.fs   [shared]  records + enums, System types only
  <P>.Domain.<S>/<S>Storage.fs  [backend] CRUD via FSharp.Data.Npgsql
  <P>.Domain.<S>/<S>Service.fs  [backend] returns ApiResult; business logic goes here
  Api/<P>Api.fsproj          net10.0 shared project (deps: FSharp.Core, FSharp.SystemTextJson)
  Api/<S>Api.fs              [shared]  namespace <P>.Api.<S>: Api contract record + Client(HttpClient, basePath)
  Api/ApiHandlers.fs         [backend] module <P>.Api.Handlers: respond / withArg / withBody / withBatchOffset
  Api/<S>ApiServer.fs        [backend] namespace <P>.Api.<S>: module Server, build : Api -> HttpHandler
  Api/<S>ApiWireup.fs        [backend] Api record implemented by Service
  Api/<P>ServerApi.fs        [backend] all schema apis + handler (everything under /api)
  Api/<P>ClientApi.fs        [shared]  root client <P>.Api.<P>(http, ?basePath = "api")
```

**Wire contract** (keep it language agnostic):
- JSON uses `Json.options` in `Common.fs` (FSharp.SystemTextJson, fieldless unions as strings,
  `None` as `null`). The server and the client must use the same options.
- Errors are a `Problem` (`Title` = `ApiError` case name, `Status`, `Detail`) with the matching
  HTTP status.
- The routes and their HTTP shapes are documented in README's "The HTTP api". Changing them
  breaks non-F# callers.

**Invariant:** files marked `[shared]` must not depend on server or database libraries (Npgsql,
Giraffe, ASP.NET, type providers). Front ends and other services consume them.

## Package management

- **This repo:** NuGet Central Package Management. Versions live in `Directory.Packages.props`;
  `PgGen.fsproj` has version-less `PackageReference`s. `FSharp.Core` is explicit and pinned low
  (`DisableImplicitFSharpCoreReference`) so the package does not force consumers onto the latest
  FSharp.Core.
- **Generated code:** also CPM. To change a version or add a package, edit `CodeStatic.fs`
  (`generatedPackageVersions`, plus `sharedPackages` or `backendPackages`).
  - The generated props enable `CentralPackageTransitivePinningEnabled`. This matters because NuGet
    picks the *lowest* matching transitive version, while paket picked the highest. Add a
    `PackageVersion` for any transitive package with known problems.
  - **FSharp.Core is explicit** (pinned to 10.0.100, with `DisableImplicitFSharpCoreReference` in
    the generated projects). The SDK's implicit reference does not apply under CPM, and
    FSharp.SystemTextJson's minimum of FSharp.Core 4.7 would win, which has no `task {}` and no
    string interpolation.
  - `writePackageProps` will not overwrite a `Directory.Packages.props` it did not generate (it
    checks for the `Generated by PgGen` marker). It prints the needed lines instead.
- Paket is gone everywhere. `CodeGen` only *warns* about leftover paket files in output folders;
  it never deletes user files.

## Build, verify, release

```bash
dotnet build                      # library
dotnet fsi protein_demo.fsx       # quick SQL smoke test (needs the Debug build)
dotnet pack -c Release -o out     # FSharp.PgGen.<ver>.nupkg + .snupkg
```

**After any generator change, run the end-to-end smoke test:**

```bash
./tests/smoke/run.sh          # PSQL=/path/to/psql if psql is not on PATH (e.g. Windows Git Bash)
```

It needs a local Postgres where the `read_write` role (password `readwrite`, the generated
defaults) can create databases, and it drops and recreates `pggen_smoke`. It:
1. generates `tests/smoke/spec.fsx` into a temp folder;
2. loads the SQL;
3. builds the shared project and the backend (type-checked against the database);
4. hosts the backend on Kestrel and runs the checks in `tests/smoke/rt/Program.fs` through the
   generated client: CRUD, enums, nullables, paging, error mapping and RLS tenant isolation.

It prints `PASS` / `FAIL` lines and exits non-zero on failure. Extend `spec.fsx` and `Program.fs`
when you add generator features. It isn't in CI yet; that would need a Postgres service container.

**Release:** set `<Version>` in `PgGen.fsproj`, or push a tag `vX.Y.Z`. `.github/workflows/ci.yml`
builds, runs the demo, packs, and on tags pushes to nuget.org using the `NUGET_API_KEY` secret.
The id `PgGen` on nuget.org is owned by an unrelated project. Its versions are unlisted, so it does
not appear in search, but the id cannot be reused; hence `FSharp.PgGen`.

## Gotchas and known issues

- **RLS settings are session level** (`set_config(..., false)`). `applyAmbientToConnection` runs on
  *every* connection open and clears the tenant when none is set; that is what prevents tenant
  leakage across pooled connections. Don't make it conditional again. (`is_local=true` silently
  broke RLS: the setting ended with the implicit transaction of the `select set_config`.)
- A table may have an `Id` column or a `pKey`, not both (`emitDatabase` rejects it). Read
  functions annotate their record type, because a `<T>PKey` record can have exactly the same fields.
- **The project name must differ from schema names.** The root client type `<P>.Api.<P>` would
  collide with the schema namespace `<P>.Api.<S>`.
- `emitDatabase` **throws** on reserved column names. Library code must never call `exit`.
- `CodeGen.generate` prints progress to stdout and overwrites generated files unconditionally.
- Reverse engineering:
  - `mapType2` handles the common udt names and falls back to `mapType` (the TODO is to merge them).
  - Composite primary keys and unique constraints are not recovered.
  - Schema exclusions are spliced into SQL strings (fine for a local tool, but don't expose it).
- The generated `Timestamp` column SQL embeds `default now()` in the type string (`ColumnType.Sql`).
- `FComment`, `ERComment`, `CUpdate` and `TableAttr.TBD` are accepted by the DSL but ignored by
  generators.
- Generated delete operations are stubs that raise `NotImplementedException` (a 500 over HTTP).

## Checklist for changes

- Generator change → `./tests/smoke/run.sh` (see above).
- New generated dependency → add it to `CodeStatic.generatedPackageVersions`, and to
  `backendPackages` unless it is genuinely front-end safe.
- User-visible DSL or output change → update `README.md`.
- Architecture, layout, process or gotcha change → update **this file**.
