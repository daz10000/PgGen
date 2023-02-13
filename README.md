# PgGen

## Intro
Postgres schema generation, following my own idiosyncratic style (heavily influenced by [PgModeler}(https://pgmodeler.io/)

## Features

Generates SQL for
    - database creation
    - schema creation
    - simple table creation
    - foreign key references, unique constraints

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

- support for indices
- more field types
- generation of boiler plate IO code
- consider parsing a different input format (F# data structures with tags?)

## Random ideas
- auto type selection (id -> int,  name -> text)
- like minimal typing (whitespace indentation seems appealing but YAML bad)
- autonaming fields that refer to other tables

This seems like a good idea from a rapid data entry pov..  Could also allow excel columns for input. 
```
MyDb
    schema1
        table1
            id
            col1
            col2
            ->table2  (could generate an id_table2 field pointing to table2.id)
        table2
            id
            col3

    schema2
        table3
            col4
            col5
        table4
```
