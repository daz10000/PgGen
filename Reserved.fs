module PgGen.Reserved

let reservedRaw = [|
    ("Key Word","PostgreSQL") ;
    ("A","") ;
    ("ABORT","non-reserved") ;
    ("ABS","") ;
    ("ABSENT","") ;
    ("ABSOLUTE","non-reserved") ;
    ("ACCESS","non-reserved") ;
    ("ACCORDING","") ;
    ("ACOS","") ;
    ("ACTION","non-reserved") ;
    ("ADA","") ;
    ("ADD","non-reserved") ;
    ("ADMIN","non-reserved") ;
    ("AFTER","non-reserved") ;
    ("AGGREGATE","non-reserved") ;
    ("ALL","reserved") ;
    ("ALLOCATE","") ;
    ("ALSO","non-reserved") ;
    ("ALTER","non-reserved") ;
    ("ALWAYS","non-reserved") ;
    ("ANALYSE","reserved") ;
    ("ANALYZE","reserved") ;
    ("AND","reserved") ;
    ("ANY","reserved") ;
    ("ARE","") ;
    ("ARRAY","reserved") ;
    ("ARRAY_AGG","") ;
    ("ARRAY_?MAX_?CARDINALITY","") ;
    ("AS","reserved") ;
    ("ASC","reserved") ;
    ("ASENSITIVE","non-reserved") ;
    ("ASIN","") ;
    ("ASSERTION","non-reserved") ;
    ("ASSIGNMENT","non-reserved") ;
    ("ASYMMETRIC","reserved") ;
    ("AT","non-reserved") ;
    ("ATAN","") ;
    ("ATOMIC","non-reserved") ;
    ("ATTACH","non-reserved") ;
    ("ATTRIBUTE","non-reserved") ;
    ("ATTRIBUTES","") ;
    ("AUTHORIZATION","reserved (can be function or type)") ;
    ("AVG","") ;
    ("BACKWARD","non-reserved") ;
    ("BASE64","") ;
    ("BEFORE","non-reserved") ;
    ("BEGIN","non-reserved") ;
    ("BEGIN_FRAME","") ;
    ("BEGIN_PARTITION","") ;
    ("BERNOULLI","") ;
    ("BETWEEN","non-reserved (cannot be function or type)") ;
    ("BIGINT","non-reserved (cannot be function or type)") ;
    ("BINARY","reserved (can be function or type)") ;
    ("BIT","non-reserved (cannot be function or type)") ;
    ("BIT_LENGTH","") ;
    ("BLOB","") ;
    ("BLOCKED","") ;
    ("BOM","") ;
    ("BOOLEAN","non-reserved (cannot be function or type)") ;
    ("BOTH","reserved") ;
    ("BREADTH","non-reserved") ;
    ("BY","non-reserved") ;
    ("C","") ;
    ("CACHE","non-reserved") ;
    ("CALL","non-reserved") ;
    ("CALLED","non-reserved") ;
    ("CARDINALITY","") ;
    ("CASCADE","non-reserved") ;
    ("CASCADED","non-reserved") ;
    ("CASE","reserved") ;
    ("CAST","reserved") ;
    ("CATALOG","non-reserved") ;
    ("CATALOG_NAME","") ;
    ("CEIL","") ;
    ("CEILING","") ;
    ("CHAIN","non-reserved") ;
    ("CHAINING","") ;
    ("CHAR","non-reserved (cannot be function or type)") ;
    ("CHARACTER","non-reserved (cannot be function or type)") ;
    ("CHARACTERISTICS","non-reserved") ;
    ("CHARACTERS","") ;
    ("CHARACTER_LENGTH","") ;
    ("CHARACTER_?SET_?CATALOG","") ;
    ("CHARACTER_SET_NAME","") ;
    ("CHARACTER_SET_SCHEMA","") ;
    ("CHAR_LENGTH","") ;
    ("CHECK","reserved") ;
    ("CHECKPOINT","non-reserved") ;
    ("CLASS","non-reserved") ;
    ("CLASSIFIER","") ;
    ("CLASS_ORIGIN","") ;
    ("CLOB","") ;
    ("CLOSE","non-reserved") ;
    ("CLUSTER","non-reserved") ;
    ("COALESCE","non-reserved (cannot be function or type)") ;
    ("COBOL","") ;
    ("COLLATE","reserved") ;
    ("COLLATION","reserved (can be function or type)") ;
    ("COLLATION_CATALOG","") ;
    ("COLLATION_NAME","") ;
    ("COLLATION_SCHEMA","") ;
    ("COLLECT","") ;
    ("COLUMN","reserved") ;
    ("COLUMNS","non-reserved") ;
    ("COLUMN_NAME","") ;
    ("COMMAND_FUNCTION","") ;
    ("COMMAND_?FUNCTION_?CODE","") ;
    ("COMMENT","non-reserved") ;
    ("COMMENTS","non-reserved") ;
    ("COMMIT","non-reserved") ;
    ("COMMITTED","non-reserved") ;
    ("COMPRESSION","non-reserved") ;
    ("CONCURRENTLY","reserved (can be function or type)") ;
    ("CONDITION","") ;
    ("CONDITIONAL","") ;
    ("CONDITION_NUMBER","") ;
    ("CONFIGURATION","non-reserved") ;
    ("CONFLICT","non-reserved") ;
    ("CONNECT","") ;
    ("CONNECTION","non-reserved") ;
    ("CONNECTION_NAME","") ;
    ("CONSTRAINT","reserved") ;
    ("CONSTRAINTS","non-reserved") ;
    ("CONSTRAINT_CATALOG","") ;
    ("CONSTRAINT_NAME","") ;
    ("CONSTRAINT_SCHEMA","") ;
    ("CONSTRUCTOR","") ;
    ("CONTAINS","") ;
    ("CONTENT","non-reserved") ;
    ("CONTINUE","non-reserved") ;
    ("CONTROL","") ;
    ("CONVERSION","non-reserved") ;
    ("CONVERT","") ;
    ("COPY","non-reserved") ;
    ("CORR","") ;
    ("CORRESPONDING","") ;
    ("COS","") ;
    ("COSH","") ;
    ("COST","non-reserved") ;
    ("COUNT","") ;
    ("COVAR_POP","") ;
    ("COVAR_SAMP","") ;
    ("CREATE","reserved") ;
    ("CROSS","reserved (can be function or type)") ;
    ("CSV","non-reserved") ;
    ("CUBE","non-reserved") ;
    ("CUME_DIST","") ;
    ("CURRENT","non-reserved") ;
    ("CURRENT_CATALOG","reserved") ;
    ("CURRENT_DATE","reserved") ;
    ("CURRENT_?DEFAULT_?TRANSFORM_?GROUP","") ;
    ("CURRENT_PATH","") ;
    ("CURRENT_ROLE","reserved") ;
    ("CURRENT_ROW","") ;
    ("CURRENT_SCHEMA","reserved (can be function or type)") ;
    ("CURRENT_TIME","reserved") ;
    ("CURRENT_TIMESTAMP","reserved") ;
    ("CURRENT_?TRANSFORM_?GROUP_?FOR_?TYPE","") ;
    ("CURRENT_USER","reserved") ;
    ("CURSOR","non-reserved") ;
    ("CURSOR_NAME","") ;
    ("CYCLE","non-reserved") ;
    ("DATA","non-reserved") ;
    ("DATABASE","non-reserved") ;
    ("DATALINK","") ;
    ("DATE","") ;
    ("DATETIME_?INTERVAL_?CODE","") ;
    ("DATETIME_?INTERVAL_?PRECISION","") ;
    ("DAY","non-reserved") ;
    ("DB","") ;
    ("DEALLOCATE","non-reserved") ;
    ("DEC","non-reserved (cannot be function or type)") ;
    ("DECFLOAT","") ;
    ("DECIMAL","non-reserved (cannot be function or type)") ;
    ("DECLARE","non-reserved") ;
    ("DEFAULT","reserved") ;
    ("DEFAULTS","non-reserved") ;
    ("DEFERRABLE","reserved") ;
    ("DEFERRED","non-reserved") ;
    ("DEFINE","") ;
    ("DEFINED","") ;
    ("DEFINER","non-reserved") ;
    ("DEGREE","") ;
    ("DELETE","non-reserved") ;
    ("DELIMITER","non-reserved") ;
    ("DELIMITERS","non-reserved") ;
    ("DENSE_RANK","") ;
    ("DEPENDS","non-reserved") ;
    ("DEPTH","non-reserved") ;
    ("DEREF","") ;
    ("DERIVED","") ;
    ("DESC","reserved") ;
    ("DESCRIBE","") ;
    ("DESCRIPTOR","") ;
    ("DETACH","non-reserved") ;
    ("DETERMINISTIC","") ;
    ("DIAGNOSTICS","") ;
    ("DICTIONARY","non-reserved") ;
    ("DISABLE","non-reserved") ;
    ("DISCARD","non-reserved") ;
    ("DISCONNECT","") ;
    ("DISPATCH","") ;
    ("DISTINCT","reserved") ;
    ("DLNEWCOPY","") ;
    ("DLPREVIOUSCOPY","") ;
    ("DLURLCOMPLETE","") ;
    ("DLURLCOMPLETEONLY","") ;
    ("DLURLCOMPLETEWRITE","") ;
    ("DLURLPATH","") ;
    ("DLURLPATHONLY","") ;
    ("DLURLPATHWRITE","") ;
    ("DLURLSCHEME","") ;
    ("DLURLSERVER","") ;
    ("DLVALUE","") ;
    ("DO","reserved") ;
    ("DOCUMENT","non-reserved") ;
    ("DOMAIN","non-reserved") ;
    ("DOUBLE","non-reserved") ;
    ("DROP","non-reserved") ;
    ("DYNAMIC","") ;
    ("DYNAMIC_FUNCTION","") ;
    ("DYNAMIC_?FUNCTION_?CODE","") ;
    ("EACH","non-reserved") ;
    ("ELEMENT","") ;
    ("ELSE","reserved") ;
    ("EMPTY","") ;
    ("ENABLE","non-reserved") ;
    ("ENCODING","non-reserved") ;
    ("ENCRYPTED","non-reserved") ;
    ("END","reserved") ;
    ("END-EXEC","") ;
    ("END_FRAME","") ;
    ("END_PARTITION","") ;
    ("ENFORCED","") ;
    ("ENUM","non-reserved") ;
    ("EQUALS","") ;
    ("ERROR","") ;
    ("ESCAPE","non-reserved") ;
    ("EVENT","non-reserved") ;
    ("EVERY","") ;
    ("EXCEPT","reserved") ;
    ("EXCEPTION","") ;
    ("EXCLUDE","non-reserved") ;
    ("EXCLUDING","non-reserved") ;
    ("EXCLUSIVE","non-reserved") ;
    ("EXEC","") ;
    ("EXECUTE","non-reserved") ;
    ("EXISTS","non-reserved (cannot be function or type)") ;
    ("EXP","") ;
    ("EXPLAIN","non-reserved") ;
    ("EXPRESSION","non-reserved") ;
    ("EXTENSION","non-reserved") ;
    ("EXTERNAL","non-reserved") ;
    ("EXTRACT","non-reserved (cannot be function or type)") ;
    ("FALSE","reserved") ;
    ("FAMILY","non-reserved") ;
    ("FETCH","reserved") ;
    ("FILE","") ;
    ("FILTER","non-reserved") ;
    ("FINAL","") ;
    ("FINALIZE","non-reserved") ;
    ("FINISH","") ;
    ("FIRST","non-reserved") ;
    ("FIRST_VALUE","") ;
    ("FLAG","") ;
    ("FLOAT","non-reserved (cannot be function or type)") ;
    ("FLOOR","") ;
    ("FOLLOWING","non-reserved") ;
    ("FOR","reserved") ;
    ("FORCE","non-reserved") ;
    ("FOREIGN","reserved") ;
    ("FORMAT","") ;
    ("FORTRAN","") ;
    ("FORWARD","non-reserved") ;
    ("FOUND","") ;
    ("FRAME_ROW","") ;
    ("FREE","") ;
    ("FREEZE","reserved (can be function or type)") ;
    ("FROM","reserved") ;
    ("FS","") ;
    ("FULFILL","") ;
    ("FULL","reserved (can be function or type)") ;
    ("FUNCTION","non-reserved") ;
    ("FUNCTIONS","non-reserved") ;
    ("FUSION","") ;
    ("G","") ;
    ("GENERAL","") ;
    ("GENERATED","non-reserved") ;
    ("GET","") ;
    ("GLOBAL","non-reserved") ;
    ("GO","") ;
    ("GOTO","") ;
    ("GRANT","reserved") ;
    ("GRANTED","non-reserved") ;
    ("GREATEST","non-reserved (cannot be function or type)") ;
    ("GROUP","reserved") ;
    ("GROUPING","non-reserved (cannot be function or type)") ;
    ("GROUPS","non-reserved") ;
    ("HANDLER","non-reserved") ;
    ("HAVING","reserved") ;
    ("HEADER","non-reserved") ;
    ("HEX","") ;
    ("HIERARCHY","") ;
    ("HOLD","non-reserved") ;
    ("HOUR","non-reserved") ;
    ("ID","") ;
    ("IDENTITY","non-reserved") ;
    ("IF","non-reserved") ;
    ("IGNORE","") ;
    ("ILIKE","reserved (can be function or type)") ;
    ("IMMEDIATE","non-reserved") ;
    ("IMMEDIATELY","") ;
    ("IMMUTABLE","non-reserved") ;
    ("IMPLEMENTATION","") ;
    ("IMPLICIT","non-reserved") ;
    ("IMPORT","non-reserved") ;
    ("IN","reserved") ;
    ("INCLUDE","non-reserved") ;
    ("INCLUDING","non-reserved") ;
    ("INCREMENT","non-reserved") ;
    ("INDENT","") ;
    ("INDEX","non-reserved") ;
    ("INDEXES","non-reserved") ;
    ("INDICATOR","") ;
    ("INHERIT","non-reserved") ;
    ("INHERITS","non-reserved") ;
    ("INITIAL","") ;
    ("INITIALLY","reserved") ;
    ("INLINE","non-reserved") ;
    ("INNER","reserved (can be function or type)") ;
    ("INOUT","non-reserved (cannot be function or type)") ;
    ("INPUT","non-reserved") ;
    ("INSENSITIVE","non-reserved") ;
    ("INSERT","non-reserved") ;
    ("INSTANCE","") ;
    ("INSTANTIABLE","") ;
    ("INSTEAD","non-reserved") ;
    ("INT","non-reserved (cannot be function or type)") ;
    ("INTEGER","non-reserved (cannot be function or type)") ;
    ("INTEGRITY","") ;
    ("INTERSECT","reserved") ;
    ("INTERSECTION","") ;
    ("INTERVAL","non-reserved (cannot be function or type)") ;
    ("INTO","reserved") ;
    ("INVOKER","non-reserved") ;
    ("IS","reserved (can be function or type)") ;
    ("ISNULL","reserved (can be function or type)") ;
    ("ISOLATION","non-reserved") ;
    ("JOIN","reserved (can be function or type)") ;
    ("JSON_ARRAY","") ;
    ("JSON_ARRAYAGG","") ;
    ("JSON_EXISTS","") ;
    ("JSON_OBJECT","") ;
    ("JSON_OBJECTAGG","") ;
    ("JSON_QUERY","") ;
    ("JSON_TABLE","") ;
    ("JSON_TABLE_PRIMITIVE","") ;
    ("JSON_VALUE","") ;
    ("K","") ;
    ("KEEP","") ;
    ("KEY","non-reserved") ;
    ("KEYS","") ;
    ("KEY_MEMBER","") ;
    ("KEY_TYPE","") ;
    ("LABEL","non-reserved") ;
    ("LAG","") ;
    ("LANGUAGE","non-reserved") ;
    ("LARGE","non-reserved") ;
    ("LAST","non-reserved") ;
    ("LAST_VALUE","") ;
    ("LATERAL","reserved") ;
    ("LEAD","") ;
    ("LEADING","reserved") ;
    ("LEAKPROOF","non-reserved") ;
    ("LEAST","non-reserved (cannot be function or type)") ;
    ("LEFT","reserved (can be function or type)") ;
    ("LENGTH","") ;
    ("LEVEL","non-reserved") ;
    ("LIBRARY","") ;
    ("LIKE","reserved (can be function or type)") ;
    ("LIKE_REGEX","") ;
    ("LIMIT","reserved") ;
    ("LINK","") ;
    ("LISTAGG","") ;
    ("LISTEN","non-reserved") ;
    ("LN","") ;
    ("LOAD","non-reserved") ;
    ("LOCAL","non-reserved") ;
    ("LOCALTIME","reserved") ;
    ("LOCALTIMESTAMP","reserved") ;
    ("LOCATION","non-reserved") ;
    ("LOCATOR","") ;
    ("LOCK","non-reserved") ;
    ("LOCKED","non-reserved") ;
    ("LOG","") ;
    ("LOG10","") ;
    ("LOGGED","non-reserved") ;
    ("LOWER","") ;
    ("M","") ;
    ("MAP","") ;
    ("MAPPING","non-reserved") ;
    ("MATCH","non-reserved") ;
    ("MATCHED","non-reserved") ;
    ("MATCHES","") ;
    ("MATCH_NUMBER","") ;
    ("MATCH_RECOGNIZE","") ;
    ("MATERIALIZED","non-reserved") ;
    ("MAX","") ;
    ("MAXVALUE","non-reserved") ;
    ("MEASURES","") ;
    ("MEMBER","") ;
    ("MERGE","non-reserved") ;
    ("MESSAGE_LENGTH","") ;
    ("MESSAGE_OCTET_LENGTH","") ;
    ("MESSAGE_TEXT","") ;
    ("METHOD","non-reserved") ;
    ("MIN","") ;
    ("MINUTE","non-reserved") ;
    ("MINVALUE","non-reserved") ;
    ("MOD","") ;
    ("MODE","non-reserved") ;
    ("MODIFIES","") ;
    ("MODULE","") ;
    ("MONTH","non-reserved") ;
    ("MORE","") ;
    ("MOVE","non-reserved") ;
    ("MULTISET","") ;
    ("MUMPS","") ;
    ("NAME","non-reserved") ;
    ("NAMES","non-reserved") ;
    ("NAMESPACE","") ;
    ("NATIONAL","non-reserved (cannot be function or type)") ;
    ("NATURAL","reserved (can be function or type)") ;
    ("NCHAR","non-reserved (cannot be function or type)") ;
    ("NCLOB","") ;
    ("NESTED","") ;
    ("NESTING","") ;
    ("NEW","non-reserved") ;
    ("NEXT","non-reserved") ;
    ("NFC","non-reserved") ;
    ("NFD","non-reserved") ;
    ("NFKC","non-reserved") ;
    ("NFKD","non-reserved") ;
    ("NIL","") ;
    ("NO","non-reserved") ;
    ("NONE","non-reserved (cannot be function or type)") ;
    ("NORMALIZE","non-reserved (cannot be function or type)") ;
    ("NORMALIZED","non-reserved") ;
    ("NOT","reserved") ;
    ("NOTHING","non-reserved") ;
    ("NOTIFY","non-reserved") ;
    ("NOTNULL","reserved (can be function or type)") ;
    ("NOWAIT","non-reserved") ;
    ("NTH_VALUE","") ;
    ("NTILE","") ;
    ("NULL","reserved") ;
    ("NULLABLE","") ;
    ("NULLIF","non-reserved (cannot be function or type)") ;
    ("NULLS","non-reserved") ;
    ("NULL_ORDERING","") ;
    ("NUMBER","") ;
    ("NUMERIC","non-reserved (cannot be function or type)") ;
    ("OBJECT","non-reserved") ;
    ("OCCURRENCE","") ;
    ("OCCURRENCES_REGEX","") ;
    ("OCTETS","") ;
    ("OCTET_LENGTH","") ;
    ("OF","non-reserved") ;
    ("OFF","non-reserved") ;
    ("OFFSET","reserved") ;
    ("OIDS","non-reserved") ;
    ("OLD","non-reserved") ;
    ("OMIT","") ;
    ("ON","reserved") ;
    ("ONE","") ;
    ("ONLY","reserved") ;
    ("OPEN","") ;
    ("OPERATOR","non-reserved") ;
    ("OPTION","non-reserved") ;
    ("OPTIONS","non-reserved") ;
    ("OR","reserved") ;
    ("ORDER","reserved") ;
    ("ORDERING","") ;
    ("ORDINALITY","non-reserved") ;
    ("OTHERS","non-reserved") ;
    ("OUT","non-reserved (cannot be function or type)") ;
    ("OUTER","reserved (can be function or type)") ;
    ("OUTPUT","") ;
    ("OVER","non-reserved") ;
    ("OVERFLOW","") ;
    ("OVERLAPS","reserved (can be function or type)") ;
    ("OVERLAY","non-reserved (cannot be function or type)") ;
    ("OVERRIDING","non-reserved") ;
    ("OWNED","non-reserved") ;
    ("OWNER","non-reserved") ;
    ("P","") ;
    ("PAD","") ;
    ("PARALLEL","non-reserved") ;
    ("PARAMETER","non-reserved") ;
    ("PARAMETER_MODE","") ;
    ("PARAMETER_NAME","") ;
    ("PARAMETER_?ORDINAL_?POSITION","") ;
    ("PARAMETER_?SPECIFIC_?CATALOG","") ;
    ("PARAMETER_?SPECIFIC_?NAME","") ;
    ("PARAMETER_?SPECIFIC_?SCHEMA","") ;
    ("PARSER","non-reserved") ;
    ("PARTIAL","non-reserved") ;
    ("PARTITION","non-reserved") ;
    ("PASCAL","") ;
    ("PASS","") ;
    ("PASSING","non-reserved") ;
    ("PASSTHROUGH","") ;
    ("PASSWORD","non-reserved") ;
    ("PAST","") ;
    ("PATH","") ;
    ("PATTERN","") ;
    ("PER","") ;
    ("PERCENT","") ;
    ("PERCENTILE_CONT","") ;
    ("PERCENTILE_DISC","") ;
    ("PERCENT_RANK","") ;
    ("PERIOD","") ;
    ("PERMISSION","") ;
    ("PERMUTE","") ;
    ("PIPE","") ;
    ("PLACING","reserved") ;
    ("PLAN","") ;
    ("PLANS","non-reserved") ;
    ("PLI","") ;
    ("POLICY","non-reserved") ;
    ("PORTION","") ;
    ("POSITION","non-reserved (cannot be function or type)") ;
    ("POSITION_REGEX","") ;
    ("POWER","") ;
    ("PRECEDES","") ;
    ("PRECEDING","non-reserved") ;
    ("PRECISION","non-reserved (cannot be function or type)") ;
    ("PREPARE","non-reserved") ;
    ("PREPARED","non-reserved") ;
    ("PRESERVE","non-reserved") ;
    ("PREV","") ;
    ("PRIMARY","reserved") ;
    ("PRIOR","non-reserved") ;
    ("PRIVATE","") ;
    ("PRIVILEGES","non-reserved") ;
    ("PROCEDURAL","non-reserved") ;
    ("PROCEDURE","non-reserved") ;
    ("PROCEDURES","non-reserved") ;
    ("PROGRAM","non-reserved") ;
    ("PRUNE","") ;
    ("PTF","") ;
    ("PUBLIC","") ;
    ("PUBLICATION","non-reserved") ;
    ("QUOTE","non-reserved") ;
    ("QUOTES","") ;
    ("RANGE","non-reserved") ;
    ("RANK","") ;
    ("READ","non-reserved") ;
    ("READS","") ;
    ("REAL","non-reserved (cannot be function or type)") ;
    ("REASSIGN","non-reserved") ;
    ("RECHECK","non-reserved") ;
    ("RECOVERY","") ;
    ("RECURSIVE","non-reserved") ;
    ("REF","non-reserved") ;
    ("REFERENCES","reserved") ;
    ("REFERENCING","non-reserved") ;
    ("REFRESH","non-reserved") ;
    ("REGR_AVGX","") ;
    ("REGR_AVGY","") ;
    ("REGR_COUNT","") ;
    ("REGR_INTERCEPT","") ;
    ("REGR_R2","") ;
    ("REGR_SLOPE","") ;
    ("REGR_SXX","") ;
    ("REGR_SXY","") ;
    ("REGR_SYY","") ;
    ("REINDEX","non-reserved") ;
    ("RELATIVE","non-reserved") ;
    ("RELEASE","non-reserved") ;
    ("RENAME","non-reserved") ;
    ("REPEATABLE","non-reserved") ;
    ("REPLACE","non-reserved") ;
    ("REPLICA","non-reserved") ;
    ("REQUIRING","") ;
    ("RESET","non-reserved") ;
    ("RESPECT","") ;
    ("RESTART","non-reserved") ;
    ("RESTORE","") ;
    ("RESTRICT","non-reserved") ;
    ("RESULT","") ;
    ("RETURN","non-reserved") ;
    ("RETURNED_CARDINALITY","") ;
    ("RETURNED_LENGTH","") ;
    ("RETURNED_?OCTET_?LENGTH","") ;
    ("RETURNED_SQLSTATE","") ;
    ("RETURNING","reserved") ;
    ("RETURNS","non-reserved") ;
    ("REVOKE","non-reserved") ;
    ("RIGHT","reserved (can be function or type)") ;
    ("ROLE","non-reserved") ;
    ("ROLLBACK","non-reserved") ;
    ("ROLLUP","non-reserved") ;
    ("ROUTINE","non-reserved") ;
    ("ROUTINES","non-reserved") ;
    ("ROUTINE_CATALOG","") ;
    ("ROUTINE_NAME","") ;
    ("ROUTINE_SCHEMA","") ;
    ("ROW","non-reserved (cannot be function or type)") ;
    ("ROWS","non-reserved") ;
    ("ROW_COUNT","") ;
    ("ROW_NUMBER","") ;
    ("RULE","non-reserved") ;
    ("RUNNING","") ;
    ("SAVEPOINT","non-reserved") ;
    ("SCALAR","") ;
    ("SCALE","") ;
    ("SCHEMA","non-reserved") ;
    ("SCHEMAS","non-reserved") ;
    ("SCHEMA_NAME","") ;
    ("SCOPE","") ;
    ("SCOPE_CATALOG","") ;
    ("SCOPE_NAME","") ;
    ("SCOPE_SCHEMA","") ;
    ("SCROLL","non-reserved") ;
    ("SEARCH","non-reserved") ;
    ("SECOND","non-reserved") ;
    ("SECTION","") ;
    ("SECURITY","non-reserved") ;
    ("SEEK","") ;
    ("SELECT","reserved") ;
    ("SELECTIVE","") ;
    ("SELF","") ;
    ("SEMANTICS","") ;
    ("SENSITIVE","") ;
    ("SEQUENCE","non-reserved") ;
    ("SEQUENCES","non-reserved") ;
    ("SERIALIZABLE","non-reserved") ;
    ("SERVER","non-reserved") ;
    ("SERVER_NAME","") ;
    ("SESSION","non-reserved") ;
    ("SESSION_USER","reserved") ;
    ("SET","non-reserved") ;
    ("SETOF","non-reserved (cannot be function or type)") ;
    ("SETS","non-reserved") ;
    ("SHARE","non-reserved") ;
    ("SHOW","non-reserved") ;
    ("SIMILAR","reserved (can be function or type)") ;
    ("SIMPLE","non-reserved") ;
    ("SIN","") ;
    ("SINH","") ;
    ("SIZE","") ;
    ("SKIP","non-reserved") ;
    ("SMALLINT","non-reserved (cannot be function or type)") ;
    ("SNAPSHOT","non-reserved") ;
    ("SOME","reserved") ;
    ("SORT_DIRECTION","") ;
    ("SOURCE","") ;
    ("SPACE","") ;
    ("SPECIFIC","") ;
    ("SPECIFICTYPE","") ;
    ("SPECIFIC_NAME","") ;
    ("SQL","non-reserved") ;
    ("SQLCODE","") ;
    ("SQLERROR","") ;
    ("SQLEXCEPTION","") ;
    ("SQLSTATE","") ;
    ("SQLWARNING","") ;
    ("SQRT","") ;
    ("STABLE","non-reserved") ;
    ("STANDALONE","non-reserved") ;
    ("START","non-reserved") ;
    ("STATE","") ;
    ("STATEMENT","non-reserved") ;
    ("STATIC","") ;
    ("STATISTICS","non-reserved") ;
    ("STDDEV_POP","") ;
    ("STDDEV_SAMP","") ;
    ("STDIN","non-reserved") ;
    ("STDOUT","non-reserved") ;
    ("STORAGE","non-reserved") ;
    ("STORED","non-reserved") ;
    ("STRICT","non-reserved") ;
    ("STRING","") ;
    ("STRIP","non-reserved") ;
    ("STRUCTURE","") ;
    ("STYLE","") ;
    ("SUBCLASS_ORIGIN","") ;
    ("SUBMULTISET","") ;
    ("SUBSCRIPTION","non-reserved") ;
    ("SUBSET","") ;
    ("SUBSTRING","non-reserved (cannot be function or type)") ;
    ("SUBSTRING_REGEX","") ;
    ("SUCCEEDS","") ;
    ("SUM","") ;
    ("SUPPORT","non-reserved") ;
    ("SYMMETRIC","reserved") ;
    ("SYSID","non-reserved") ;
    ("SYSTEM","non-reserved") ;
    ("SYSTEM_TIME","") ;
    ("SYSTEM_USER","") ;
    ("T","") ;
    ("TABLE","reserved") ;
    ("TABLES","non-reserved") ;
    ("TABLESAMPLE","reserved (can be function or type)") ;
    ("TABLESPACE","non-reserved") ;
    ("TABLE_NAME","") ;
    ("TAN","") ;
    ("TANH","") ;
    ("TEMP","non-reserved") ;
    ("TEMPLATE","non-reserved") ;
    ("TEMPORARY","non-reserved") ;
    ("TEXT","non-reserved") ;
    ("THEN","reserved") ;
    ("THROUGH","") ;
    ("TIES","non-reserved") ;
    ("TIME","non-reserved (cannot be function or type)") ;
    ("TIMESTAMP","non-reserved (cannot be function or type)") ;
    ("TIMEZONE_HOUR","") ;
    ("TIMEZONE_MINUTE","") ;
    ("TO","reserved") ;
    ("TOKEN","") ;
    ("TOP_LEVEL_COUNT","") ;
    ("TRAILING","reserved") ;
    ("TRANSACTION","non-reserved") ;
    ("TRANSACTIONS_?COMMITTED","") ;
    ("TRANSACTIONS_?ROLLED_?BACK","") ;
    ("TRANSACTION_ACTIVE","") ;
    ("TRANSFORM","non-reserved") ;
    ("TRANSFORMS","") ;
    ("TRANSLATE","") ;
    ("TRANSLATE_REGEX","") ;
    ("TRANSLATION","") ;
    ("TREAT","non-reserved (cannot be function or type)") ;
    ("TRIGGER","non-reserved") ;
    ("TRIGGER_CATALOG","") ;
    ("TRIGGER_NAME","") ;
    ("TRIGGER_SCHEMA","") ;
    ("TRIM","non-reserved (cannot be function or type)") ;
    ("TRIM_ARRAY","") ;
    ("TRUE","reserved") ;
    ("TRUNCATE","non-reserved") ;
    ("TRUSTED","non-reserved") ;
    ("TYPE","non-reserved") ;
    ("TYPES","non-reserved") ;
    ("UESCAPE","non-reserved") ;
    ("UNBOUNDED","non-reserved") ;
    ("UNCOMMITTED","non-reserved") ;
    ("UNCONDITIONAL","") ;
    ("UNDER","") ;
    ("UNENCRYPTED","non-reserved") ;
    ("UNION","reserved") ;
    ("UNIQUE","reserved") ;
    ("UNKNOWN","non-reserved") ;
    ("UNLINK","") ;
    ("UNLISTEN","non-reserved") ;
    ("UNLOGGED","non-reserved") ;
    ("UNMATCHED","") ;
    ("UNNAMED","") ;
    ("UNNEST","") ;
    ("UNTIL","non-reserved") ;
    ("UNTYPED","") ;
    ("UPDATE","non-reserved") ;
    ("UPPER","") ;
    ("URI","") ;
    ("USAGE","") ;
    ("USER","reserved") ;
    ("USER_?DEFINED_?TYPE_?CATALOG","") ;
    ("USER_?DEFINED_?TYPE_?CODE","") ;
    ("USER_?DEFINED_?TYPE_?NAME","") ;
    ("USER_?DEFINED_?TYPE_?SCHEMA","") ;
    ("USING","reserved") ;
    ("UTF16","") ;
    ("UTF32","") ;
    ("UTF8","") ;
    ("VACUUM","non-reserved") ;
    ("VALID","non-reserved") ;
    ("VALIDATE","non-reserved") ;
    ("VALIDATOR","non-reserved") ;
    ("VALUE","non-reserved") ;
    ("VALUES","non-reserved (cannot be function or type)") ;
    ("VALUE_OF","") ;
    ("VARBINARY","") ;
    ("VARCHAR","non-reserved (cannot be function or type)") ;
    ("VARIADIC","reserved") ;
    ("VARYING","non-reserved") ;
    ("VAR_POP","") ;
    ("VAR_SAMP","") ;
    ("VERBOSE","reserved (can be function or type)") ;
    ("VERSION","non-reserved") ;
    ("VERSIONING","") ;
    ("VIEW","non-reserved") ;
    ("VIEWS","non-reserved") ;
    ("VOLATILE","non-reserved") ;
    ("WHEN","reserved") ;
    ("WHENEVER","") ;
    ("WHERE","reserved") ;
    ("WHITESPACE","non-reserved") ;
    ("WIDTH_BUCKET","") ;
    ("WINDOW","reserved") ;
    ("WITH","reserved") ;
    ("WITHIN","non-reserved") ;
    ("WITHOUT","non-reserved") ;
    ("WORK","non-reserved") ;
    ("WRAPPER","non-reserved") ;
    ("WRITE","non-reserved") ;
    ("XML","non-reserved") ;
    ("XMLAGG","") ;
    ("XMLATTRIBUTES","non-reserved (cannot be function or type)") ;
    ("XMLBINARY","") ;
    ("XMLCAST","") ;
    ("XMLCOMMENT","") ;
    ("XMLCONCAT","non-reserved (cannot be function or type)") ;
    ("XMLDECLARATION","") ;
    ("XMLDOCUMENT","") ;
    ("XMLELEMENT","non-reserved (cannot be function or type)") ;
    ("XMLEXISTS","non-reserved (cannot be function or type)") ;
    ("XMLFOREST","non-reserved (cannot be function or type)") ;
    ("XMLITERATE","") ;
    ("XMLNAMESPACES","non-reserved (cannot be function or type)") ;
    ("XMLPARSE","non-reserved (cannot be function or type)") ;
    ("XMLPI","non-reserved (cannot be function or type)") ;
    ("XMLQUERY","") ;
    ("XMLROOT","non-reserved (cannot be function or type)") ;
    ("XMLSCHEMA","") ;
    ("XMLSERIALIZE","non-reserved (cannot be function or type)") ;
    ("XMLTABLE","non-reserved (cannot be function or type)") ;
    ("XMLTEXT","") ;
    ("XMLVALIDATE","") ;
    ("YEAR","non-reserved") ;
    ("YES","non-reserved") ;
    ("ZONE","non-reserved")
|]

