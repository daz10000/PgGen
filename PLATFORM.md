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
| `CodeStatic.fs` | Fixed templates for generated code: `Db.fs` (connection, `TenantRls`), `Common.fs`, and the **generated package catalogue** (`generatedPackageVersions`, `sharedPackages`, `backendPackages`) |
| `CodeGen.fs` | F# generation: `emitDomain` (per schema: Domain, Storage, Service, Api contract + client, ApiServer routes, Wireup), master api files, `.fsproj` files, `Directory.Packages.props` |
| `Build.fs` | The DSL functions users call: `db`, `schema`, `table`, `col`, `frefId`, `enum`, `enumDef`, `unique`, `pKey`, `rlsTenant`, `rlsOrganization` |
| `Schema.fs` | `PgGen.ExtractSchema`: reverse engineering via `information_schema` / `pg_enum`, and emits a DSL `.fsx` |
| `protein_demo.fsx` | Example spec that prints SQL (loads `bin/Debug/net10.0/PgGen.dll`) |
| `schema2fs.fsx` | Argu CLI around `ExtractSchema` |
| `docs/plough-removal.md` | Investigation: replacing Plough in generated code, and the downstream-consumer analysis |

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
  parameters, so `None` is sent as e.g. `-1` or `''`, and the SQL turns it back into `NULL`. Only
  some types have sentinels; the others `failwith` at generation time.

## Generated output (what downstream projects get)

```
<folder>/
  Directory.Packages.props   CPM versions (only if no non-PgGen props file exists up the tree)
  compile_time_db.txt        connection string for the FSharp.Data.Npgsql type provider
  <P>.Backend.fsproj         net10.0; ProjectReference -> Api/<P>Api.fsproj
  Common/Common.fs           [shared]  BatchOffset etc.
  Common/Db.fs               [backend] connection, transactions, TenantRls
  <P>.Domain.<S>/<S>Domain.fs   [shared]  records + enums, System types only
  <P>.Domain.<S>/<S>Storage.fs  [backend] CRUD via FSharp.Data.Npgsql
  <P>.Domain.<S>/<S>Service.fs  [backend] taskEither wrappers
  Api/<P>Api.fsproj          netstandard2.0 shared project
  Api/<S>Api.fs              [shared]  namespace <P>.Api.<S>: Api contract record + Client
  Api/<S>ApiServer.fs        [backend] namespace <P>.Api.<S>: module Server (routes)
  Api/<S>ApiWireup.fs        [backend] Api record implemented by Service
  Api/<P>ServerApi.fs        [backend] all schema apis
  Api/<P>ClientApi.fs        [shared]  root client (mounted at /api)
```

**Invariant:** files marked `[shared]` must not depend on server or database libraries (Npgsql,
Giraffe, type providers, `Plough.WebApi.Server*`). Front ends and other services consume them.

## Package management

- **This repo:** NuGet Central Package Management. Versions live in `Directory.Packages.props`;
  `PgGen.fsproj` has version-less `PackageReference`s. `FSharp.Core` is explicit and pinned low
  (`DisableImplicitFSharpCoreReference`) so the package does not force consumers onto the latest
  FSharp.Core.
- **Generated code:** also CPM. To change a version or add a package, edit `CodeStatic.fs`
  (`generatedPackageVersions`, plus `sharedPackages` or `backendPackages`).
  - The generated props enable `CentralPackageTransitivePinningEnabled`. This matters because NuGet
    picks the *lowest* matching transitive version, while paket picked the highest. Pin transitive
    packages that have known problems (currently Newtonsoft.Json, because of a CVE).
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

There is no test project. To verify generator changes end to end, do what the last maintenance
pass did:

1. Write a scratch `.fsx` spec that covers enums with keyword values (e.g. `new`, `done`), FKs, a
   `Guid` column without a `Timestamp`, nullable and array columns, RLS, and a `pKey` table.
2. Emit the SQL, create a throwaway database, and load the SQL.
3. Run `CodeGen.generate` and point `compile_time_db.txt` at that database.
4. `dotnet build` both `Api/<P>Api.fsproj` (needs no DB) and `<P>.Backend.fsproj` (needs the DB).
5. Drop the database.

A local Postgres with the `read_write` / `readwrite` dev role is what the generated defaults expect.

**Release:** set `<Version>` in `PgGen.fsproj`, or push a tag `vX.Y.Z`. `.github/workflows/ci.yml`
builds, runs the demo, packs, and on tags pushes to nuget.org using the `NUGET_API_KEY` secret.
The id `PgGen` on nuget.org is owned by an unrelated project. Its versions are unlisted, so it does
not appear in search, but the id cannot be reused; hence `FSharp.PgGen`.

## Gotchas and known issues

- **Plough** (the generated web/client layer) is unmaintained. See `docs/plough-removal.md` before
  touching the api generation. `Plough.WebApi.Server.Giraffe`'s `mustAccept` is binary
  incompatible with every Giraffe from 6.4 to 8.3.
- **`pKey` is not emitted in SQL.** `emitTable` ignores `Table.PKey`, so composite keys exist only
  in the F# code.
- **The project name must differ from schema names.** The root client type `<P>.Api.<P>` would
  collide with the schema namespace `<P>.Api.<S>`.
- `emitDatabase` **throws** on reserved column names. Library code must never call `exit`.
- `CodeGen.generate` prints progress to stdout and overwrites generated files unconditionally.
- Reverse engineering:
  - `mapType2` maps `int8` to `Int32`, while `mapType` maps it to `Id64` (the TODO is to merge them).
  - Schema exclusions are spliced into SQL strings (fine for a local tool, but don't expose it).
- The generated `Timestamp` column SQL embeds `default now()` in the type string (`ColumnType.Sql`).
- `FComment`, `ERComment`, `CUpdate` and `TableAttr.TBD` are accepted by the DSL but ignored by
  generators.
- Generated delete operations are stubs that `failwith`.

## Checklist for changes

- Generator change → regenerate a scratch project and build both generated projects (see above).
- New generated dependency → add it to `CodeStatic.generatedPackageVersions`, and to
  `backendPackages` unless it is genuinely front-end safe.
- User-visible DSL or output change → update `README.md`.
- Architecture, layout, process or gotcha change → update **this file**.
