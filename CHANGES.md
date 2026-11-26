# Changes in SibsCSV module

## Version 1.0.0

1. Enable nullability and eliminate all complaints.
1. Use `StringReader` in `ConvertFrom-Csv` instead of `MemoryStream<byte>` and `StreamReader`.
1. Use `HashSet(IEnumerable<T> collection, IEqualityComparer<T>? comparer)` overload for initialization in `ReconcilePreexistingPropertyNames()`.
1. Use `List<string>` in `BuildPropertyNames()` for `propertyNames`. Initialize it with 16 size.
1. Replace `IList<string>` with `List<string>` in all fields of all cmdlets since only `List<string>` is exactly used.
1. Define `ImportCsvHelper` as `ref struct`.
1. Add `_header` cache in `ConvertFrom-Csv`.
1. Remove `private FileStream? _fs;` from `Export-Csv` since `StreamWriter` disposes it by default.
1. Remove `IDisposable`, `Dispose()` and `_disposed` from `ExportCsvHelper` since there is nothing to dispose.
1. Remove `EndProcessing()` from `ExportCsvHelper` since PowerShell engine calls `Dispose()` itself.
1. Remove `_sw = null` from `Export-Csv` `Dispose()` since it is a bug since we can get `NRE` instead of `ObjectDisposedException` after pipeline interruption.
1. Move write operations to `ExportCsvHelper` with generic `TextWriter _writer`. Now `Export-Csv` uses `StreamWriter` and `ConvertTo-Csv` uses `StringWriter`.
Remove `WriteCsvLine()` at all.
1. Remove `#pragma warning disable 1634, 1691`.
1. Unify using modern syntax.
1. Move common code of `ConvertFrom-Csv` and `Import-Csv` to `BaseCsvReadingCommand`.
1. Define `ImportExportCSVHelper` as struct. Move `StringBuilder` caches on top level.
1. Move `IDictionary` check out of loop in `ConvertPSObjectToCSV`.
