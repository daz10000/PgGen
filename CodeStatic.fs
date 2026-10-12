module PgGen.CodeStatic

open System.IO
open PgGen.StringBuffer
open Common
open System

let compileTimeDbFile = "compile_time_db.txt"
let dbFileSource proj =
    stringBuffer {
        yield $"module {proj |> titleCase}.Db\n"
        yield $"\n"
        yield $"open System\n"
        yield $"open FSharp.Data.Npgsql\n"
        yield $"open FSharp.Data.LiteralProviders\n"
        yield $"open System.Transactions\n"
        yield sprintf """
module AppConfig =
    [<CLIMutable>]
    type AppConfig =
        {   ConnectionString : string
        }

module Db =
    let [<Literal>] defaultCommandTimeout = 600


    let mutable connectionString = None
    /// compile time connection string set via lims specific txt file with default value if file not found
    let [<Literal>] connectionStringCompileTime = TextFile<"%s">.Text

    let [<Literal>] methodTypes = MethodTypes.Task ||| MethodTypes.Sync

module TenantRls =
    type TenantContext = {
        TenantId : string
        OrganizationId : string option
    }

    let private ambientTenant = new System.Threading.AsyncLocal<TenantContext option>()

    let setAmbient (tenant:TenantContext) =
        ambientTenant.Value <- Some tenant

    let setAmbientFromValues (tenantId:string) (organizationId:string) =
        let org =
            if String.IsNullOrWhiteSpace organizationId then
                None
            else
                Some organizationId
        setAmbient { TenantId = tenantId; OrganizationId = org }

    let clearAmbient () =
        ambientTenant.Value <- None

    let tryGetAmbient () =
        ambientTenant.Value

    let private escapeSqlLiteral (value:string) =
        value.Replace("'", "''")

    let buildSessionSql (tenant:TenantContext) =
        let tenantId = escapeSqlLiteral tenant.TenantId
        let orgId = tenant.OrganizationId |> Option.defaultValue "" |> escapeSqlLiteral

        // session level (is_local=false): a transaction-local setting would be gone before the next
        // statement unless the caller opened a transaction.  Safe with pooling because every
        // connection open re-applies (or clears) it - see applyAmbientToConnection
        "select set_config('app.tenant_id', '" + tenantId + "', false);\n"
        + "select set_config('app.organization_id', '" + orgId + "', false);\n"

    let applyToConnection (connection:Npgsql.NpgsqlConnection) (tenant:TenantContext) =
        task {
            use cmd = connection.CreateCommand()
            cmd.CommandText <- buildSessionSql tenant
            let! _ = cmd.ExecuteNonQueryAsync(Async.DefaultCancellationToken)
            return ()
        }

    let applyAmbientToConnection (connection:Npgsql.NpgsqlConnection) =
        task {
            match ambientTenant.Value with
            | Some tenant ->
                do! applyToConnection connection tenant
            | None ->
                // clear anything left on a pooled connection; an empty tenant matches no rows
                do! applyToConnection connection { TenantId = ""; OrganizationId = None }
        }

type Db<'a>() =
    static member inline openConnectionAsync() =
        task {
            let conn = new Npgsql.NpgsqlConnection(Db.connectionString.Value)
            do! conn.OpenAsync(Async.DefaultCancellationToken)
            return conn
        }

    static member inline openConnection() =
        let conn = new Npgsql.NpgsqlConnection(Db.connectionString.Value)
        conn.Open()
        conn


    static member inline createTransactionScope isolationLevel =
        new TransactionScope(TransactionScopeOption.Required,
                             TransactionOptions(
                                 IsolationLevel=isolationLevel,
                                 Timeout=TransactionManager.MaximumTimeout
                             ),
                             TransactionScopeAsyncFlowOption.Enabled)

    //static member inline createTransactionScope () =
    //    Db.createTransactionScope IsolationLevel.ReadCommitted

type Db = NpgsqlConnection<ConnectionString=Db.connectionStringCompileTime,
                           CollectionType=CollectionType.ResizeArray, MethodTypes=Db.methodTypes,
                           Prepare=true, XCtor=true, CommandTimeout=Db.defaultCommandTimeout>

""" compileTimeDbFile
    }


// ------ package management (NuGet Central Package Management)

/// Every NuGet package used by generated code, with the version written to the generated
/// Directory.Packages.props.  Bump versions here.
let generatedPackageVersions = [
    // Explicit because the SDK's implicit FSharp.Core reference does not apply under CPM, and the
    // transitive minimum (4.7 via FSharp.SystemTextJson) lacks task {} / string interpolation.
    // Oldest .NET 10 FSharp.Core so any .NET 10 SDK can compile the generated code.
    "FSharp.Core", "10.0.100"
    "FSharp.Data.Npgsql", "2.1.0"
    "FSharp.Data.LiteralProviders", "1.0.3"
    "Npgsql", "10.0.3"
    "Giraffe", "8.3.0"
    "FSharp.SystemTextJson", "1.4.36"
    // Transitive pins (CentralPackageTransitivePinningEnabled) go here too.  NuGet resolves the
    // *lowest* matching transitive version, so pin anything with known issues.
]

