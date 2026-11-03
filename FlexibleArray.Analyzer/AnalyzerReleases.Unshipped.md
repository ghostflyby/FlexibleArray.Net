; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
FA0001 | FlexibleArray | Error | GetFirstElement must return a reference to an instance field of the header type
FA0002 | FlexibleArray | Error | The first-element placeholder must be the last instance field
FA0003 | FlexibleArray | Error | LayoutKind.Auto produces no C-compatible layout
FA0004 | FlexibleArray | Error | Runtime-based DllImport ignores the flexible array marshallers
FA0005 | FlexibleArray | Error | A walk-derived tail length cannot implement the write-back contract
