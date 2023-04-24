


module PgGen.CodeGen

open System.IO
open PgGen.StringBuffer
open Common
open System


let compileTimeDbFile = "compile_time_db.txt"
let titleCase (s:string) =
    System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(s)
let createModuleName suffix (project:string) (domain:string) =
    $"{project}.Domain.{domain}.{suffix}"

let ensureFolder(folder:string) =
    if not (Directory.Exists folder) then
        printfn $"Creating folder {folder}"
        Directory.CreateDirectory folder |> ignore

// postgres prefers lowercase, so CatDog -> cat_dog
// todo: not sure we are doing this on sql generation side to be consistent
let postgrestify (s:string) =
    s.Replace(" ","_").Replace("-","_").ToLowerInvariant()
let createStorageModuleName  proj domain = createModuleName "Storage" proj domain
let createApiModuleName  proj domain = createModuleName "Api" proj domain
let createServiceModuleName  proj domain = createModuleName "Service" proj domain
let createDomainModuleName  proj domain = createModuleName "Domain" proj domain
let emitDomain (proj:string) (s:Schema) =
    let domain = titleCase s.SName
    let pKeyType = "int" // fixfix todo: could vary long term but for now, hardcode
    let projCap = proj |> titleCase


    let domainCode =
        stringBuffer {
            yield $"namespace {projCap}.Domain.{domain}\n"

            let needsSystem =
                [for t in s.Tables do
                    for c in t.Cols do
                        if c.CType = Timestamp then
                            yield true] |> Seq.isEmpty |> not

            if needsSystem then
                yield $"open System\n"
            for t in s.Tables do
                let titleCap = t.TName |> titleCase
                let fsharpName = t.FSharpName()
                let colsExceptKey = t.Cols |> List.filter (fun c -> c.CType <> Id) // FIXFIX - generalize to non int keys


                yield $" // -------------------------------------------\n"
                yield $" // {t.TName}\n"
                yield $" // -------------------------------------------\n"
                yield $"type Create{fsharpName} = {{\n"
                for col in colsExceptKey do
                    yield $"    {col.FSharpName()} : {col.CType.FSharpType()}\n"
                for fr in t.FRefs do
                    if fr.Generate then
                        let colName = fr.Name |> Option.defaultValue $"id_{fr.ToTable}" |> toFSharp
                        yield $"    {colName} : int\n"
                yield $"}}"
                yield "\n"

                yield $"type Update{fsharpName} = {{\n"
                for col in colsExceptKey do
                    yield $"    {col.FSharpName()} : {col.CType.FSharpType()}\n"
                for fr in t.FRefs do
                    if fr.Generate then
                        let colName = fr.Name |> Option.defaultValue $"id_{fr.ToTable}" |> toFSharp
                        yield $"    {colName} : int\n"
                yield $"}}"
                yield "\n"
                yield $"type {fsharpName} = {{\n"
                for col in t.Cols do
                    yield $"    {col.FSharpName()} : {col.CType.FSharpType()}\n"
                for fr in t.FRefs do
                    if fr.Generate then
                        let colName = fr.Name |> Option.defaultValue $"id_{fr.ToTable}" |> toFSharp
                        yield $"    {colName} : int\n"
                yield $"}}"
                yield "\n"

        }
    let storageCode =
        stringBuffer {
            yield $"module {projCap}.Domain.{domain}.Storage\n"
            yield $"\n"
            yield $"open {projCap}.Common.Db\n"
            yield $"open {projCap}.Domain.{domain}\n"

            for t in s.Tables do
                let titleCap = t.TName |> titleCase
                let colsExceptKey = t.Cols |> List.filter (fun c -> c.CType <> Id) // FIXFIX - generalize to non int keys
                let colNamesExceptKey = String.Join(",",[for c in colsExceptKey  -> postgrestify c.CName])
                let placeHolders = String.Join(",",[for c in colsExceptKey  -> $"@{c.CName}"])
                let assignedValues = String.Join(",",[for c in colsExceptKey  -> $"{postgrestify c.CName}=request.{c.FSharpName()|> titleCase}"])

                let fsharpName = t.FSharpName()
                yield $"// -------------------------------------------\n"
                yield $"// {fsharpName} CRUD operations\n"
                yield $"// -------------------------------------------\n"
                yield $"\n"
                yield $"let create{fsharpName} (request:Create{fsharpName}) =\n"
                yield $"    task {{\n"
                yield $"        use! conn = Db.openConnectionAsync()\n"
                yield $"        use cmd = Db.CreateCommand<\"\"\"INSERT INTO {s.SName}.{t.TName} (\n"
                yield $"            {colNamesExceptKey}) VALUES({placeHolders}) RETURNING id\n"
                yield $"                                   \"\"\",SingleRow=true>(conn)\n"
                yield $"        let newId = cmd.Execute({assignedValues})\n"
                yield $"        return newId\n"
                yield $"    }}\n"
                yield $"\n"
                yield $"let read{fsharpName} (id:{pKeyType}) =\n"
                yield $"    failwithf \"Not implemented\"\n"
                yield $"    ()\n"
                yield $"\n"
                yield $"let update{fsharpName} (x:Update{fsharpName}) =\n"
                yield $"    failwithf \"Not implemented\"\n"
                yield $"    ()\n"
                yield $"\n"
                yield $"let delete{fsharpName} (id:{pKeyType}) =\n"
                yield $"    failwithf \"Not implemented\"\n"
                yield $"    ()\n"
                yield $"\n"
        }
    let apiWireup =
        stringBuffer {
            yield $"module {projCap}.Domain.{domain}.Api\n"

            yield $"\n"
            yield $"open {projCap}.Api.{domain}\n"
            yield $"open {projCap}.Domain.{domain}\n"
            yield $"\n"
            yield $"// Wire up the api definition to the service layer calls here\n"
            yield $"let api = {{\n"
            for t in s.Tables do
                let tableCap = t.TName |> titleCase
                let fsharpName = t.FSharpName()
                yield $"    // -------------------------------------------\n"
                yield $"    Create{fsharpName} = Service.create{fsharpName}\n"
                yield $"    Read{fsharpName} = Service.read{fsharpName}\n"
                yield $"    Update{fsharpName} = Service.update{fsharpName}\n"
                yield $"    Delete{fsharpName} = Service.delete{fsharpName}\n"
            yield "}\n"

        }

    let serviceLayer =
        stringBuffer {
            yield $"module {projCap}.Domain.{domain}.Service\n"
            yield $"\n"
            yield $"open {projCap}.Domain.{domain}\n"
            // yield $"open Plough.Common\n"
            yield $"open Plough.ControlFlow\n"
            yield $"\n"
            yield $"// PgGen: note - these are simple wrappers for now\n"
            for t in s.Tables do
                let tableCap = t.TName |> titleCase
                yield $"// -------------------------------------------\n"
                yield $"let create{tableCap} (request:Create{tableCap}) =\n"
                yield $"    taskEither {{\n"
                yield $"        return! Storage.create{tableCap} request\n"
                yield $"    }}\n"
                yield $"\n"

                yield $"let read{tableCap} (request:{pKeyType}) =\n"
                yield $"    taskEither {{\n"
                yield $"        return! Storage.read{tableCap} request\n"
                yield $"    }}\n"
                yield $"\n"

                yield $"let update{tableCap} (request:Update{tableCap}) =\n"
                yield $"    taskEither {{\n"
                yield $"        return! Storage.update{tableCap} request\n"
                yield $"    }}\n"
                yield $"\n"

                yield $"let delete{tableCap} (request:{pKeyType}) =\n"
                yield $"    taskEither {{\n"
                yield $"        return! Storage.delete{tableCap} request\n"
                yield $"    }}\n"
                yield $"\n"
        }
    let apiDef =
        stringBuffer {
            yield $"module {projCap}.Api.{domain}\n"
            yield $"\n"
            yield $"open Plough.ControlFlow\n"
            yield $"open Plough.WebApi.Client\n"
            yield $"open Plough.WebApi.Server\n"
            yield $"open Plough.WebApi.Server.Plain\n"
            yield $"open {projCap}.Domain.{domain}\n"

            yield $"\n"
            yield $"type Api ={{\n"
            for t in s.Tables do
                let tableCap = t.TName |> titleCase
                yield $"    Create{tableCap} : Create{tableCap} -> TaskEither<{pKeyType}>\n"
                yield $"    Read{tableCap}   : {pKeyType} -> TaskEither<{tableCap}>\n"
                yield $"    Update{tableCap} : Update{tableCap} -> TaskEither<unit>\n"
                yield $"    Delete{tableCap} : {pKeyType} -> TaskEither<string>\n"
            yield $"}}"
        }

    {| Storage = storageCode
       ApiWireup = apiWireup
       ApiDef = apiDef
       Domain = domainCode
       ServiceLayer = serviceLayer|}


