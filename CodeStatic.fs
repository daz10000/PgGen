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
        yield $"open Plough.ControlFlow\n"
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

        "select set_config('app.tenant_id', '" + tenantId + "', true);\n"
        + "select set_config('app.organization_id', '" + orgId + "', true);\n"

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
                ()
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
    "FSharp.Data.Npgsql", "2.1.0"
    "FSharp.Data.LiteralProviders", "1.0.3"
    "Npgsql", "10.0.3"
    "Giraffe", "8.3.0"
    "Plough.ControlFlow", "1.1.0"
    "Plough.WebApi.Core", "1.2.2"
    "Plough.WebApi.Server.Giraffe", "1.2.2"
    "Plough.WebApi.Client.Dotnet", "1.2.2"
    // Transitive pins (CentralPackageTransitivePinningEnabled).  NuGet resolves the *lowest*
    // matching transitive version (paket picked the highest), so pin anything with known issues.
    "Newtonsoft.Json", "13.0.4" // via Plough.WebApi.Core -> Thoth.Json.Net, 11.0.2 has a CVE
]

/// Packages referenced by the generated shared project (domain types, api contract, client).
/// Keep this free of server / database libraries so front ends can consume it.
let sharedPackages = [
    "Plough.ControlFlow"
    "Plough.WebApi.Core"
]

/// Packages referenced by the generated backend project (it also gets sharedPackages via its
/// ProjectReference to the shared project)
let backendPackages = [
    "FSharp.Data.Npgsql"
    "FSharp.Data.LiteralProviders"
    "Npgsql"
    "Giraffe"
    "Plough.WebApi.Server.Giraffe"
    "Plough.WebApi.Client.Dotnet"
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

let commonFileSource proj =
    stringBuffer {
        yield $"module {proj |> titleCase}.Common\n"
        yield $"\n"
        yield $"open System\n"
        yield $"\n"
        yield $"[<CLIMutable>]\n"
        yield $"type BatchOffset = {{\n"
        yield $"    Offset : int option\n"
        yield $"    BatchSize : int option}}\n"

        yield $"// Shared data structures like User definitions go here\n"
    }
