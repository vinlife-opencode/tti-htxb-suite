using System.Runtime.InteropServices;

namespace Htxb.RecipeCore.Bidf;

/// <summary>
/// Read-only access to a BiDF database (SQLite) through the SQLite that ships with
/// Windows, <c>winsqlite3.dll</c> (3.43 on Windows 10 22H2).
///
/// Deliberately not a NuGet package: RecipeCore is package-free so the same source
/// compiles inside the offline installer. The file is opened read-only and every
/// statement is finalised, so a BiDF is never modified by a check.
///
/// Strings cross the boundary as <see cref="IntPtr"/> to memory this class owns.
/// Declaring a <c>char*</c> parameter as <c>byte[]</c> - the obvious thing - makes
/// the marshaller treat the buffer as writable and hand it back, which corrupts the
/// process heap on the very first call (measured: 0xC0000374 on
/// <c>sqlite3_open_v2</c>).
/// </summary>
public sealed class BidfReader : IDisposable
{
    private const int SqliteOk = 0;
    private const int SqliteRow = 100;
    private const int SqliteDone = 101;
    private const int SqliteNull = 5;
    private const int SqliteOpenReadOnly = 1;

    private IntPtr _db;

    public BidfReader(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("BiDF not found", path);

        int rc = Native.sqlite3_open_v2(Utf8(path), out _db, SqliteOpenReadOnly, IntPtr.Zero);
        if (rc != SqliteOk || _db == IntPtr.Zero)
        {
            string message = _db == IntPtr.Zero ? "cannot open" : Error();
            if (_db != IntPtr.Zero) Native.sqlite3_close(_db);
            _db = IntPtr.Zero;
            throw new InvalidDataException($"cannot open {Path.GetFileName(path)} as SQLite: {message}");
        }
    }

    /// <summary>Every row of a query, each value as text ("" for NULL).</summary>
    public List<string[]> Query(string sql)
    {
        if (_db == IntPtr.Zero) throw new ObjectDisposedException(nameof(BidfReader));

        IntPtr text = Utf8(sql);
        IntPtr statement;
        int rc;
        try
        {
            rc = Native.sqlite3_prepare_v2(_db, text, -1, out statement, IntPtr.Zero);
        }
        finally
        {
            Marshal.FreeCoTaskMem(text);
        }

        if (rc != SqliteOk) throw new InvalidDataException(Error());

        var rows = new List<string[]>();
        try
        {
            int columns = Native.sqlite3_column_count(statement);
            while ((rc = Native.sqlite3_step(statement)) == SqliteRow)
            {
                var row = new string[columns];
                for (int i = 0; i < columns; i++)
                {
                    row[i] = Native.sqlite3_column_type(statement, i) == SqliteNull
                        ? string.Empty
                        : Marshal.PtrToStringUTF8(Native.sqlite3_column_text(statement, i)) ?? string.Empty;
                }
                rows.Add(row);
            }
            if (rc != SqliteDone) throw new InvalidDataException(Error());
        }
        finally
        {
            Native.sqlite3_finalize(statement);
        }

        return rows;
    }

    public string? Scalar(string sql)
    {
        List<string[]> rows = Query(sql);
        return rows.Count == 0 || rows[0].Length == 0 ? null : rows[0][0];
    }

    public long Count(string table)
    {
        string? text = Scalar("select count(*) from " + table);
        return long.TryParse(text, out long value) ? value : 0;
    }

    private string Error() =>
        _db == IntPtr.Zero ? "no database" : Marshal.PtrToStringUTF8(Native.sqlite3_errmsg(_db)) ?? "unknown error";

    /// <summary>UTF-8, null-terminated, on memory the caller must free with <see cref="Marshal.FreeCoTaskMem"/>.</summary>
    private static IntPtr Utf8(string text) => Marshal.StringToCoTaskMemUTF8(text);

    public void Dispose()
    {
        if (_db != IntPtr.Zero)
        {
            Native.sqlite3_close(_db);
            _db = IntPtr.Zero;
        }
    }

    private static class Native
    {
        private const string Dll = "winsqlite3";

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int sqlite3_open_v2(IntPtr filename, out IntPtr db, int flags, IntPtr vfs);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int sqlite3_close(IntPtr db);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int sqlite3_prepare_v2(IntPtr db, IntPtr sql, int bytes, out IntPtr stmt, IntPtr tail);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int sqlite3_step(IntPtr stmt);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int sqlite3_finalize(IntPtr stmt);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int sqlite3_column_count(IntPtr stmt);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int sqlite3_column_type(IntPtr stmt, int index);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr sqlite3_column_text(IntPtr stmt, int index);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr sqlite3_errmsg(IntPtr db);
    }
}
