param(
    [string]$PreviousBinaryPath = 'C:\OTMR\Class153OtmrMonitor\tools\RS485Analyser\bin\Release\net8.0-windows\win-x64\logs\ScpSniffer_COM2_20260817_112520.bin',
    [switch]$WriteCsv
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$currentRawPath = Join-Path $repoRoot 'TestData\OTMR_RAW_CAPTURE_20260824.txt'
$hhdPath = Join-Path $repoRoot 'TestData\HHD_Serial_Trace_20260824_143201.txt'
$offsetCsvPath = Join-Path $repoRoot 'docs\CLASS171_OTMR_BLOCKED_WRITE_OFFSETS_20260824.csv'
$previousHexPath = Join-Path (Split-Path -Parent $PreviousBinaryPath) (([IO.Path]::GetFileNameWithoutExtension($PreviousBinaryPath)) + '.hex.log')

function Convert-CompactHex([string]$hex) {
    if (($hex.Length -band 1) -ne 0) { throw "Odd-length hex value: $hex" }
    $bytes = New-Object byte[] ($hex.Length / 2)
    for ($index = 0; $index -lt $bytes.Length; $index++) {
        $bytes[$index] = [Convert]::ToByte($hex.Substring($index * 2, 2), 16)
    }
    return $bytes
}

function Convert-SpacedHex([string]$hex) {
    return [byte[]]@($hex.Split(' ', [StringSplitOptions]::RemoveEmptyEntries) |
        ForEach-Object { [Convert]::ToByte($_, 16) })
}

function Format-Bytes([byte[]]$bytes) {
    if ($bytes.Length -eq 0) { return '-' }
    return (($bytes | ForEach-Object { '{0:X2}' -f $_ }) -join ' ')
}

function Test-EqualBytes([byte[]]$left, [byte[]]$right) {
    if ($left.Length -ne $right.Length) { return $false }
    for ($index = 0; $index -lt $left.Length; $index++) {
        if ($left[$index] -ne $right[$index]) { return $false }
    }
    return $true
}

function Get-Payload([byte[]]$frame) {
    if ($frame.Length -le 12) { return [byte[]]@() }
    return [byte[]]$frame[9..($frame.Length - 4)]
}

function Get-PayloadSum([byte[]]$frame) {
    [int]$sum = 0
    foreach ($value in (Get-Payload $frame)) { $sum = ($sum + $value) -band 0xFF }
    return [byte]$sum
}

function Read-ProtocolFrames([byte[]]$bytes) {
    $frames = [System.Collections.Generic.List[byte[]]]::new()
    $offset = 0
    while ($offset -lt $bytes.Length) {
        while ($offset -lt $bytes.Length -and $bytes[$offset] -ne 0x01) { $offset++ }
        if ($offset + 9 -gt $bytes.Length) { break }
        $length = if ($bytes[$offset + 5] -eq 1 -and $bytes[$offset + 6] -eq 1 -and $bytes[$offset + 8] -eq 2) {
            12 + $bytes[$offset + 7]
        } else { 13 }
        if ($offset + $length -gt $bytes.Length) { break }
        [byte[]]$frame = $bytes[$offset..($offset + $length - 1)]
        if ($frame[0] -eq 0x01 -and $frame[8] -eq 0x02 -and
            $frame[$frame.Length - 3] -eq 0x03 -and $frame[$frame.Length - 1] -eq 0x04) {
            $frames.Add($frame)
            $offset += $length
            if ($frame[1] -eq 0x13) { break }
        } else {
            $offset++
        }
    }
    return $frames
}

function Read-PreviousCapture {
    if (-not (Test-Path -LiteralPath $PreviousBinaryPath)) { throw "Previous capture not found: $PreviousBinaryPath" }
    if (-not (Test-Path -LiteralPath $previousHexPath)) { throw "Direction/timestamp sidecar not found: $previousHexPath" }

    $records = [System.Collections.Generic.List[object]]::new()
    $rxBytes = [System.Collections.Generic.List[byte]]::new()
    foreach ($line in Get-Content -LiteralPath $previousHexPath) {
        if ($line -notmatch '^(?<time>\S+) (?<direction>RX|TX) (?<hex>[0-9A-F]+)$') {
            throw "Unrecognised previous sidecar line: $line"
        }
        [byte[]]$bytes = Convert-CompactHex $matches['hex']
        $records.Add([pscustomobject]@{
            Timestamp = $matches['time']
            Direction = $matches['direction']
            Bytes = $bytes
        })
        if ($matches['direction'] -eq 'RX') {
            foreach ($value in $bytes) { $rxBytes.Add($value) }
        }
    }

    [byte[]]$binary = [IO.File]::ReadAllBytes($PreviousBinaryPath)
    [byte[]]$sidecarRx = $rxBytes.ToArray()
    if (-not (Test-EqualBytes $binary $sidecarRx)) {
        throw 'Previous binary is not the byte-for-byte concatenation of sidecar RX records.'
    }

    return [pscustomobject]@{
        Binary = $binary
        Records = $records
        Frames = @(Read-ProtocolFrames $binary)
    }
}

function Read-CurrentFrames {
    $bytes = [System.Collections.Generic.List[byte]]::new()
    foreach ($line in Get-Content -LiteralPath $currentRawPath) {
        $parts = $line -split '  +', 4
        if ($parts.Count -ge 3 -and $parts[1] -eq 'RX' -and
            $parts[0].StartsWith('2026-08-24T14:32:', [StringComparison]::Ordinal)) {
            foreach ($value in (Convert-SpacedHex $parts[2])) { $bytes.Add($value) }
        }
    }
    return @(Read-ProtocolFrames $bytes.ToArray())
}

function Read-HhdWrites {
    $writes = @{}
    $current = $null
    foreach ($line in Get-Content -LiteralPath $hhdPath) {
        if ($line -match '^(?<event>\d+): Write Request \(DOWN\),') {
            if ($null -ne $current) { $writes[$current.Event] = $current.Bytes.ToArray() }
            $current = [pscustomobject]@{
                Event = $matches['event']
                Bytes = [System.Collections.Generic.List[byte]]::new()
            }
            continue
        }
        if ($null -ne $current -and $line -match '^\s(?<hex>[0-9A-F]{2}(?: [0-9A-F]{2}){0,15})\s{2,}') {
            foreach ($value in $matches['hex'].Split(' ')) { $current.Bytes.Add([Convert]::ToByte($value, 16)) }
        } elseif ($null -ne $current -and $line.Length -eq 0) {
            $writes[$current.Event] = $current.Bytes.ToArray()
            $current = $null
        }
    }
    if ($null -ne $current) { $writes[$current.Event] = $current.Bytes.ToArray() }
    return $writes
}

function Get-DifferenceRanges([byte[]]$previous, [byte[]]$current) {
    $ranges = [System.Collections.Generic.List[string]]::new()
    $limit = [Math]::Min($previous.Length, $current.Length)
    $start = -1
    for ($offset = 0; $offset -lt $limit; $offset++) {
        if ($previous[$offset] -ne $current[$offset] -and $start -lt 0) { $start = $offset }
        if ($previous[$offset] -eq $current[$offset] -and $start -ge 0) {
            $ranges.Add(('0x{0:X2}..0x{1:X2}' -f $start, ($offset - 1)))
            $start = -1
        }
    }
    if ($start -ge 0) { $ranges.Add(('0x{0:X2}..0x{1:X2}' -f $start, ($limit - 1))) }
    if ($previous.Length -ne $current.Length) {
        $ranges.Add(('length {0:X}->{1:X}' -f $previous.Length, $current.Length))
    }
    return $ranges
}

function Get-Slice([byte[]]$bytes, [int]$start, [int]$count) {
    if ($count -eq 0) { return [byte[]]@() }
    return [byte[]]$bytes[$start..($start + $count - 1)]
}

function Get-LcsMap([byte[]]$source, [byte[]]$target) {
    $sourceLength = $source.Length
    $targetLength = $target.Length
    $width = $targetLength + 1
    $scores = New-Object 'int[]' (($sourceLength + 1) * ($targetLength + 1))
    for ($sourceOffset = $sourceLength - 1; $sourceOffset -ge 0; $sourceOffset--) {
        for ($targetOffset = $targetLength - 1; $targetOffset -ge 0; $targetOffset--) {
            $index = ($sourceOffset * $width) + $targetOffset
            if ($source[$sourceOffset] -eq $target[$targetOffset]) {
                $scores[$index] = 1 + $scores[(($sourceOffset + 1) * $width) + $targetOffset + 1]
            } else {
                $scores[$index] = [Math]::Max(
                    $scores[(($sourceOffset + 1) * $width) + $targetOffset],
                    $scores[($sourceOffset * $width) + $targetOffset + 1])
            }
        }
    }
    $map = @{}
    $sourceOffset = 0
    $targetOffset = 0
    while ($sourceOffset -lt $sourceLength -and $targetOffset -lt $targetLength) {
        if ($source[$sourceOffset] -eq $target[$targetOffset]) {
            $map[$targetOffset] = $sourceOffset
            $sourceOffset++
            $targetOffset++
        } elseif ($scores[(($sourceOffset + 1) * $width) + $targetOffset] -ge
                  $scores[($sourceOffset * $width) + $targetOffset + 1]) {
            $sourceOffset++
        } else {
            $targetOffset++
        }
    }
    return $map
}

function Get-GlobalEditGaps([byte[]]$source, [byte[]]$target, [hashtable]$map) {
    $gaps = [System.Collections.Generic.List[object]]::new()
    $sourceCursor = 0
    $targetCursor = 0
    foreach ($targetMatchValue in @($map.Keys | Sort-Object { [int]$_ })) {
        $targetMatch = [int]$targetMatchValue
        $sourceMatch = [int]$map[$targetMatch]
        if ($sourceMatch -gt $sourceCursor -or $targetMatch -gt $targetCursor) {
            $gaps.Add([pscustomobject]@{
                SourceStart = $sourceCursor
                SourceCount = $sourceMatch - $sourceCursor
                TargetStart = $targetCursor
                TargetCount = $targetMatch - $targetCursor
            })
        }
        $sourceCursor = $sourceMatch + 1
        $targetCursor = $targetMatch + 1
    }
    if ($sourceCursor -lt $source.Length -or $targetCursor -lt $target.Length) {
        $gaps.Add([pscustomobject]@{
            SourceStart = $sourceCursor
            SourceCount = $source.Length - $sourceCursor
            TargetStart = $targetCursor
            TargetCount = $target.Length - $targetCursor
        })
    }
    return $gaps
}

function Get-StreamLocation([int]$offset, [int[]]$lengths) {
    $cursor = 0
    for ($page = 0; $page -lt $lengths.Length; $page++) {
        if ($offset -lt $cursor + $lengths[$page]) {
            return 'page{0}+0x{1:X2}' -f ($page + 1), ($offset - $cursor)
        }
        $cursor += $lengths[$page]
    }
    return 'end+0x{0:X}' -f ($offset - $cursor)
}

$previous = Read-PreviousCapture
$currentFrames = @(Read-CurrentFrames)
$writes = Read-HhdWrites
$previousByTransaction = @{}
$currentByTransaction = @{}
foreach ($frame in $previous.Frames) { $previousByTransaction[[int]$frame[1]] = $frame }
foreach ($frame in $currentFrames) { $currentByTransaction[[int]$frame[1]] = $frame }

$rxRecords = @($previous.Records | Where-Object Direction -eq 'RX')
$txRecords = @($previous.Records | Where-Object Direction -eq 'TX')
$liveStarts = @($rxRecords | Where-Object {
    $_.Bytes.Length -ge 2 -and $_.Bytes[0] -eq 0xFB -and $_.Bytes[1] -eq 0xFB
})

Write-Output 'Previous-capture integrity and scope:'
'binary={0} bytes; sidecar RX records={1}; sidecar TX records={2}; protocol frames={3}; FB-FB-starting RX chunks={4}' -f
    $previous.Binary.Length, $rxRecords.Count, $txRecords.Count, $previous.Frames.Count, $liveStarts.Count
'transactions={0}' -f (($previous.Frames | ForEach-Object { '{0:X2}' -f $_[1] }) -join ',')
foreach ($frame in $previous.Frames) {
    if ((Get-PayloadSum $frame) -ne $frame[$frame.Length - 2]) {
        throw ('Previous RX 01 {0:X2} has an invalid payload sum.' -f $frame[1])
    }
}

$pageMappings = @(
    [pscustomobject]@{ Event='000312'; Transaction=0x02 },
    [pscustomobject]@{ Event='000335'; Transaction=0x04 },
    [pscustomobject]@{ Event='000357'; Transaction=0x06 },
    [pscustomobject]@{ Event='000378'; Transaction=0x08 },
    [pscustomobject]@{ Event='000401'; Transaction=0x0A },
    [pscustomobject]@{ Event='000423'; Transaction=0x0C }
)

Write-Output 'Previous versus 2026-08-24 source RX payloads:'
$rxDifferenceRows = [System.Collections.Generic.List[object]]::new()
foreach ($mapping in $pageMappings) {
    [byte[]]$oldPayload = Get-Payload $previousByTransaction[$mapping.Transaction]
    [byte[]]$newPayload = Get-Payload $currentByTransaction[$mapping.Transaction]
    $different = 0
    for ($offset = 0; $offset -lt [Math]::Min($oldPayload.Length, $newPayload.Length); $offset++) {
        if ($oldPayload[$offset] -ne $newPayload[$offset]) {
            $different++
            $rxDifferenceRows.Add([pscustomobject]@{
                Event = $mapping.Event
                SourceRx = '01 {0:X2}' -f $mapping.Transaction
                PayloadOffset = '0x{0:X2}' -f $offset
                FrameOffset = '0x{0:X3}' -f ($offset + 9)
                PreviousValue = '{0:X2}' -f $oldPayload[$offset]
                CurrentValue = '{0:X2}' -f $newPayload[$offset]
            })
        }
    }
    $ranges = @(Get-DifferenceRanges $oldPayload $newPayload)
    '{0} source=01 {1:X2}; payload={2:X}; differing={3}; ranges={4}' -f
        $mapping.Event, $mapping.Transaction, $newPayload.Length, $different,
        $(if ($ranges.Count -eq 0) { '-' } else { $ranges -join ',' })
}

$currentRxPayloads = @($pageMappings | ForEach-Object { [byte[]](Get-Payload $currentByTransaction[$_.Transaction]) })
$currentTxPayloads = @($pageMappings | ForEach-Object { [byte[]](Get-Payload $writes[$_.Event]) })
$rxStreamList = [System.Collections.Generic.List[byte]]::new()
$txStreamList = [System.Collections.Generic.List[byte]]::new()
foreach ($payload in $currentRxPayloads) { foreach ($value in $payload) { $rxStreamList.Add($value) } }
foreach ($payload in $currentTxPayloads) { foreach ($value in $payload) { $txStreamList.Add($value) } }
[byte[]]$rxStream = $rxStreamList.ToArray()
[byte[]]$txStream = $txStreamList.ToArray()
$globalMap = Get-LcsMap $rxStream $txStream
$globalGaps = @(Get-GlobalEditGaps $rxStream $txStream $globalMap)
[int[]]$rxLengths = @(0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xCA)
[int[]]$txLengths = @(0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xC6)
$previousRxStreamList = [System.Collections.Generic.List[byte]]::new()
foreach ($mapping in $pageMappings) {
    foreach ($value in (Get-Payload $previousByTransaction[$mapping.Transaction])) {
        $previousRxStreamList.Add($value)
    }
}
[byte[]]$previousRxStream = $previousRxStreamList.ToArray()

Write-Output 'Global current-recorder RX-page stream to Analyser TX-page stream LCS:'
'rx={0}; tx={1}; copied={2}; unexplained-target={3}; gaps={4}' -f
    $rxStream.Length, $txStream.Length, $globalMap.Count, ($txStream.Length - $globalMap.Count), $globalGaps.Count
foreach ($gap in $globalGaps) {
    [byte[]]$sourceBytes = Get-Slice $rxStream $gap.SourceStart $gap.SourceCount
    [byte[]]$previousSourceBytes = Get-Slice $previousRxStream $gap.SourceStart $gap.SourceCount
    [byte[]]$targetBytes = Get-Slice $txStream $gap.TargetStart $gap.TargetCount
    'RX {0}+{1} [old:{2}; current:{3}] -> TX {4}+{5} [{6}]' -f
        (Get-StreamLocation $gap.SourceStart $rxLengths), $gap.SourceCount,
        (Format-Bytes $previousSourceBytes), (Format-Bytes $sourceBytes),
        (Get-StreamLocation $gap.TargetStart $txLengths), $gap.TargetCount, (Format-Bytes $targetBytes)
}

$eventPageIndex = @{ '000312'=0; '000335'=1; '000357'=2; '000378'=3; '000401'=4; '000423'=5 }
$globalRows = [System.Collections.Generic.List[object]]::new()
$replacementSources = @{
    '000312:0x009' = [pscustomobject]@{ Start=0x000; Count=5 }
    '000312:0x08B' = [pscustomobject]@{ Start=0x086; Count=1 }
    '000312:0x0BD' = [pscustomobject]@{ Start=0x0B8; Count=1 }
    '000423:0x0CD' = [pscustomobject]@{ Start=(0x4FB + 0xC8); Count=1 }
}
$candidateRows = @(Import-Csv -LiteralPath $offsetCsvPath | Where-Object {
    $eventPageIndex.ContainsKey($_.Event) -and
    ($_.Classification -in 'unknown', 'modified from current OTMR' -or
     ($_.Event -eq '000378' -and $_.OffsetHex -in '0x00F', '0x010'))
})

Write-Output 'Prior unknown/modified target bytes under continuous-stream alignment:'
foreach ($offsetRow in $candidateRows) {
    $frameOffset = [Convert]::ToInt32($offsetRow.OffsetHex.Substring(2), 16)
    $payloadOffset = $frameOffset - 9
    $targetGlobalOffset = ($eventPageIndex[$offsetRow.Event] * 0xFF) + $payloadOffset
    $hasGlobalSource = $globalMap.ContainsKey($targetGlobalOffset)
    $sourceGlobalOffset = if ($hasGlobalSource) { [int]$globalMap[$targetGlobalOffset] } else { -1 }
    $replacementKey = $offsetRow.Event + ':' + $offsetRow.OffsetHex
    $replacement = if ($replacementSources.ContainsKey($replacementKey)) { $replacementSources[$replacementKey] } else { $null }
    [byte[]]$currentSourceBytes = if ($hasGlobalSource) { [byte[]]@($rxStream[$sourceGlobalOffset]) }
        elseif ($null -ne $replacement) { Get-Slice $rxStream $replacement.Start $replacement.Count }
        else { [byte[]]@() }
    [byte[]]$previousSourceBytes = if ($hasGlobalSource) { [byte[]]@($previousRxStream[$sourceGlobalOffset]) }
        elseif ($null -ne $replacement) { Get-Slice $previousRxStream $replacement.Start $replacement.Count }
        else { [byte[]]@() }
    $currentSourceValue = Format-Bytes $currentSourceBytes
    $previousSourceValue = Format-Bytes $previousSourceBytes
    $betweenSessions = if ($currentSourceBytes.Length -eq 0) { 'no aligned source' }
        elseif (Test-EqualBytes $currentSourceBytes $previousSourceBytes) { 'constant source' }
        else { 'different source' }
    $finding = if ($hasGlobalSource) { 'copied from current OTMR under continuous-page alignment' }
        elseif ($null -ne $replacement) { 'modified/replacement; generation rule unknown' }
        else { 'unknown under continuous-page alignment' }
    $globalRow = [pscustomobject]@{
        Event = $offsetRow.Event
        TxFrameOffset = $offsetRow.OffsetHex
        TxValue = $offsetRow.TxValue
        EarlierClassification = $offsetRow.Classification
        ContinuousSourceLocation = if ($hasGlobalSource) { Get-StreamLocation $sourceGlobalOffset $rxLengths }
            elseif ($null -ne $replacement -and $replacement.Count -eq 1) { Get-StreamLocation $replacement.Start $rxLengths }
            elseif ($null -ne $replacement) {
                '{0}..{1}' -f (Get-StreamLocation $replacement.Start $rxLengths),
                    (Get-StreamLocation ($replacement.Start + $replacement.Count - 1) $rxLengths)
            } else { '-' }
        PreviousRxSourceValue = $previousSourceValue
        CurrentRxSourceValue = $currentSourceValue
        BetweenSessions = $betweenSessions
        Finding = $finding
    }
    $globalRows.Add($globalRow)
    '{0} {1} TX={2}; source={3} old={4} current={5}; {6}' -f
        $globalRow.Event, $globalRow.TxFrameOffset, $globalRow.TxValue,
        $globalRow.ContinuousSourceLocation, $globalRow.PreviousRxSourceValue,
        $globalRow.CurrentRxSourceValue, $globalRow.Finding
}

$fields = @(
    [pscustomobject]@{ Event='000312'; TxPayloadStart=0x00; TxCount=1; SourceStart=0x00; SourceCount=5; Note='leading 5-to-1 replacement' },
    [pscustomobject]@{ Event='000312'; TxPayloadStart=0x82; TxCount=1; SourceStart=0x86; SourceCount=1; Note='aligned modification' },
    [pscustomobject]@{ Event='000312'; TxPayloadStart=0xB4; TxCount=1; SourceStart=0xB8; SourceCount=1; Note='aligned modification' },
    [pscustomobject]@{ Event='000312'; TxPayloadStart=0xFB; TxCount=4; SourceStart=-1; SourceCount=0; Note='appended bytes' },
    [pscustomobject]@{ Event='000335'; TxPayloadStart=0xFB; TxCount=4; SourceStart=-1; SourceCount=0; Note='appended bytes after RX deletion at 0x43..0x46' },
    [pscustomobject]@{ Event='000357'; TxPayloadStart=0x7D; TxCount=2; SourceStart=-1; SourceCount=0; Note='inserted bytes' },
    [pscustomobject]@{ Event='000357'; TxPayloadStart=0xCD; TxCount=2; SourceStart=-1; SourceCount=0; Note='inserted bytes' },
    [pscustomobject]@{ Event='000357'; TxPayloadStart=0xF9; TxCount=6; SourceStart=-1; SourceCount=0; Note='appended bytes' },
    [pscustomobject]@{ Event='000378'; TxPayloadStart=0x5E; TxCount=2; SourceStart=-1; SourceCount=0; Note='inserted bytes' },
    [pscustomobject]@{ Event='000378'; TxPayloadStart=0x8E; TxCount=2; SourceStart=-1; SourceCount=0; Note='inserted bytes' },
    [pscustomobject]@{ Event='000378'; TxPayloadStart=0xFB; TxCount=4; SourceStart=-1; SourceCount=0; Note='appended bytes' },
    [pscustomobject]@{ Event='000423'; TxPayloadStart=0xC4; TxCount=1; SourceStart=0xC8; SourceCount=1; Note='one-byte modification after the global four-byte shift' }
)
$sourceTransactionByEvent = @{ '000312'=0x02; '000335'=0x04; '000357'=0x06; '000378'=0x08; '000423'=0x0C }
$comparisonRows = [System.Collections.Generic.List[object]]::new()

Write-Output 'Previously unexplained/modified TX fields against both source RX sessions:'
foreach ($field in $fields) {
    $transaction = $sourceTransactionByEvent[$field.Event]
    [byte[]]$oldPayload = Get-Payload $previousByTransaction[$transaction]
    [byte[]]$newPayload = Get-Payload $currentByTransaction[$transaction]
    [byte[]]$txPayload = Get-Payload $writes[$field.Event]
    [byte[]]$txBytes = Get-Slice $txPayload $field.TxPayloadStart $field.TxCount
    [byte[]]$oldSame = Get-Slice $oldPayload $field.TxPayloadStart $field.TxCount
    [byte[]]$newSame = Get-Slice $newPayload $field.TxPayloadStart $field.TxCount
    [byte[]]$oldSource = if ($field.SourceStart -ge 0) { Get-Slice $oldPayload $field.SourceStart $field.SourceCount } else { [byte[]]@() }
    [byte[]]$newSource = if ($field.SourceStart -ge 0) { Get-Slice $newPayload $field.SourceStart $field.SourceCount } else { [byte[]]@() }
    $sourceComparison = if ($field.SourceStart -lt 0) { 'no aligned source' }
        elseif (Test-EqualBytes $oldSource $newSource) { 'constant RX source' }
        else { 'different RX source' }
    $row = [pscustomobject]@{
        Event = $field.Event
        TxFrameRange = if ($field.TxCount -eq 1) { '0x{0:X3}' -f ($field.TxPayloadStart + 9) }
            else { '0x{0:X3}..0x{1:X3}' -f ($field.TxPayloadStart + 9), ($field.TxPayloadStart + 8 + $field.TxCount) }
        CurrentTx = Format-Bytes $txBytes
        SourceRx = '01 {0:X2}' -f $transaction
        AlignedSourcePayloadRange = if ($field.SourceStart -lt 0) { '-' }
            elseif ($field.SourceCount -eq 1) { '0x{0:X2}' -f $field.SourceStart }
            else { '0x{0:X2}..0x{1:X2}' -f $field.SourceStart, ($field.SourceStart + $field.SourceCount - 1) }
        PreviousSource = Format-Bytes $oldSource
        CurrentSource = Format-Bytes $newSource
        SourceComparison = $sourceComparison
        PreviousRxSameTxOffset = Format-Bytes $oldSame
        CurrentRxSameTxOffset = Format-Bytes $newSame
        Note = $field.Note
    }
    $comparisonRows.Add($row)
    '{0} TX {1} [{2}]; source {3} payload {4}: old=[{5}] current=[{6}] ({7}); {8}' -f
        $row.Event, $row.TxFrameRange, $row.CurrentTx, $row.SourceRx,
        $row.AlignedSourcePayloadRange, $row.PreviousSource, $row.CurrentSource,
        $row.SourceComparison, $row.Note
}

if ($WriteCsv) {
    $output = Join-Path $repoRoot 'docs\CLASS171_OTMR_WORKING_CAPTURE_FIELD_COMPARISON_20260817_20260824.csv'
    $globalRows | Export-Csv -LiteralPath $output -NoTypeInformation -Encoding UTF8
    Write-Output "Wrote $output"
    $rxOutput = Join-Path $repoRoot 'docs\CLASS171_OTMR_WORKING_CAPTURE_RX_DIFFS_20260817_20260824.csv'
    $rxDifferenceRows | Export-Csv -LiteralPath $rxOutput -NoTypeInformation -Encoding UTF8
    Write-Output "Wrote $rxOutput"
}

Write-Output 'Prior sidecar direction evidence:'
if ($txRecords.Count -eq 0) {
    Write-Output 'No TX records exist. The .bin is RX-only by RawLogWriter design, so prior Analyser write values cannot be observed or reconstructed from this capture alone.'
} else {
    Write-Output ("Observed TX records: {0}" -f $txRecords.Count)
}
