namespace PgGen


type Db = { DName : string ; Owner : string ; Schemas : Schema list ; Comment : string option }
and Schema = { SName : string ; Tables : Table list ; Comment : string option ; Enums : PEnum list}

and ForeignRef = {
    FromSchema : string option
    FromTable : string option
    FromCols : string list
    ToTable : string
    ToSchema : string option
    ToCols : string list
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
} and EnumRefAttr =
    | EName of string
    | ERComment of string
    | ENullable

and Table = {
        TName : string
        Comment : string option
        Attributes : TableAttr list
        Cols : Column list
        FRefs : ForeignRef list
        ERefs : EnumRef list
        Uniques : Unique list
}
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

and Column = {
    CName : string
    CType : ColumnType
    Nullable : bool
    Array : bool
    Comment : string option
}

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
