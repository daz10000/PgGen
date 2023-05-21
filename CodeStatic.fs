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


// ------ tool file setup
let paketFile = """source https://api.nuget.org/v3/index.json

framework: net7.0
storage:none

nuget FSharp.Core
nuget FSharp.Data.Npgsql >= 2.0.0
nuget FSharp.Data.LiteralProviders >= 1.0.0
nuget Npgsql >= 7.0.0
nuget Plough.ControlFlow >= 1.1.0
nuget Giraffe >= 6.0.0
nuget Thoth.Json.Giraffe >= 1.2.2
nuget Plough.WebApi.Client.Dotnet >= 1.2.2
#nuget Plough.WebApi.Server >= 1.2.2
nuget Plough.WebApi.Server.Giraffe >= 1.2.2

"""
let paketReferencesFile = """FSharp.Core
Fsharp.Data.Npgsql
FSharp.Data.LiteralProviders
Npgsql
Plough.ControlFlow
Plough.WebApi.Server.Giraffe
Plough.WebApi.Client.Dotnet
"""

let dotnetToolsJson = """{
  "version": 1,
  "isRoot": true,
  "tools": {
    "paket": {
      "version": "7.2.1",
      "commands": [
        "paket"
      ]
    }
  }
}"""

let commonFileSource proj =
    stringBuffer {
        yield $"module {proj |> titleCase}.Common\n"
        yield $"\n"
        yield $"open Plough.ControlFlow\n"
        yield $"open System\n"
        yield $"\n"
        yield $"[<CLIMutable>]\n"
        yield $"type BatchOffset = {{\n"
        yield $"    Offset : int option\n"
        yield $"    BatchSize : int option}}\n"

        yield $"// Shared data structures like User definitions go here\n"
    }
