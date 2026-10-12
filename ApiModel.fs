/// Language neutral description of the generated api: the record / enum types and the operations.
/// Domain types, the Api contract record, the client, the server routes and the OpenAPI document
/// are all emitted from this model so they cannot disagree with each other.
module PgGen.ApiModel

open System
open Common

/// Paging limits used by generated keyset paging (also written into generated Common.fs)
let defaultPageLimit = 100
let maxPageLimit = 1000

type TypeRef =
    | TInt
    | TInt64
    | TBool
    | TString
    /// jsonb column: a string containing a json document
    | TJson
    | TFloat
    | TDecimal
    | TDateTime
    | TGuid
    | TBytes
    | TUnit
    | TEnum of name:string
    | TRecord of name:string
    | TOption of TypeRef
    | TArray of TypeRef
    member x.FSharp() : string =
        match x with
        | TInt -> "int"
        | TInt64 -> "int64"
        | TBool -> "bool"
        | TString | TJson -> "string"
        | TFloat -> "float"
        | TDecimal -> "decimal"
        | TDateTime -> "DateTime"
        | TGuid -> "Guid"
        | TBytes -> "byte[]"
        | TUnit -> "unit"
        | TEnum n | TRecord n -> n
        | TOption t -> $"{t.FSharp()} option"
        | TArray t -> $"{t.FSharp()} []"
    member x.StripOption() =
        match x with
        | TOption t -> t
        | t -> t

type Field = { Name : string; Type : TypeRef; Doc : string option }

type RecordDef = { Name : string; Fields : Field list; Doc : string option }

/// Fieldless union.  Cases are (F# case name, value on the wire = postgres enum label)
type EnumDef = { Name : string; Cases : (string * string) list; Doc : string option }

let columnType (c:Column) =
    let baseType =
        match c.CType with
        | Id | Int32 -> TInt
        | Id64 -> TInt64
        | String -> TString
        | Jsonb -> TJson
        | Bool -> TBool
        | Float -> TFloat
        | Decimal -> TDecimal
        | Timestamp -> TDateTime
        | ColumnType.Guid -> TGuid
        | Blob -> TBytes
        | Enum e -> TEnum (toFSharp e)
    let t = if c.Array then TArray baseType else baseType
    if c.Nullable then TOption t else t

let columnField (c:Column) = { Name = c.FSharpName(); Type = columnType c; Doc = c.Comment }

let enumDef (e:PEnum) =
    { Name = toFSharp e.EName
      Cases = [ for v in e.EValues -> toFSharp v, v ]
      Doc = e.EComment }

// ---------------------------------------------------------------------------------------------
// Per table shapes
// ---------------------------------------------------------------------------------------------

/// Columns making up the primary key, in key order
let keyColumns (t:Table) =
    let full = t.FullCols()
    match t.PKey with
    | None -> full |> List.filter (fun c -> c.CType = Id)
    | Some pk ->
        [ for name in pk.Cols ->
            match full |> List.tryFind (fun c -> c.CName = name) with
            | Some c -> c
            | None -> failwithf $"Could not find column primary key referenced '{name}' in table '{t.TName}'" ]

let keyType (t:Table) =
    match t.PKey with
    | None -> TInt
    | Some _ -> TRecord (t.PKeyTypeName())

/// Generated foreign key columns (one list_by_<col> lookup each)
let foreignKeyColumns (t:Table) =
    let names =
        [ for fr in t.FRefs do
            if fr.Generate then fr.Name |> Option.defaultValue $"id_{fr.ToTable}" ]
    t.FullCols() |> List.filter (fun c -> names |> List.contains c.CName)

/// Unique constraints usable as lookups (read_by_<cols>); array columns are skipped
let uniqueLookups (t:Table) =
    let full = t.FullCols()
    [ for u in t.Uniques do
        let cols = u.Cols |> List.choose (fun n -> full |> List.tryFind (fun c -> c.CName = n))
        if cols.Length = u.Cols.Length && cols |> List.forall (fun c -> not c.Array) then
            yield cols ]

let pageRequestName (t:Table) = $"{t.FSharpName()}Page"
let pageResultName (t:Table) = $"{t.FSharpName()}PageResult"
let fkRequestName (t:Table) (c:Column) = $"{t.FSharpName()}By{c.FSharpName()}Page"
let uniqueRequestName (t:Table) (cols:Column list) =
    let suffix = cols |> List.map (fun c -> c.FSharpName()) |> String.concat ""
    $"{t.FSharpName()}By{suffix}"

let private pagingFields (t:Table) =
    [ { Name = "After"; Type = TOption (keyType t)
        Doc = Some "Only return rows after this key: pass the Next value of the previous page" }
      { Name = "Limit"; Type = TOption TInt
        Doc = Some $"Maximum number of rows (default {defaultPageLimit}, at most {maxPageLimit})" } ]