let reserved =
    reservedRaw
    |> Array.choose (fun (n,t) -> if t.StartsWith "reserved" then Some (n.ToLower()) else None)
    |> Set.ofArray

let reservedFSharp =
    [
    "abstract"
    "and"
    "as"
    "assert"
    "base"
    "begin"
    "class"
    "default"
    "delegate"
    "do"
    "done"
    "downcast"
    "downto"
    "elif"
    "else"
    "end"
    "exception"
    "extern"
    "FALSE"
    "finally"
    "fixed"
    "for"
    "fun"
    "function"
    "global"
    "if"
    "in"
    "inherit"
    "inline"
    "interface"
    "internal"
    "lazy"
    "let"
    "match"
    "member"
    "module"
    "mutable"
    "namespace"
    "new"
    "not"
    "null"
    "of"
    "open"
    "or"
    "override"
    "private"
    "public"
    "rec"
    "return"
    "select"
    "static"
    "struct"
    "then"
    "to"
    "TRUE"
    "try"
    "type"
    "upcast"
    "use"
    "val"
    "void"
    "when"
    "while"
    "with"
    "yield"
    "const"
    "asr"
    "land"
    "lor"
    "lsl"
    "lsr"
    "lxor"
    "mod"
    "sig"
    "break"
    "checked"
    "component"
    "const"
    "constraint"
    "continue"
    "event"
    "external"
    "include"
    "mixin"
    "parallel"
    "process"
    "protected"
    "pure"
    "sealed"
    "tailcall"
    "trait"
    "virtual"] |> Set.ofList

let isReserved (s:string) = reserved.Contains (s.ToLower()) || reservedFSharp.Contains (s.ToLower())
