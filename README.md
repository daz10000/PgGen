# PgGen

## Intro
Postgres schema generation, following my own idiosyncratic style (heavily influences by [PgModeler}(https://pgmodeler.io/)

## Features

Generates SQL for
    - database creation
    - schema creation
    - simple table creation

## Todo

- referential integrity
- support for indices, unique fields
- simplify creation syntax (heavy F# expressions right now)
- consider parsing a different input format (F# data structures with tags?)
- generation of boiler plate IO code

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


### Example for ref integrity'

CONSTRAINT atype_refers_to_atype_id FOREIGN KEY (id_atype) references enzyme.atype(id)

### Example for unique

CONSTRAINT attribute_signature_unique UNIQUE (signature)
