namespace PgGen


type Db = { DName : string ; Owner : string ; Schemas : Schema list }
and Schema = { SName : string ; Tables : Table list }

and Table = {
        TName : string
        Cols : Column list
}
and ColumnType =
    | Id
    | String
    | Int32
    | Timestamp
    member x.Sql() =
        match x with
        | Id -> "int"
        | String -> "text"
        | Int32 -> "int"
        | Timestamp -> "timestamptz default now()"

and Column = {
    CName : string
    CType : ColumnType
    Nullable : bool
    Array : bool
}
