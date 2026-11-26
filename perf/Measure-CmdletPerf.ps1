$testFile = "$env:TEMP\benchmark_data.csv"

if (-not (Test-Path $testFile)) {

    $sw = [System.Diagnostics.Stopwatch]::StartNew()

    $writer = [System.IO.StreamWriter]::new($testFile, $false, [System.Text.Encoding]::UTF8)
    $writer.WriteLine("Id,Name,Email,Description,Amount,CreatedAt")

    for ($i = 1; $i -le 100000; $i++) {
        $percent = [Math]::Truncate($i / 1000)
        Write-Progress -Activity "Create Test Data" -Status "Creating test data..." -PercentComplete $percent
        $writer.WriteLine("$i,`"User, Name $i`",user$i@example.com,`"Some text with `"`"quotes`"`" and commas, line $i`",$($i * 1.5),2026-11-25T10:00:00Z")
    }
    $writer.Close()
    Write-Host "File is created in $($sw.Elapsed.TotalSeconds) sec. Size: $([math]::Round((Get-Item $testFile).Length / 1MB, 2)) MB"
    Write-Progress -Activity "Create Test Data" -PercentComplete
}

$iterations = 5

function Measure-CmdletAllocation {
    param(
        [string]$Name,
        [scriptblock]$CommandBlock,
        [int]$Iterations = 5
    )

    # 1. Прогрев JIT (не попадает в статистику)
    & $CommandBlock | Out-Null

    $metrics = for ($i = 1; $i -le $Iterations; $i++) {
        [GC]::Collect()
        [GC]::WaitForPendingFinalizers()
        [GC]::Collect()

        $gen0Before = [GC]::CollectionCount(0)
        $gen1Before = [GC]::CollectionCount(1)
        $gen2Before = [GC]::CollectionCount(2)
        $before     = [GC]::GetTotalAllocatedBytes($true)

        $timer = [Diagnostics.Stopwatch]::StartNew()
        & $CommandBlock | Out-Null
        $timer.Stop()

        $allocated = [GC]::GetTotalAllocatedBytes($true) - $before
        $gen0 = [GC]::CollectionCount(0) - $gen0Before
        $gen1 = [GC]::CollectionCount(1) - $gen1Before
        $gen2 = [GC]::CollectionCount(2) - $gen2Before

        [PSCustomObject]@{
            TimeMs      = $timer.Elapsed.TotalMilliseconds
            AllocatedMB = $allocated / 1MB
            Gen0        = $gen0
            Gen1        = $gen1
            Gen2        = $gen2
        }
    }

    [PSCustomObject]@{
        Test           = $Name
        AvgTime_ms     = [math]::Round(($metrics.TimeMs | Measure-Object -Average).Average, 2)
        Allocated_MB   = [math]::Round(($metrics.AllocatedMB | Measure-Object -Average).Average, 2)
        Gen0_Collects  = [math]::Round(($metrics.Gen0 | Measure-Object -Average).Average, 1)
        Gen1_Collects  = [math]::Round(($metrics.Gen1 | Measure-Object -Average).Average, 1)
        Gen2_Collects  = [math]::Round(($metrics.Gen2 | Measure-Object -Average).Average, 1)
    }
}

$report = @(
    $SibsCSV_Path = "$PSScriptRoot\..\distrib\Module\SibsCSV"

    Write-Progress -Activity "Test Data" -Status "Built-in Import-Csv..."
    Measure-CmdletAllocation -Name "Built-in Import-Csv" -CommandBlock {
        $null = Microsoft.PowerShell.Utility\Import-Csv -Path $testFile
    }
    Write-Progress -Activity "Test Data" -Status "SibsCSV Import-Csv..."
    Measure-CmdletAllocation -Name "SibsCSV Import-Csv" -CommandBlock {
        Import-Module $SibsCSV_Path
        $null = SibsCSV\Import-Csv -Path $testFile
    }

    Write-Progress -Activity "Test Data" -Status "Built-in ConvertFrom-Csv..."
    Measure-CmdletAllocation -Name "Built-in ConvertFrom-Csv" -CommandBlock {
        $null = Get-Content $testFile | Microsoft.PowerShell.Utility\ConvertFrom-Csv
    }
    Write-Progress -Activity "Test Data" -Status "SibsCSV ConvertFrom-Csv..."
    Measure-CmdletAllocation -Name "SibsCSV ConvertFrom-Csv" -CommandBlock {
        Import-Module $SibsCSV_Path
        $null = Get-Content $testFile | SibsCSV\ConvertFrom-Csv
    }
)

$report | Format-Table -AutoSize

$timestamp = Get-Date -Format "yyyy-MM-dd_HH-mm-ss"
$filePath = Join-Path -Path $PSScriptRoot -ChildPath 'Results' -AdditionalChildPath "report_$timestamp.txt"
$prevStyle = $PSStyle.OutputRendering
$PSStyle.OutputRendering = 'PlainText'
$report | Format-Table -AutoSize | Out-File -FilePath $filePath -Encoding utf8 -Width 4096
$PSStyle.OutputRendering = $prevStyle