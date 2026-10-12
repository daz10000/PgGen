### 0.1.0 - Unreleased
* First NuGet release, as FSharp.PgGen (the PgGen id on nuget.org belongs to another project)
* PgGen and generated projects use NuGet central package management instead of paket
* Generated web layer uses plain Giraffe + System.Text.Json; all Plough dependencies removed
* Generated shared project (domain types, api contract, HttpClient client) depends only on FSharp.SystemTextJson
* Language agnostic JSON api: enums as strings, options as null, errors as Problem bodies with HTTP status codes
* Every operation returns ApiResult<'T> = Task<Result<'T, ApiError>>
* Delete implemented; update and delete return NotFound when no row matched (including rows hidden by RLS)
* Fix: nullable columns were written through sentinel values, so Some 0.0 / Some "" / Some -1 were stored as NULL; now real NULL parameters
* Nullable Bool, Guid, Blob and array columns supported
* Composite primary keys (pKey) emitted in SQL
* New generated operations per table: count, keyset paging (page), a paged lookup per foreign key (list_by_<fk>) and a lookup per unique constraint (read_by_<cols>)
* index DSL item; foreign key columns are indexed automatically
* Enum values on the wire are the postgres labels (e.g. "in_progress"), not F# case names
* JSON input: optional fields may be omitted or null
* Fix: nullable enum columns did not compile in update / read types
* Generated domain types, api contract, client and routes all come from one model (ApiModel.fs)
* Fix: RLS tenant settings were transaction local, so tenants could not see or insert their own rows
* Fix: storage used blocking type provider calls
* Fix: Guid columns without timestamps, F# keyword enum values, nullable decimal / bigint columns
* Fix: client signatures and query parameters for composite keys and paging
* Reverse engineering handles uuid, jsonb, bytea and array columns; bigint maps to Id64
* emitDatabase throws instead of exiting the process
* Version and package release notes come from this file
* tests/smoke end to end test; README rewrite; PLATFORM.md maintainer guide
