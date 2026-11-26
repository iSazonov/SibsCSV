# PowerShell CSV cmdlets

The repository contains a PowerShell module with _optimized_ version of CSV cmdlets (`Import-Csv`/`ConvertFrom-Csv`, `Export-Csv`/`ConvertTo-Csv`) from `Microsoft.PowerShell.Utility` standard module.

| Cmdlet | Time Win | Mem Win |
| --- | :---: | :---: |
| `Import-Csv` | ~20% | ~9% |
| `ConvertFrom-Csv` | ~16% | ~32% |

## Build module

```cmd
dotnet build
```

or

```cmd
dotnet build -c Release
```

## Usage

Install `SibsCSV` module from `distrib\Module` folder. Then import:

```pwsh
Import-Module SibsCSV
```

## Run Pester tests

```cmd
runtests.cmd
```

## Run performance tests

```pwsh
cd .\perf
./Measure-CmdletPerf.ps1
```