let commonFileSource proj =
    stringBuffer {
        yield $"module {proj |> titleCase}.Common\n"
        yield $"\n"
        yield $"open Plough.ControlFlow\n"
        yield $"open System\n"
        yield $"\n"
        yield $"// Shared data structures like User definitions go here\n"
    }

let dbFileSource proj =
    stringBuffer {
        yield $"module {proj |> titleCase}.Common.Db\n"
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

let cleanSlash (path:string) =
    path.Replace("\\","/")
let generate (proj:string) (folder:string) (d:Db) =
    let projCap = titleCase proj
    ensureFolder folder
    let apiFolder = Path.Combine(folder, $"Api")
    ensureFolder apiFolder

    let generatedFileNames = [
        for schema in d.Schemas do
            let schemaCap = titleCase schema.SName
            printfn $"Generating schema {schemaCap}"
            let schemaSubFolder = $"{projCap}.Domain.{schemaCap}"
            let schemaFolder = Path.Combine(folder, $"{projCap}.Domain.{schemaCap}")
            ensureFolder schemaFolder

            let code = emitDomain proj schema


            let storageFile = Path.Combine(schemaSubFolder,$"{schemaCap}Storage.fs")
            let serviceFile = Path.Combine(schemaSubFolder,$"{schemaCap}Service.fs")
            let domainFile = Path.Combine(schemaSubFolder,$"{schemaCap}Domain.fs")
            let apiDefFile = Path.Combine("Api",$"{schemaCap}Api.fs")
            let apiWireupFile = Path.Combine("Api",$"{schemaCap}ApiWireup.fs")

            yield domainFile
            yield apiDefFile // api uses data structures from domainFile
            yield storageFile
            yield serviceFile
            yield apiWireupFile

            let fullStorageFile = Path.Combine(folder, storageFile)
            printfn $"  generating {fullStorageFile|> cleanSlash}"
            File.WriteAllText(fullStorageFile, code.Storage)

            let fullApiWireupFile = Path.Combine(folder, apiWireupFile)
            printfn $"  generating {fullApiWireupFile|> cleanSlash}"
            File.WriteAllText(fullApiWireupFile, code.ApiWireup)

            let fullServiceFile = Path.Combine(folder, serviceFile)
            printfn $"  generating {fullServiceFile|> cleanSlash}"
            File.WriteAllText(fullServiceFile, code.ServiceLayer)

            let fullDomainFile = Path.Combine(folder, domainFile)
            printfn $"  generating {fullDomainFile|> cleanSlash}"
            File.WriteAllText(fullDomainFile, code.Domain)

            let fullApiDef = Path.Combine(folder, apiDefFile)
            printfn $"  generating {fullApiDef|> cleanSlash}"
            File.WriteAllText(fullApiDef, code.ApiDef)

    ]
    let auxFileNames = [
        let commonFolderName = "Common"
        let fullCommonFolder = Path.Join(folder,commonFolderName)
        if not <| Directory.Exists fullCommonFolder then
            printfn $"Creating folder {fullCommonFolder}"
            ensureFolder fullCommonFolder

        let commonFile = Path.Join(commonFolderName,"Common.fs")
        yield commonFile

        let fullCommonFile = Path.Combine(folder, commonFile)
        File.WriteAllText(fullCommonFile, commonFileSource proj)

        let dbFile = Path.Join(commonFolderName,"Db.fs")
        yield dbFile

        let fullDbFile = Path.Combine(folder, dbFile)
        File.WriteAllText(fullDbFile, dbFileSource proj)

    ]

    let apiFiles,nonApiFiles = generatedFileNames |> List.partition (fun x -> x.StartsWith("Api/") || x.StartsWith("Api\\"))

    let fileNames = auxFileNames @ nonApiFiles @ apiFiles

    // write out an fsproj file with all the individual files in it
    let fsProjContent =
        stringBuffer {
            yield $"<Project Sdk=\"Microsoft.NET.Sdk\">\n"
            yield $"  <PropertyGroup>\n"
            yield $"    <TargetFramework>net7.0</TargetFramework>\n"
            yield $"  </PropertyGroup>\n"
            yield $"  <ItemGroup>\n"
            for file in fileNames do
                yield $"    <Compile Include=\"{file|> cleanSlash}\" />\n"
            yield $"  </ItemGroup>\n"
            yield $"<ItemGroup>\n"
            yield $"    <!--\n"
            yield $"    <PackageReference Include=\"FSharp.Data.Npgsql\" Version=\"[2.0.0)\" />\n"
            yield $"    <PackageReference Include=\"FSharp.Data.LiteralProviders\" Version=\"[1.0.0)\" />\n"
            yield $"    <PackageReference Include=\"Npgsql\" Version=\"[7.0.0)\" />\n"
            yield $"    <PackageReference Include=\"Plough.ControlFlow\" Version=\"[1.1.0)\" />\n"
            yield $"    -->\n"
            yield $"</ItemGroup>\n"
            yield $"</Project>\n"
        }
    let fsProjPath = Path.Combine(folder, $"{projCap}.Backend.fsproj")
    printfn $"Generating {fsProjPath|> cleanSlash}"
    File.WriteAllText(fsProjPath, fsProjContent)

    let compileTimeDbFile = Path.Combine(folder,compileTimeDbFile)
    File.WriteAllText(compileTimeDbFile,$"Host=127.0.0.1;Username=read_write;Password=readwrite;Database={proj};Pooling=true")

    let paketDepsFile = Path.Combine(folder, "paket.dependencies")
    let paketRefsFile = Path.Combine(folder, "paket.references")
    let configFolder = Path.Combine(folder, ".config")
    ensureFolder configFolder
    File.WriteAllText(Path.Combine(configFolder, "dotnet-tools.json"), dotnetToolsJson)

    File.WriteAllText(paketDepsFile, paketFile)
    File.WriteAllText(paketRefsFile, paketReferencesFile)

(* future work

// Using table api for bulk inserts
 use table = new Db.enzyme.Tables.enzyme ()
        let mutable counter = 0

        for signature, accessions in enzymes do
            if not <| lookup.ContainsKey(signature) then
                let newRow =
                    table.NewRow(accessions = Some accessions, signature = signature, id_source = uniprotSource)

                table.Rows.Add(newRow)
                counter <- counter + 1
                if counter % 1000 = 0 then printf $"... {counter}"

        let updatedCount = table.Update(conn)
        printfn $"   added {updatedCount} records for enzymes in {elapsed ()} seconds"

        for row in table.Rows do
            lookup.Add(row.signature, row.id * 1<GlobalProteinId>)




// Example of bulk insert style, where we prefetch a range of ids, and write directly to the db (this is fastest / lowest level)

 let! ids =
            task {
                use getIds = Db.CreateCommand<"
                    SELECT nextval('enzyme.protein_id_seq') FROM generate_series(1, @n)">(conn)
                let! ids = getIds.TaskAsyncExecute(idsNeededCount)
                return ids |> Seq.map Option.get |> Array.ofSeq
            }
        log "loadprots" $"reserved {ids.Length} ids"

        use proteinWriter = conn.BeginBinaryImport(importProteinSeqCmd)

        for protein in proteins do
            if not <| cache.Protein.ContainsKey protein.Accession then
                let idSource =
                    match protein.Origin with
                    | SwissProt -> swissprotSource
                    | Trembl -> tremblSource
                    | Uniprot -> uniprotSource
                let md5 =
                    protein.Md5 |> Option.defaultValue (Common.md5String protein.Sequence)

                proteinWriter.StartRow()
                proteinWriter.Write(ids[added],NpgsqlTypes.NpgsqlDbType.Integer)
                proteinWriter.Write(protein.Accession,NpgsqlTypes.NpgsqlDbType.Text)
                proteinWriter.Write(protein.Name,NpgsqlTypes.NpgsqlDbType.Text)
                proteinWriter.Write(protein.IsFragment,NpgsqlTypes.NpgsqlDbType.Boolean)
                proteinWriter.Write(protein.IsPrecursor,NpgsqlTypes.NpgsqlDbType.Boolean)
                proteinWriter.Write(md5,NpgsqlTypes.NpgsqlDbType.Text)
                proteinWriter.Write(idSource,NpgsqlTypes.NpgsqlDbType.Integer)
                proteinWriter.Write(protein.Sequence,NpgsqlTypes.NpgsqlDbType.Text)

                cache.Protein.Add(protein.Accession, ProteinLookup(int(ids[added])* 1<GlobalProteinId>,md5))

*)
