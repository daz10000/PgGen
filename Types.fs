namespace PgGen


type Db = { DName : string ; Owner : string ; Schemas : Schema list }
and Schema = { SName : string ; Tables : Table list }

and ForeignRef = {
    FromSchema : string option
    FromTable : string option
    FromCols : string list
    ToTable : string
    ToSchema : string option
    ToCols : string list
    Generate : bool
}
and Unique = {
    Cols : string list
}

and Table = {
        TName : string
        Attributes : TableAttr list
        Cols : Column list
        FRefs : ForeignRef list
        Uniques : Unique list
}
and TableAttr =
    | Comment of string

and ColumnType =
    | Id
    | String
    | Int32
    | Timestamp
    | Jsonb
    member x.Sql() =
        match x with
        | Id -> "int"
        | String -> "text"
        | Int32 -> "int"
        | Timestamp -> "timestamptz default now()"
        | Jsonb -> "jsonb"

and Column = {
    CName : string
    CType : ColumnType
    Nullable : bool
    Array : bool
}

type ColumnMatters =
    | Nullable
    | Array

/// Things in the table itself (cols et al)
type TableBodyItem =
    | ColumnItem of Column
    | Unique of Unique
    | ForeignRef of ForeignRef



type SchemaAttr =
    | TBD  // fix fix

type SchemaBodyItem =
    | TableItem of Table

type DBAttr =
    | Owner of string

type DBBodyItem =
    | SchemaItem of Schema