/// Packages referenced by the generated shared project (domain types, api contract, client).
/// Keep this free of server / database libraries so front ends can consume it.
let sharedPackages = [
    "FSharp.Core"
    "FSharp.SystemTextJson" // F# unions / options on the wire, identical settings on client and server
]

/// Packages referenced by the generated backend project (it also gets sharedPackages via its
/// ProjectReference to the shared project)
let backendPackages = [
    "FSharp.Core"
    "FSharp.Data.Npgsql"
    "FSharp.Data.LiteralProviders"
    "Npgsql"
    "Giraffe"
]

/// Marker so we only ever overwrite a Directory.Packages.props that PgGen wrote itself
let generatedPropsMarker = "Generated by PgGen"

let packageVersionLines () =
    [ for name, version in generatedPackageVersions ->
        $"    <PackageVersion Include=\"{name}\" Version=\"{version}\" />" ]

let directoryPackagesPropsSource () =
    stringBuffer {
        yield "<Project>\n"
        yield $"  <!-- {generatedPropsMarker}: regenerated on every run, edit versions in PgGen (CodeStatic.fs) or\n"
        yield "       delete this file and manage the versions in a Directory.Packages.props further up the tree. -->\n"
        yield "  <PropertyGroup>\n"
        yield "    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>\n"
        yield "    <CentralPackageTransitivePinningEnabled>true</CentralPackageTransitivePinningEnabled>\n"
        yield "  </PropertyGroup>\n"
        yield "  <ItemGroup>\n"
        yield packageVersionLines ()
        yield "  </ItemGroup>\n"
        yield "</Project>\n"
    }

/// Shared (client + server) definitions: paging, the api error model, json settings and the
/// HttpClient helpers used by the generated typed client.  No server dependencies allowed here.
let private commonTemplate = """module __PROJ__.Common

open System
open System.Net.Http
open System.Text
open System.Text.Json
open System.Text.Json.Serialization
open System.Threading.Tasks

[<CLIMutable>]
type BatchOffset = {
    Offset : int option
    BatchSize : int option}

/// Errors an api call can return.  Sent over the wire as a Problem (title / status / detail)
type ApiError =
    | NotFound of string
    | Validation of string
    | Conflict of string
    | Unexpected of string

/// Result of every api operation, on both the server and the client
type ApiResult<'T> = Task<Result<'T, ApiError>>

/// Error body returned by the server (RFC 7807 style, readable from any language)
type Problem = {
    Title : string
    Status : int
    Detail : string
}

module ApiError =
    let status = function
        | NotFound _ -> 404
        | Validation _ -> 400
        | Conflict _ -> 409
        | Unexpected _ -> 500

    let toProblem (err:ApiError) =
        let title, detail =
            match err with
            | NotFound d -> "NotFound", d
            | Validation d -> "Validation", d
            | Conflict d -> "Conflict", d
            | Unexpected d -> "Unexpected", d
        { Title = title; Status = status err; Detail = detail }

    let ofProblem (p:Problem) =
        match p.Title, p.Status with
        | "NotFound", _ | _, 404 -> NotFound p.Detail
        | "Validation", _ | _, 400 -> Validation p.Detail
        | "Conflict", _ | _, 409 -> Conflict p.Detail
        | _ -> Unexpected p.Detail

/// Json settings shared by server and client.  Fieldless unions (enums) are plain strings and
/// None is null, so non F# clients see ordinary json.
module Json =
    let options =
        JsonFSharpOptions.Default()
            .WithUnionUnwrapFieldlessTags()
            .ToJsonSerializerOptions()

/// HttpClient helpers for the generated typed client.  Paths are relative (no leading /) so the
/// HttpClient.BaseAddress may include a path prefix - end it with a /.
module ApiClient =
    let private errorOf (response:HttpResponseMessage) =
        task {
            let status = int response.StatusCode
            let! body = response.Content.ReadAsStringAsync()
            let problem =
                try
                    match box (JsonSerializer.Deserialize<Problem>(body, Json.options)) with
                    | null -> None
                    | p -> Some (unbox<Problem> p)
                with _ -> None
            return
                match problem with
                | Some p -> ApiError.ofProblem p
                | None -> ApiError.ofProblem { Title = ""; Status = status; Detail = $"HTTP {status} {response.ReasonPhrase}" }
        }

    let send<'T> (http:HttpClient) (request:HttpRequestMessage) : ApiResult<'T> =
        task {
            try
                use request = request
                use! response = http.SendAsync request
                if response.IsSuccessStatusCode then
                    use! stream = response.Content.ReadAsStreamAsync()
                    let! value = JsonSerializer.DeserializeAsync<'T>(stream, Json.options)
                    return Ok value
                else
                    let! err = errorOf response
                    return Error err
            with e ->
                return Error (Unexpected e.Message)
        }

    let get<'T> (http:HttpClient) (path:string) : ApiResult<'T> =
        send<'T> http (new HttpRequestMessage(HttpMethod.Get, path))

    let post<'B,'T> (http:HttpClient) (path:string) (body:'B) : ApiResult<'T> =
        let request = new HttpRequestMessage(HttpMethod.Post, path)
        request.Content <- new StringContent(JsonSerializer.Serialize<'B>(body, Json.options), Encoding.UTF8, "application/json")
        send<'T> http request

    /// path?name=value&... for the parameters that are Some
    let withQuery (path:string) (parameters:(string * int option) list) =
        match [ for name, value in parameters do
                    match value with
                    | Some v -> yield $"{Uri.EscapeDataString name}={v}"
                    | None -> () ] with
        | [] -> path
        | ps -> path + "?" + String.Join("&", ps)

// Shared data structures like User definitions go here
"""

