# BIDF `(NEW)` Column Rename – Method History

## Context

A BIDF file (SQLite database, extension `.bidf`) was rejected because it was
not compliant with the BIDF specification. The cause: certain table columns
had a literal ` (NEW)` / `(NEW)` suffix baked into their column names
(e.g. `platform (NEW)`, `theadid(NEW)`). These suffixes are not part of the
BIDF schema and must be removed so the column names match the spec.

> The `(NEW)` suffix marks columns that were recently added by a newer tool
> version. When the file is read by an older tool / a strict validator, the
> names are unrecognized and the file is rejected.

## Files involved (Example: `Template_V1_5.bidf`)

- **Source:** `Template_V1_5.bidf`
- **Output:** `Template1_V1_5.bidf` (same folder, same format, unchanged data)

## What is done

For **every table** in the SQLite database, every column whose name contains
`(NEW)` is **renamed** by stripping the ` (NEW)` or `(NEW)` suffix from the
**column name only**. The column itself, its type, default value, NOT NULL
flag, primary key and **all of its row data are left completely unchanged** –
only the name string changes. Columns are **never** removed.

## Method (step by step)

1. **Copy the source file** to the new target file name so the original is
   never touched.
2. **Enumerate tables** via `sqlite_master` (skip the internal
   `sqlite_sequence` table).
3. For each table, read `PRAGMA table_info(<table>)` to get every column with
   its name, type, NOT NULL flag, default value and PK index.
4. Compute the **new column name**:
   `new_name = old_name.replace(' (NEW)', '').replace('(NEW)', '')`
   (handles both the spaced and unspaced variants).
5. Rebuild the table (SQLite cannot rename/remove a column in place before
   v3.35 in all cases, so we use the standard "recreate" approach):
   - `CREATE TABLE <table>_tmp ( ...columns with new names, types, defaults,
     NOT NULL, PRIMARY KEY... )`
   - `INSERT INTO <table>_tmp (<cols>) SELECT <cols> FROM <table>`
   - `DROP TABLE <table>`
   - `ALTER TABLE <table>_tmp RENAME TO <table>`
6. Continue until all tables are processed, then close the connection.

## Key script (portable)

```python
import sqlite3, shutil

SRC = r'PATH_TO_SOURCE.bidf'
DST = r'PATH_TO_OUTPUT.bidf'

shutil.copy(SRC, DST)

con = sqlite3.connect(DST)
con.isolation_level = None
cur = con.cursor()

cur.execute("SELECT name FROM sqlite_master WHERE type='table'")
tables = [r[0] for r in cur.fetchall()]

SUFFIXES = [' (NEW)', '(NEW)']   # spaced and unspaced variants

for t in tables:
    if t == 'sqlite_sequence':
        continue
    cur.execute("PRAGMA table_info(%s)" % t)
    cols = cur.fetchall()

    newcols = []
    changed = False
    for c in cols:
        old = c[1]
        new = old
        for s in SUFFIXES:
            new = new.replace(s, '')
        if new != old:
            changed = True
        coltype = c[2]
        notnull = " NOT NULL" if c[3] else ""
        defv = ""
        if c[4] is not None:
            defv = " DEFAULT " + (c[4] if isinstance(c[4], str) else repr(c[4]))
        newcols.append((old, new, coltype, notnull, defv, c[5]))

    if not changed:
        continue

    cols_sql = ", ".join('"%s" %s%s%s' % (nn, ct, nf, dv)
                         for (_, nn, ct, nf, dv, _) in newcols)
    pk = [nn for (_, nn, _, _, _, pi) in newcols if pi > 0]
    if pk:
        cols_sql += ", PRIMARY KEY (%s)" % ", ".join('"%s"' % p for p in pk)

    tmp = t + "_tmp"
    cur.execute("CREATE TABLE %s (%s)" % (tmp, cols_sql))
    sel = ", ".join('"%s"' % nn for (_, nn, _, _, _, _) in newcols)
    cur.execute("INSERT INTO %s (%s) SELECT %s FROM %s" % (tmp, sel, sel, t))
    cur.execute("DROP TABLE %s" % t)
    cur.execute("ALTER TABLE %s RENAME TO %s" % (tmp, t))

con.close()
print("DONE")
```

## Verification (run after the migration)

```python
import sqlite3
con = sqlite3.connect(DST)
cur = con.cursor()
found = []
cur.execute("SELECT name FROM sqlite_master WHERE type='table'")
for t in [r[0] for r in cur.fetchall()]:
    cur.execute("PRAGMA table_info(%s)" % t)
    for c in cur.fetchall():
        if '(NEW)' in c[1]:
            found.append((t, c[1]))
assert not found, ("(NEW) columns still present: %r" % found)
print("No (NEW) columns left. Integrity:",
      cur.execute("PRAGMA integrity_check").fetchone()[0])
con.close()
```

## Porting to another project

1. Point `SRC` / `DST` at the new source and desired output file paths.
2. Run the script with `python` (Python 3 with the built-in `sqlite3` module –
   no third-party dependencies).
3. This script **only renames** columns by stripping the `(NEW)` / ` (NEW)`
   suffix from the name; it never drops columns, data, or tables. If a
   different cleanup is intended (e.g. actually removing a column), that is a
   separate operation.
