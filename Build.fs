module PgGen.Build

open Common

let defaultOwner = "read_write"
let col (name:string) (cType:ColumnType) (modList:ColumnMatters list) =

    let hasMod (x:ColumnMatters) = modList |> List.contains x

    ColumnItem
        {   CName = name
            CType = cType
            Nullable = hasMod Nullable
            Array = hasMod Array
        }


let table (name:string) (tableAttr:TableAttr list) (body:TableBodyItem list) =
    TableItem
        {   TName = name
            Attributes = tableAttr
            Cols = [ for b in body do
                        match b with
                        | ColumnItem c ->
                            c
                        | _ -> ()
            ]
            Uniques = [ for b in body do
                        match b with
                        | Unique u -> u
                        | _ -> () ]
            FRefs = [ for b in body do
                        match b with
                        | ForeignRef f -> f
                        | _ -> ()
            ]
        }

let schema (name:string) (schemaAttr:SchemaAttr list) (body:SchemaBodyItem list) =
    SchemaItem {
        SName = name
        Tables = [ for t in body do
                    match t with
                    | TableItem t -> t
        ]
    }

let db (name:string) (dbAttr:DBAttr list) (body:DBBodyItem list) =
    {   DName = name
        Schemas = [ for s in body do
                        match s with
                        | SchemaItem s -> s ]
        Owner =
            match (dbAttr
            |> List.tryFind (fun a -> match a with | Owner o -> true) )  with
            | Some(Owner o) -> o
            | _ -> defaultOwner
    }

let unique (cols:string list) =
    Unique {
        Cols = cols }
let frefId (reference:string) =
    let schema, table =
        match reference with
        | Regex @"(.+)\.(.+)" [schema ; table] -> Some schema,table
        | s -> None, s

    ForeignRef
        {   FromTable = None
            FromSchema = None
            FromCols = [$"id_{table}"]
            ToTable = table
            ToSchema = schema
            ToCols = ["id"]
            Generate = true
        }