let commonFileSource proj = commonTemplate.Replace("__PROJ__", titleCase proj)

/// Backend only: Giraffe handlers that run an api function and write the result (or a Problem)
/// as json using the shared settings.
let private handlersTemplate = """module __PROJ__.Api.Handlers

open System
open System.Text.Json
open System.Threading.Tasks
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.Logging
open Giraffe
open __PROJ__.Common

let writeJson (statusCode:int) (value:'T) : HttpHandler =
    fun _next ctx ->
        task {
            ctx.SetStatusCode statusCode
            ctx.SetContentType "application/json; charset=utf-8"
            do! JsonSerializer.SerializeAsync<'T>(ctx.Response.Body, value, Json.options)
            return Some ctx
        }

/// Map exceptions to api errors.  Constraint violations are the caller's problem (4xx), anything
/// else is logged and reported without details.
let rec toApiError (ctx:HttpContext) (e:exn) =
    match e with
    | :? AggregateException as ae when ae.InnerExceptions.Count = 1 -> toApiError ctx ae.InnerException
    | :? Npgsql.PostgresException as pe when pe.SqlState = "23505" || pe.SqlState = "23503" ->
        Conflict pe.MessageText // unique / foreign key violation
    | :? Npgsql.PostgresException as pe when pe.SqlState.StartsWith "23" || pe.SqlState.StartsWith "22" ->
        Validation pe.MessageText // other integrity constraint / bad data
    | :? JsonException as je -> Validation $"Invalid request: {je.Message}"
    | :? NotImplementedException as ne -> Unexpected ne.Message
    | _ ->
        ctx.GetLogger("PgGen.Api").LogError(e, "Unhandled error for {Method} {Path}", ctx.Request.Method, ctx.Request.Path)
        Unexpected "Internal server error"

/// Run an api call and respond with 200 + json, or the error's status + Problem json
let respond (work:HttpContext -> ApiResult<'T>) : HttpHandler =
    fun next ctx ->
        task {
            let! result =
                task {
                    try
                        return! work ctx
                    with e ->
                        return Error (toApiError ctx e)
                }
            match result with
            | Ok value -> return! writeJson 200 value next ctx
            | Error err -> return! writeJson (ApiError.status err) (ApiError.toProblem err) next ctx
        }

/// Argument taken from the route (e.g. routef "/x/read/%i")
let withArg (f:'A -> ApiResult<'T>) (arg:'A) : HttpHandler =
    respond (fun _ -> f arg)

/// Argument is the json request body
let withBody (f:'B -> ApiResult<'T>) : HttpHandler =
    respond (fun ctx ->
        task {
            let! body = JsonSerializer.DeserializeAsync<'B>(ctx.Request.Body, Json.options)
            if isNull (box body) then
                return Error (Validation "Request body is required")
            else
                return! f body
        })

/// Paging from the query string (?offset=..&batchSize=..)
let withBatchOffset (f:BatchOffset -> ApiResult<'T>) : HttpHandler =
    respond (fun ctx ->
        let tryInt name =
            match ctx.TryGetQueryStringValue name with
            | None -> Ok None
            | Some v ->
                match Int32.TryParse v with
                | true, i -> Ok (Some i)
                | _ -> Error (Validation $"Query parameter {name} must be an integer")
        match tryInt "offset", tryInt "batchSize" with
        | Ok offset, Ok batchSize -> f { Offset = offset; BatchSize = batchSize }
        | Error e, _ | _, Error e -> Task.FromResult(Error e))
"""

let handlersFileSource proj = handlersTemplate.Replace("__PROJ__", titleCase proj)
