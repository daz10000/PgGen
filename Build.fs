module PgGen.Build

open Common

let defaultOwner = "read_write"
let col (name:string) (cType:ColumnType) (modList:ColumnMatters list) =

    let hasMod (x:ColumnMatters) = modList |> List.contains x

    let comment =
        match modList |> List.filter (fun a -> match a with | CComment _ -> true | _ -> false) with
        | [CComment c] ->  Some c
        | [] -> None
        | x -> failwithf "Multiple comments for column {name} {x}"

    ColumnItem
        {   CName = name
            CType = cType
            Nullable = hasMod Nullable
            Array = hasMod Array
            Comment = comment
        }


let table (name:string) (tableAttr:TableAttr list) (body:TableBodyItem list) =
    let comment =
        match tableAttr |> List.filter (fun a -> match a with | Comment _ -> true | _ -> false) with
        | [Comment c] ->  Some c
        | [] -> None
        | x -> failwithf "Multiple comments for table {name} {x}"

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
            ERefs = [ for e in body do
                            match e with
                            | EnumRef e -> e
                            | _ -> ()
            ]
            Comment = comment
        }

let schema (name:string) (schemaAttr:SchemaAttr list) (body:SchemaBodyItem list) =

    let comment =
        schemaAttr
        |> List.tryFind (fun a -> match a with | SComment _ -> true | _ -> false)
        |> Option.map (fun c  -> match c with | SComment c -> c | _ -> failwithf "impossible")

    let enums = [ for b in body do
                    match b with
                    | PEnum e -> e
                    | _ -> ()
    ]
    let tables = [ for t in body do
                        match t with
                        | TableItem t -> yield t
                        | _ -> ()]
    SchemaItem {
        SName = name
        Tables = tables
        Comment = comment
        Enums = enums
    }

let db (name:string) (dbAttr:DBAttr list) (body:DBBodyItem list) =
    let comment =
        match dbAttr |> List.filter (fun a -> match a with | DComment _ -> true | _ -> false) with
        | [DComment c] ->  Some c
        | [] -> None
        | x -> failwithf $"Multiple comments for db {name} {x}"

    {   DName = name
        Schemas = [ for s in body do
                        match s with
                        | SchemaItem s -> s ]
        Owner =
            match (dbAttr
            |> List.tryFind (fun a -> match a with | Owner o -> true | _ -> false) )  with
            | Some(Owner o) -> o
            | _ -> defaultOwner
        Comment = comment
    }

let unique (cols:string list) =
    Unique {
        Cols = cols }
let frefId (reference:string) (attrs:FRefAttr list) =

    let name =
        match List.filter (fun a -> match a with | FName n -> true | _ -> false) attrs with
        | [FName n] -> Some n
        | [] -> None
        | x -> failwithf $"frefId: expected one name, got multiple {x}"

    let schema, table =
        match reference with
        | Regex @"(.+)\.(.+)" [schema ; table] -> Some schema,table
        | s -> None, s

    ForeignRef
        {   FromTable = None
            FromSchema = None
            FromCols = [name |> Option.defaultValue $"id_{table}"]
            ToTable = table
            ToSchema = schema
            ToCols = ["id"]
            Generate = true
            Name = name
            IsNullable = attrs |> List.exists (fun a -> match a with | FNullable -> true | _ -> false)
        }

let enumDef (name:string) (attrs:EAttr list) =
    let comment =
        match attrs |> List.filter (fun a -> match a with | EComment _ -> true | _ -> false) with
        | [EComment c] ->  Some c
        | [] -> None
        | x -> failwithf $"Multiple comments for enum {name} {x}"
    PEnum
        {   EName = name
            EValues = [ for a in attrs do
                        match a with
                        | Member v -> yield v
                        | _ ->()
            ]
            EComment = comment
        }

let enum (name:string) (attrs:EnumRefAttr list) =
    let schema, enum =
        match name with
        | Regex @"(.+)\.(.+)" [schema ; enum] -> Some schema,enum
        | s -> None, s

    let name =
        match attrs |> List.filter (fun a -> match a with | EName n -> true | _ -> false) with
        | [EName n] -> Some n
        | [] -> None
        | x -> failwithf $"enum: expected one name, got multiple {x}"

    EnumRef
        {   EName = enum
            ESchema = schema
            Name = name
            IsNullable = attrs |> List.exists (fun a -> match a with | ENullable -> true | _ -> false)
            Generate = true // only option right now
        }
