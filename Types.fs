namespace PgGen


open System
type Db = { DName : string ; Owner : string ; Schemas : Schema list ; Comment : string option }
and Schema = { SName : string ; Tables : Table list ; Comment : string option ; Enums : PEnum list}

and ForeignRef = {
    /// Schema with the referring table/column
    FromSchema : string option
    /// Table with the referring column
    FromTable : string option
    /// Cols that make up the reference (see Generate option though which overrides this)
    FromCols : string list

    /// Other table that we are referring to
    ToTable : string
    /// Schema of other table that we are referring to
    ToSchema : string option
    ToCols : string list
    /// SHould we auto generate the referring column?
    Generate : bool
    IsNullable : bool
    Name : string option
}
and FRefAttr =
    | FName of string
    | FComment of string
    | FNullable
and Unique = {
    Cols : string list
}
/// This is a reference to an existing enum (inside a table)
and EnumRef = {
    EName : string
    ESchema : string option
    Generate : bool
    IsNullable : bool
    Name : string option
}

and EnumRefAttr =
    | EName of string
    | ERComment of string
    | ENullable

and PKey = {
    Cols : string list
}

and Table = {
        TName : string
        Comment : string option
        Attributes : TableAttr list
        Cols : Column list
        FRefs : ForeignRef list
        ERefs : EnumRef list
        Uniques : Unique list
        PKey : PKey option
} with
    member x.FSharpName() =
        x.TName.Split([|'_'|])
        |> Array.map (fun (s:string) -> s.[0].ToString().ToUpper() + s.[1..]) |> String.concat ""
    member x.PKeyName() =
        match x.PKey with
        | Some _ -> $"{x.FSharpName()}PKey"
        | None -> "int"
    member x.FullCols() =
        [   yield! x.Cols // regular columns
            for fk in x.FRefs do // foreign references
                if fk.Generate then
                    match fk.Name with
                    | None ->
                        yield { CName = $"id_{fk.ToTable}"; CType = ColumnType.Int32; Nullable = false; Array = false ; Comment = None}
                    | Some
                        name -> yield { CName = name; CType = ColumnType.Int32; Nullable = false; Array = false ; Comment = None}
                else
                    failwithf $"Not implemented,  non int/id foreign keys {fk}"
        ]

and TableAttr =
    | Comment of string
    | TBD

and ColumnType =
    | Id
    | String
    | Bool
    | Int32
    | Timestamp
    | Jsonb
    member x.Sql() =
        match x with
        | Id -> "int"
        | String -> "text"
        | Bool -> "boolean"
        | Int32 -> "int"
        | Timestamp -> "timestamptz default now()"
        | Jsonb -> "jsonb"
    member x.FSharpType() =
        match x with
        | Id -> "int"
        | String -> "string"
        | Bool -> "bool"
        | Int32 -> "int"
        | Timestamp -> "DateTime"
        | Jsonb -> "string"

and Column = {
    CName : string
    CType : ColumnType
    Nullable : bool
    Array : bool
    Comment : string option
} with
    member x.FSharpName() =
        x.CName.Split([|'_'|])
        |> Array.map (fun (s:string) -> s.[0].ToString().ToUpper() + s.[1..]) |> String.concat ""

and PEnum = {
    EName : string
    EValues : string list
    EComment : string option
}

type EAttr =
    | Member of string
    | EComment of string

type ColumnMatters =
    | Nullable
    | Array
    | CComment of string

/// Things in the table itself (cols et al)
type TableBodyItem =
    | ColumnItem of Column
    | Unique of Unique
    | ForeignRef of ForeignRef
    | EnumRef of EnumRef
    | PKey of PKey



type SchemaAttr =
    | TBD
    | SComment of string

type SchemaBodyItem =
    | TableItem of Table
    | PEnum of PEnum

type DBAttr =
    | Owner of string
    | DComment of string

type DBBodyItem =
    | SchemaItem of Schema
