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
| `ApiModel.fs` | **The model of the generated api**: `TypeRef`, `RecordDef`, `EnumDef`, `Operation` (name, route, verb, argument / result types, `OpKind`). `tableRecords` / `tableOperations` list everything generated per table; `validateQueries` checks named queries |
| `OpenApi.fs` | OpenAPI 3.1 document built from `ApiModel` |
| `CodeGen.fs` | F# generation: `emitDomain` (per schema: Domain types and Api / client / routes / wireup from `ApiModel`; Storage and Service per `OpKind`), master api files, `openapi.json`, `.fsproj` files, `Directory.Packages.props` |
| `Build.fs` | The DSL functions users call: `db`, `schema`, `table`, `col`, `frefId`, `enum`, `enumDef`, `unique`, `pKey`, `rlsTenant`, `rlsOrganization` |
| `Schema.fs` | `PgGen.ExtractSchema`: reverse engineering via `information_schema` / `pg_enum`, and emits a DSL `.fsx` |
| `protein_demo.fsx` | Example spec that prints SQL (loads `bin/Debug/net10.0/PgGen.dll`) |
| `schema2fs.fsx` | Argu CLI around `ExtractSchema` |
| `tests/smoke/` | End-to-end test: `run.sh` generates a project, loads it into a fresh database, compiles it and runs a live HTTP round trip with the generated client (`rt/Program.fs`) |
| `RELEASE_NOTES.md` | FAKE-style release notes. The first `### x.y.z` heading **is the package version** (read by `Directory.Build.props`) |
| `docs/proposal-queries-and-openapi.md` | Proposal: query framework (FK/unique lookups, named queries, SQL files, generic filters), OpenAPI generation, soft delete |
| `docs/plough-removal.md` | Why and how Plough was replaced by plain Giraffe, and the downstream-consumer analysis |

## How generation works

- **`ApiModel` is the single source of truth** for generated types and operations. The domain
  records, the `Api` contract record, the client members, the Giraffe routes, the wireup and
  `openapi.json` are all emitted by iterating `tableRecords` / `tableOperations`. **To add an
  operation**, add it to `tableOperations` (with a new `OpKind`), emit its storage SQL and service
  function in `CodeGen.emitDomain` (the compiler's incomplete-match warning shows where), and the
  rest follows. Records are emitted in list order, so a record must come after any record it
  refers to.
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
- Create and update commands use the type provider's `AllParametersOptional=true`: every
  parameter is an `option`, and `None` is sent as SQL `NULL`. Non-null columns pass `Some x`. (An
  older version used sentinel values like `-1` / `''` for NULL, which silently turned real values
  into NULL. Don't reintroduce that.) The same mechanism is the basis for optional query filters
  in named queries.
- Update and delete return rows affected; the service turns 0 into `Error (NotFound ...)`. This
  also covers rows hidden by RLS, so their existence isn't revealed.
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
  Api/openapi.json           OpenAPI 3.1, embedded in the backend, served at GET /api/openapi.json
```

**Per-table operations** (see `ApiModel.tableOperations`):
- CRUD;
- `count`;
- `page` (keyset);
- `list_by_<fk>` for each generated FK;
- `read_by_<cols>` for each `unique`;
- `query/<name>` for each named query;
- the original offset `list`.

**Wire contract** (keep it language agnostic):
- JSON settings live in `Common.fs`.
  - `Json.options` is for **writing**: `None` as an explicit `null`.
  - `Json.readOptions` is for **reading**: a missing or null option field is `None`, so non-F#
    clients can omit optional fields. FSharp.SystemTextJson's `SkippableOptionFields.Always` was
    tried and rejected, because it fails on explicit `null`.
  - Enum cases carry `[<JsonName "<pg label>">]`, so the wire value is the Postgres label.
- Keyset paging: requests have `After` (cursor) and `Limit`; results are `{ Items; Next }`. The
  cursor is the key, or for named queries the sort columns plus the key.
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

**Versioning and release notes** follow FAKE's `RELEASE_NOTES.md` convention:

```
### 0.2.0 - 2026-11-01
* Added: ...
* Fix: ...

### 0.1.0 - 2026-10-12
* ...
```

- The newest entry is at the top. `Directory.Build.props` reads the first `### x.y.z` heading as
  `Version` (pre-release suffixes like `0.3.0-beta.1` work) and its bullets as
  `PackageReleaseNotes`. There is no `<Version>` in the fsproj.
- **Every user-visible change adds a bullet to the top entry.** If the top entry is already
  released (tagged), start a new entry above it, and bump the version per semver: breaking change
  to the DSL or generated code → minor while < 1.0, major after.
- Check with `dotnet msbuild PgGen.fsproj -getProperty:Version -getProperty:PackageReleaseNotes`.

**Release:** replace `Unreleased` in the top heading with the date, commit, and push tag `vX.Y.Z`.
`.github/workflows/ci.yml` checks the tag matches RELEASE_NOTES.md, builds, runs the demo, packs,
and pushes to nuget.org using the `NUGET_API_KEY` secret.
The id `PgGen` on nuget.org is owned by an unrelated project. Its versions are unlisted, so it does
not appear in search, but the id cannot be reused; hence `FSharp.PgGen`.

## Gotchas and known issues

- **RLS settings are session level** (`set_config(..., false)`). `applyAmbientToConnection` runs on
  *every* connection open and clears the tenant when none is set; that is what prevents tenant
  leakage across pooled connections. Don't make it conditional again. (`is_local=true` silently
  broke RLS: the setting ended with the implicit transaction of the `select set_config`.)
- **Named query SQL** uses the optional-predicate pattern `(@p::type IS NULL OR col = @p::type)`
  under `AllParametersOptional=true`.
  - Cast the parameter at **every** use, not just in the `IS NULL` test. Npgsql sends enum arrays
    as `text[]`, which gives "operator does not exist: <enum> = text" unless every occurrence is
    cast.
  - With prepared statements, Postgres may switch to a generic plan that is weak for optional
    predicates. Watch query plans on big tables (mitigations are in the proposal doc).
- Keyset paging with a sort order needs non-null sort columns in one direction (`validateQueries`
  enforces this). Mixed directions would need an expanded OR condition instead of a row
  comparison.
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
- Deletes are hard deletes. Deleting a row that is still referenced returns `Conflict` (FK
  violation).

## Checklist for changes

- Generator change → `./tests/smoke/run.sh` (see above).
- New generated dependency → add it to `CodeStatic.generatedPackageVersions`, and to
  `backendPackages` unless it is genuinely front-end safe.
- User-visible DSL or output change → update `README.md` **and add a bullet to `RELEASE_NOTES.md`**.
- Architecture, layout, process or gotcha change → update **this file**.