/// Every record type generated for a table, in declaration order
let tableRecords (t:Table) : RecordDef list =
    let name = t.FSharpName()
    let full = t.FullCols()
    [ { Name = $"Create{name}"
        Fields = full |> List.filter (fun c -> c.CType <> Id) |> List.map columnField
        Doc = Some $"Values for a new {t.TName} row" }
      { Name = $"Update{name}"
        Fields = full |> List.map columnField
        Doc = Some $"New values for an existing {t.TName} row (identified by its key)" }
      { Name = name
        Fields = full |> List.map columnField
        Doc = t.Comment |> Option.orElse (Some $"A {t.TName} row") }
      match t.PKey with
      | Some _ ->
        { Name = t.PKeyTypeName()
          Fields = keyColumns t |> List.map columnField
          Doc = Some $"Primary key of {t.TName}" }
      | None -> ()
      { Name = pageRequestName t
        Fields = pagingFields t
        Doc = Some $"Request for a page of {t.TName} rows in key order" }
      { Name = pageResultName t
        Fields =
            [ { Name = "Items"; Type = TArray (TRecord name); Doc = None }
              { Name = "Next"; Type = TOption (keyType t)
                Doc = Some "Key to pass as After for the next page; null when there are no more rows" } ]
        Doc = Some $"A page of {t.TName} rows" }
      for c in foreignKeyColumns t do
        { Name = fkRequestName t c
          Fields = { columnField c with Type = (columnType c).StripOption(); Doc = None } :: pagingFields t
          Doc = Some $"Request for a page of {t.TName} rows with the given {c.CName}" }
      for cols in uniqueLookups t do
        { Name = uniqueRequestName t cols
          Fields = cols |> List.map (fun c -> { columnField c with Type = (columnType c).StripOption() })
          Doc = Some $"""Unique key ({String.Join(", ", cols |> List.map (fun c -> c.CName))}) of {t.TName}""" } ]

// ---------------------------------------------------------------------------------------------
// Operations
// ---------------------------------------------------------------------------------------------

type Verb =
    | Get
    | Post

type ArgStyle =
    /// GET, no input
    | NoArg
    /// GET <path>/{id}
    | RouteId
    /// GET <path>?offset=..&batchSize=.. (original list operation)
    | OffsetQuery
    /// POST with the argument as the json body
    | JsonBody

/// The ways an operation is implemented in storage (used to generate storage / service code)
type OpKind =
    | CreateOp
    | ReadOp
    | UpdateOp
    | DeleteOp
    | ListOp
    | CountOp
    | PageOp
    | ListByOp of Column
    | ReadByOp of Column list

type Operation = {
    /// Api record field / client member, e.g. ReadSample
    Name : string
    /// route relative to /<schema>, e.g. /sample/read
    Path : string
    Verb : Verb
    Arg : ArgStyle
    ArgType : TypeRef
    Result : TypeRef
    Summary : string
    Table : Table
    Kind : OpKind
} with
    /// service / storage function name, e.g. readSample
    member x.FunctionName = $"{x.Name.[0..0].ToLowerInvariant()}{x.Name.[1..]}"

let tableOperations (t:Table) : Operation list =
    let name = t.FSharpName()
    let path op = $"/{t.TName}/{op}"
    let op opName opPath verb arg argType result summary kind =
        { Name = opName; Path = opPath; Verb = verb; Arg = arg; ArgType = argType; Result = result
          Summary = summary; Table = t; Kind = kind }
    let row = TRecord name
    let hasIdKey = t.PKey.IsNone
    [ op $"Create{name}" (path "create") Post JsonBody (TRecord $"Create{name}")
        (if hasIdKey then TInt else TUnit) $"Create a {t.TName}; returns the new id" CreateOp
      if hasIdKey then
          op $"Read{name}" (path "read") Get RouteId TInt (TOption row) $"Read a {t.TName} by id (null if not found)" ReadOp
      else
          op $"Read{name}" (path "read") Post JsonBody (keyType t) (TOption row) $"Read a {t.TName} by key (null if not found)" ReadOp
      op $"Update{name}" (path "update") Post JsonBody (TRecord $"Update{name}") TUnit
        $"Update a {t.TName}; 404 if it doesn't exist" UpdateOp
      op $"Delete{name}" (path "delete") Post JsonBody (keyType t) TUnit
        $"Delete a {t.TName}; 404 if it doesn't exist, 409 if other rows still refer to it" DeleteOp
      op $"List{name}" (path "list") Get OffsetQuery (TRecord "BatchOffset") (TArray row)
        $"List {t.TName} rows with offset paging (prefer page for large tables)" ListOp
      op $"Count{name}" (path "count") Get NoArg TUnit TInt64 $"Number of {t.TName} rows" CountOp
      op $"Page{name}" (path "page") Post JsonBody (TRecord (pageRequestName t)) (TRecord (pageResultName t))
        $"A page of {t.TName} rows in key order (keyset paging)" PageOp
      for c in foreignKeyColumns t do
        op $"List{name}By{c.FSharpName()}" (path $"list_by_{c.CName}") Post JsonBody (TRecord (fkRequestName t c))
            (TRecord (pageResultName t)) $"A page of {t.TName} rows with the given {c.CName}" (ListByOp c)
      for cols in uniqueLookups t do
        let suffix = cols |> List.map (fun c -> c.FSharpName()) |> String.concat ""
        let routeSuffix = cols |> List.map (fun c -> c.CName) |> String.concat "_"
        op $"Read{name}By{suffix}" (path $"read_by_{routeSuffix}") Post JsonBody (TRecord (uniqueRequestName t cols))
            (TOption row) $"Read a {t.TName} by its unique {routeSuffix} (null if not found)" (ReadByOp cols) ]

let schemaOperations (s:Schema) = s.Tables |> List.collect tableOperations
