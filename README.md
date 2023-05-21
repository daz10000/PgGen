# PgGen

## Intro
Postgres schema generation, following my own idiosyncratic style (heavily influenced by [PgModeler}(https://pgmodeler.io/)

## Features

Generates SQL for
    - database creation
    - schema creation
    - simple table creation
    - foreign key references, unique constraints
Generates code fo
    - CRUD operations
    - Asp.Net / Plough web api endpoints

## Example

```FSharp

let db =
    db "proteins" [ Owner "read_write" ] [
        schema "enzyme" [] [
                table "uniprot_entry" [] [
                                col "id" Id []
                                col "name" String []
                                col "common_name" String [Nullable]
                                col "accno" String []
                                col "secondary" String [Array]
                            ]
                table "organism" [] [
                            col "id"  Id []
                            col "name"  String []
                            col "id_taxon"  Int32 [Nullable]
                            col "common_name"  String [Nullable]
                            col "taxonomy"  String [Nullable ; Array]
                        ]

                table "uniprot_data" [ Comment "largely json structured data"] [
                    col "id" Id []
                    col "keywords" Jsonb [] 
                    col "genes" Jsonb []
                    col "comments" Jsonb []
                    col "features" Jsonb []
                    frefId "uniprot_entry" // add an id_uniprot_entry reference to table proteins.uniprot_entry.id
                ] ] ]

let output = Generate.emitDatabase db

printfn $"{output}"

```

```
+------------------+--------------------------+-------------------------------------------------------------------+
| Column           | Type                     | Modifiers                                                         |
|------------------+--------------------------+-------------------------------------------------------------------|
| id               | integer                  |  not null default nextval('enzyme.uniprot_data_id_seq'::regclass) |
| keywords         | jsonb[]                  |  not null                                                         |
| refs             | jsonb[]                  |  not null                                                         |
| comments         | jsonb[]                  |  not null                                                         |
| genes            | jsonb[]                  |  not null                                                         |
| features         | jsonb[]                  |  not null                                                         |
| created          | timestamp with time zone |  not null default now()                                           |
| updated          | timestamp with time zone |  not null default now()                                           |
| id_uniprot_entry | integer                  |  not null                                                         |
+------------------+--------------------------+-------------------------------------------------------------------+
```


## Todo

- finish CRUD operations
    - update
    - delete
        - soft delete schemes?
    - list all?
    - no update fields (e.g. created)
    - ambient inputs
        - tenant id 
        - user id (not from update operations)
    - dapper / plough query api

- support for indices
- more field types
