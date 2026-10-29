param(
    [switch]$WriteCsv
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$oldPath = Join-Path $repoRoot 'TestData\HHD_Serial_Trace_20260824_143201.txt'
$newPath = Join-Path $repoRoot 'TestData\HHD_Serial_Trace_20260825_START_LIVE_STOP.txt'
$oldRawPath = Join-Path $repoRoot 'TestData\OTMR_RAW_CAPTURE_20260824.txt'
$ccfPath = Join-Path $repoRoot 'TestData\CLASS171_GUI_TEST.ccf'

function Read-HhdEvents([string]$path) {
    $events = [System.Collections.Generic.List[object]]::new()
    $current = $null
    foreach ($line in Get-Content -LiteralPath $path) {
        if ($line -match '^(?<event>\d+): (?<kind>.+?), (?<time>\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{7}) ') {
            if ($null -ne $current) { $events.Add([pscustomobject]$current) }
            $current = [ordered]@{
                Event = [int]$matches['event']
                EventText = $matches['event']
                Kind = $matches['kind']
                TimestampText = $matches['time']
                Timestamp = [datetime]::ParseExact(
                    $matches['time'], 'yyyy-MM-dd HH:mm:ss.fffffff',
                    [Globalization.CultureInfo]::InvariantCulture)
                Bytes = [System.Collections.Generic.List[byte]]::new()
                Detail = [System.Collections.Generic.List[string]]::new()
            }
            continue
        }
        if ($null -eq $current) { continue }
        if ($line -match '^\s(?<hex>[0-9A-F]{2}(?: [0-9A-F]{2}){0,15})\s{2,}') {
            foreach ($value in $matches['hex'].Split(' ')) {
                $current.Bytes.Add([Convert]::ToByte($value, 16))
            }
        } elseif ($line.Length -gt 0) {
            $current.Detail.Add($line)
        }
    }
    if ($null -ne $current) { $events.Add([pscustomobject]$current) }
    return $events
}

function Read-FramesFromEvents([object[]]$events, [string]$kind, [int]$minimumEvent, [int]$maximumEvent) {
    $stream = [System.Collections.Generic.List[byte]]::new()
    foreach ($event in $events | Where-Object {
        $_.Kind -eq $kind -and $_.Event -ge $minimumEvent -and $_.Event -le $maximumEvent
    }) {
        foreach ($value in $event.Bytes) { $stream.Add($value) }
    }

    $frames = [System.Collections.Generic.List[byte[]]]::new()
    $offset = 0
    while ($offset -lt $stream.Count) {
        while ($offset -lt $stream.Count -and $stream[$offset] -ne 0x01) { $offset++ }
        if ($offset + 9 -gt $stream.Count) { break }
        $length = 12 + $stream[$offset + 7]
        if ($offset + $length -gt $stream.Count) { break }
        [byte[]]$frame = $stream.GetRange($offset, $length).ToArray()
        if ($frame[8] -eq 0x02 -and $frame[$frame.Length - 3] -eq 0x03 -and
            $frame[$frame.Length - 1] -eq 0x04) {
            $frames.Add($frame)
            $offset += $length
        } else {
            $offset++
        }
    }
    return $frames
}

function Read-OldSoftwareRxFrames {
    $stream = [System.Collections.Generic.List[byte]]::new()
    foreach ($line in Get-Content -LiteralPath $oldRawPath) {
        $parts = $line -split '  +', 4
        if ($parts.Count -lt 3 -or $parts[1] -ne 'RX' -or
            -not $parts[0].StartsWith('2026-08-24T14:32:', [StringComparison]::Ordinal)) {
            continue
        }
        foreach ($value in $parts[2].Split(' ', [StringSplitOptions]::RemoveEmptyEntries)) {
            $stream.Add([Convert]::ToByte($value, 16))
        }
    }
    $syntheticEvent = [pscustomobject]@{ Kind='Read Request (UP)'; Event=1; Bytes=$stream }
    return @(Read-FramesFromEvents @($syntheticEvent) 'Read Request (UP)' 0 2)
}

function Test-EqualBytes([byte[]]$left, [byte[]]$right) {
    if ($left.Length -ne $right.Length) { return $false }
    for ($index = 0; $index -lt $left.Length; $index++) {
        if ($left[$index] -ne $right[$index]) { return $false }
    }
    return $true
}

function Format-Bytes([byte[]]$bytes) {
    if ($bytes.Length -eq 0) { return '-' }
    return (($bytes | ForEach-Object { '{0:X2}' -f $_ }) -join ' ')
}

function Get-Payload([byte[]]$frame) {
    if ($frame.Length -le 12) { return [byte[]]@() }
    return [byte[]]$frame[9..($frame.Length - 4)]
}

function Get-Stream([byte[][]]$frames, [int[]]$indexes) {
    $values = [System.Collections.Generic.List[byte]]::new()
    foreach ($index in $indexes) {
        foreach ($value in (Get-Payload $frames[$index])) { $values.Add($value) }
    }
    return [byte[]]$values.ToArray()
}

function Get-Hash([byte[]]$bytes) {
    $sha = [Security.Cryptography.SHA256]::Create()
    try { return [BitConverter]::ToString($sha.ComputeHash($bytes)).Replace('-', '') }
    finally { $sha.Dispose() }
}

function Get-Event([object[]]$events, [int]$number) {
    return $events | Where-Object Event -eq $number | Select-Object -First 1
}

function Get-Milliseconds([datetime]$from, [datetime]$to) {
    return [Math]::Round(($to - $from).TotalMilliseconds, 4)
}

function Add-ProvenanceCopy(
    [System.Collections.Generic.List[object]]$target,
    [object[]]$source,
    [int]$start,
    [int]$endExclusive) {
    for ($offset = $start; $offset -lt $endExclusive; $offset++) {
        $target.Add($source[$offset])
    }
}

function Add-ProvenanceConstant(
    [System.Collections.Generic.List[object]]$target,
    [byte]$value,
    [string]$reference) {
    $target.Add([pscustomobject]@{
        Value = $value
        SourceCategory = 'protocol constant'
        SourceReference = $reference
    })
}

$oldEvents = @(Read-HhdEvents $oldPath)
$newEvents = @(Read-HhdEvents $newPath)

[byte[][]]$oldTx = @(Read-FramesFromEvents $oldEvents 'Write Request (DOWN)' 0 999999)
[byte[][]]$oldRx = @(Read-OldSoftwareRxFrames)
[byte[][]]$startTx = @(Read-FramesFromEvents $newEvents 'Write Request (DOWN)' 41 253)
[byte[][]]$startRx = @(Read-FramesFromEvents $newEvents 'Read Request (UP)' 44 238)
[byte[][]]$stopTx = @(Read-FramesFromEvents $newEvents 'Write Request (DOWN)' 1033 1245)
[byte[][]]$stopRx = @(Read-FramesFromEvents $newEvents 'Read Request (UP)' 1036 1230)

foreach ($namedSet in @(
    [pscustomobject]@{ Name='oldTx'; Frames=$oldTx },
    [pscustomobject]@{ Name='oldRx'; Frames=$oldRx },
    [pscustomobject]@{ Name='startTx'; Frames=$startTx },
    [pscustomobject]@{ Name='startRx'; Frames=$startRx },
    [pscustomobject]@{ Name='stopTx'; Frames=$stopTx },
    [pscustomobject]@{ Name='stopRx'; Frames=$stopRx }
)) {
    if ($namedSet.Frames.Count -ne 19) {
        throw "Expected 19 protocol frames in $($namedSet.Name), found $($namedSet.Frames.Count)."
    }
}

$allOldStartTxEqual = $true
$allOldStopTxEqual = $true
$allRxEqual = $true
$allStartStopRxEqual = $true
for ($index = 0; $index -lt 19; $index++) {
    $allOldStartTxEqual = $allOldStartTxEqual -and (Test-EqualBytes $oldTx[$index] $startTx[$index])
    $allOldStopTxEqual = $allOldStopTxEqual -and (Test-EqualBytes $oldTx[$index] $stopTx[$index])
    $allRxEqual = $allRxEqual -and
        (Test-EqualBytes $oldRx[$index] $startRx[$index]) -and
        (Test-EqualBytes $oldRx[$index] $stopRx[$index])
    $allStartStopRxEqual = $allStartStopRxEqual -and (Test-EqualBytes $startRx[$index] $stopRx[$index])
}

[int[]]$dataFrameIndexes = @(1, 3, 5, 7, 9, 11)
[int[]]$writePageIndexes = @(12, 13, 14, 15, 16, 17)
[byte[]]$oldRxStream = Get-Stream $oldRx $dataFrameIndexes
[byte[]]$startRxStream = Get-Stream $startRx $dataFrameIndexes
[byte[]]$stopRxStream = Get-Stream $stopRx $dataFrameIndexes
[byte[]]$oldTxStream = Get-Stream $oldTx $writePageIndexes
[byte[]]$startTxStream = Get-Stream $startTx $writePageIndexes
[byte[]]$stopTxStream = Get-Stream $stopTx $writePageIndexes
[byte[]]$selectedCcf = [IO.File]::ReadAllBytes($ccfPath)

Write-Output 'Protocol exchange equality:'
'2026-08-24 TX frames={0}; 2026-08-25 START TX frames={1}; STOP TX frames={2}' -f
    $oldTx.Count, $startTx.Count, $stopTx.Count
'old START vs new START TX identical={0}; old START vs new STOP TX identical={1}; new START/STOP RX identical={2}; all-date RX identical={3}' -f
    $allOldStartTxEqual, $allOldStopTxEqual, $allStartStopRxEqual, $allRxEqual
'old RX stream SHA256={0}; old TX stream SHA256={1}' -f (Get-Hash $oldRxStream), (Get-Hash $oldTxStream)

$unknownFields = @(
    [pscustomobject]@{ Event='000312'; FrameIndex=12; Offsets=@(0x009); RelatedSourceOffsets=@(0x000); SourceCount=5; Rule='5-to-1 replacement' },
    [pscustomobject]@{ Event='000312'; FrameIndex=12; Offsets=@(0x08B); RelatedSourceOffsets=@(0x086); SourceCount=1; Rule='one-byte replacement' },
    [pscustomobject]@{ Event='000312'; FrameIndex=12; Offsets=@(0x0BD); RelatedSourceOffsets=@(0x0B8); SourceCount=1; Rule='one-byte replacement' },
    [pscustomobject]@{ Event='000357'; FrameIndex=14; Offsets=@(0x086,0x087); RelatedSourceOffsets=@((2*0xFF)+0x6B); SourceCount=2; Rule='zero insertion after source removal' },
    [pscustomobject]@{ Event='000357'; FrameIndex=14; Offsets=@(0x0D6,0x0D7); RelatedSourceOffsets=@((2*0xFF)+0xCB); SourceCount=2; Rule='zero insertion after source removal' },
    [pscustomobject]@{ Event='000378'; FrameIndex=15; Offsets=@(0x00F,0x010); RelatedSourceOffsets=@((2*0xFF)+0xEB); SourceCount=2; Rule='cross-page zero insertion after source removal' },
    [pscustomobject]@{ Event='000378'; FrameIndex=15; Offsets=@(0x067,0x068); RelatedSourceOffsets=@((3*0xFF)+0x5C); SourceCount=2; Rule='zero insertion after source removal' },
    [pscustomobject]@{ Event='000378'; FrameIndex=15; Offsets=@(0x097,0x098); RelatedSourceOffsets=@((3*0xFF)+0x74); SourceCount=2; Rule='zero insertion after source removal' },
    [pscustomobject]@{ Event='000423'; FrameIndex=17; Offsets=@(0x0CD); RelatedSourceOffsets=@((5*0xFF)+0xC8); SourceCount=1; Rule='one-byte replacement after global shift' }
)

$rows = [System.Collections.Generic.List[object]]::new()
foreach ($field in $unknownFields) {
    for ($fieldByte = 0; $fieldByte -lt $field.Offsets.Count; $fieldByte++) {
        $frameOffset = $field.Offsets[$fieldByte]
        $sourceOffset = $field.RelatedSourceOffsets[0] + [Math]::Min($fieldByte, $field.SourceCount - 1)
        $txIdentical = $oldTx[$field.FrameIndex][$frameOffset] -eq $startTx[$field.FrameIndex][$frameOffset] -and
            $oldTx[$field.FrameIndex][$frameOffset] -eq $stopTx[$field.FrameIndex][$frameOffset]
        $rxIdentical = $oldRxStream[$sourceOffset] -eq $startRxStream[$sourceOffset] -and
            $oldRxStream[$sourceOffset] -eq $stopRxStream[$sourceOffset]
        $isPhaseDependent = $field.Event -eq '000312' -and $frameOffset -in 0x08B, 0x0BD
        $ccfOffset = if ($frameOffset -eq 0x08B) { 0x0231 } elseif ($frameOffset -eq 0x0BD) { 0x0263 } else { -1 }
        $resolvedByCcf = $ccfOffset -ge 0
        $resolved = -not $isPhaseDependent -or $resolvedByCcf
        $classification = if ($field.Rule -like '*zero insertion*') {
            'resolved repeated structural zero insertion across three exchanges'
        } elseif ($field.Event -eq '000312' -and $frameOffset -eq 0x009) {
            'resolved repeated continuous-stream prefix constant across three exchanges'
        } elseif ($field.Event -eq '000423') {
            'resolved repeated continuous-stream terminal constant across three exchanges'
        } elseif ($frameOffset -eq 0x08B) {
            'resolved phase-selected value: START uses selected CCF 0x0231; STOP uses cached recorder value; wire=(FE-value) mod 256'
        } else {
            'resolved phase-selected value: START uses selected CCF 0x0263; STOP uses cached recorder value; wire=(03-value) mod 256'
        }
        $phaseFinding = if ($field.Event -eq '000312' -and $frameOffset -eq 0x08B) {
            'START CCF[0x0231]=04 -> FA; STOP cached RX=01 -> FD; wire=(FE-source) mod 256'
        } elseif ($field.Event -eq '000312' -and $frameOffset -eq 0x0BD) {
            'START CCF[0x0263]=00 -> 03; STOP cached RX=03 -> 00; wire=(03-source) mod 256'
        } else { 'identical in old START, new START, and new STOP exchanges' }
        $rows.Add([pscustomobject]@{
            Event = $field.Event
            FrameOffset = '0x{0:X3}' -f $frameOffset
            Tx20260824 = '{0:X2}' -f $oldTx[$field.FrameIndex][$frameOffset]
            Tx20260825Start = '{0:X2}' -f $startTx[$field.FrameIndex][$frameOffset]
            Tx20260825Stop = '{0:X2}' -f $stopTx[$field.FrameIndex][$frameOffset]
            TxIdenticalAllExchanges = $txIdentical
            RelatedContinuousRxOffset = '0x{0:X3}' -f $sourceOffset
            Rx20260824 = '{0:X2}' -f $oldRxStream[$sourceOffset]
            Rx20260825Start = '{0:X2}' -f $startRxStream[$sourceOffset]
            Rx20260825Stop = '{0:X2}' -f $stopRxStream[$sourceOffset]
            RxIdenticalAllExchanges = $rxIdentical
            Transform = $field.Rule
            PhaseFinding = $phaseFinding
            SelectedCcfOffset = if ($ccfOffset -ge 0) { '0x{0:X4}' -f $ccfOffset } else { '' }
            SelectedCcfValue = if ($ccfOffset -ge 0) { '{0:X2}' -f $selectedCcf[$ccfOffset] } else { '' }
            ResolvedBySecondHhd = -not $isPhaseDependent
            ResolvedByCcfEvidence = $resolvedByCcf
            ResolvedOverall = $resolved
            Classification = $classification
        })
    }
}

Write-Output 'Previously unexplained bytes:'
$rows | Format-Table Event,FrameOffset,Tx20260824,Tx20260825Start,Tx20260825Stop,
    RelatedContinuousRxOffset,Rx20260824,Rx20260825Start,Rx20260825Stop -AutoSize |
    Out-String -Width 220 | Write-Output

# Build the complete START stream again from explicit provenance-bearing rules.
# This mirrors the non-transmitting C# generator and asserts against both HHD
# START captures before the table is exported.
$rxSourceStream = [System.Collections.Generic.List[object]]::new()
$sourceTransactions = 0x02, 0x04, 0x06, 0x08, 0x0A, 0x0C
for ($page = 0; $page -lt $dataFrameIndexes.Count; $page++) {
    [byte[]]$payload = Get-Payload $oldRx[$dataFrameIndexes[$page]]
    for ($offset = 0; $offset -lt $payload.Length; $offset++) {
        $rxSourceStream.Add([pscustomobject]@{
            Value = $payload[$offset]
            SourceCategory = 'recorder RX'
            SourceReference = ('RX 01 {0:X2} payload 0x{1:X2}' -f $sourceTransactions[$page], $offset)
        })
    }
}
[object[]]$rxSources = $rxSourceStream.ToArray()
$txSources = [System.Collections.Generic.List[object]]::new()
$page3 = 2 * 0xFF
$page4 = 3 * 0xFF
$page6 = 5 * 0xFF
Add-ProvenanceConstant $txSources 0x02 'proven continuous-stream prefix replacement'
Add-ProvenanceCopy $txSources $rxSources 0x005 0x086
$txSources.Add([pscustomobject]@{
    Value = [byte]((0xFE - $selectedCcf[0x0231]) -band 0xFF)
    SourceCategory = 'selected CCF'
    SourceReference = 'CCF 0x0231 transformed as (FE-value) mod 256'
})
Add-ProvenanceCopy $txSources $rxSources 0x087 0x0B8
$txSources.Add([pscustomobject]@{
    Value = [byte]((0x03 - $selectedCcf[0x0263]) -band 0xFF)
    SourceCategory = 'selected CCF'
    SourceReference = 'CCF 0x0263 transformed as (03-value) mod 256'
})
Add-ProvenanceCopy $txSources $rxSources 0x0B9 ($page3 + 0x6B)
Add-ProvenanceCopy $txSources $rxSources ($page3 + 0x6D) ($page3 + 0x83)
Add-ProvenanceConstant $txSources 0x00 'proven structural zero insertion after RX page 3 payload 0x6B..0x6C removal'
Add-ProvenanceConstant $txSources 0x00 'proven structural zero insertion after RX page 3 payload 0x6B..0x6C removal'
Add-ProvenanceCopy $txSources $rxSources ($page3 + 0x83) ($page3 + 0xCB)
Add-ProvenanceCopy $txSources $rxSources ($page3 + 0xCD) ($page3 + 0xD3)
Add-ProvenanceConstant $txSources 0x00 'proven structural zero insertion after RX page 3 payload 0xCB..0xCC removal'
Add-ProvenanceConstant $txSources 0x00 'proven structural zero insertion after RX page 3 payload 0xCB..0xCC removal'
Add-ProvenanceCopy $txSources $rxSources ($page3 + 0xD3) ($page3 + 0xEB)
Add-ProvenanceCopy $txSources $rxSources ($page3 + 0xED) ($page4 + 0x0C)
Add-ProvenanceConstant $txSources 0x00 'proven cross-page zero insertion after RX page 3 payload 0xEB..0xEC removal'
Add-ProvenanceConstant $txSources 0x00 'proven cross-page zero insertion after RX page 3 payload 0xEB..0xEC removal'
Add-ProvenanceCopy $txSources $rxSources ($page4 + 0x0C) ($page4 + 0x5C)
Add-ProvenanceCopy $txSources $rxSources ($page4 + 0x5E) ($page4 + 0x64)
Add-ProvenanceConstant $txSources 0x00 'proven structural zero insertion after RX page 4 payload 0x5C..0x5D removal'
Add-ProvenanceConstant $txSources 0x00 'proven structural zero insertion after RX page 4 payload 0x5C..0x5D removal'
Add-ProvenanceCopy $txSources $rxSources ($page4 + 0x64) ($page4 + 0x74)
Add-ProvenanceCopy $txSources $rxSources ($page4 + 0x76) ($page4 + 0x94)
Add-ProvenanceConstant $txSources 0x00 'proven structural zero insertion after RX page 4 payload 0x74..0x75 removal'
Add-ProvenanceConstant $txSources 0x00 'proven structural zero insertion after RX page 4 payload 0x74..0x75 removal'
Add-ProvenanceCopy $txSources $rxSources ($page4 + 0x94) ($page6 + 0xC8)
Add-ProvenanceConstant $txSources 0x40 'proven continuous-stream terminal replacement'
Add-ProvenanceCopy $txSources $rxSources ($page6 + 0xC9) $rxSources.Length

if ($txSources.Count -ne $oldTxStream.Length -or $txSources.Count -ne $startTxStream.Length) {
    throw "Generated START payload stream length $($txSources.Count) does not match both captures."
}
for ($offset = 0; $offset -lt $txSources.Count; $offset++) {
    if ($txSources[$offset].Value -ne $oldTxStream[$offset] -or
        $txSources[$offset].Value -ne $startTxStream[$offset]) {
        throw ('Generated START payload mismatch at continuous offset 0x{0:X3}.' -f $offset)
    }
}

$provenanceRows = [System.Collections.Generic.List[object]]::new()
$blockedEvents = '000301', '000312', '000335', '000357', '000378', '000401', '000423'
[byte[]]$acknowledgement = $oldTx[11]
for ($offset = 0; $offset -lt $acknowledgement.Length; $offset++) {
    $category = if ($offset -in 1, 5, 9) { 'recorder RX' }
        elseif ($offset -eq 11) { 'calculated field/checksum' }
        else { 'protocol constant' }
    $reference = if ($offset -in 1, 9) { 'RX 01 0C transaction' }
        elseif ($offset -eq 5) { 'RX 01 0C frame offset 0x004' }
        elseif ($offset -eq 11) { 'payload sum modulo 256' }
        else { 'proven acknowledgement framing/header constant' }
    $provenanceRows.Add([pscustomobject]@{
        Event = '000301'; FrameOffset = '0x{0:X3}' -f $offset
        Value = '{0:X2}' -f $acknowledgement[$offset]
        SourceCategory = $category; SourceReference = $reference
    })
}

$payloadCursor = 0
for ($page = 0; $page -lt $writePageIndexes.Count; $page++) {
    [byte[]]$frame = $oldTx[$writePageIndexes[$page]]
    $payloadLength = $frame.Length - 12
    for ($offset = 0; $offset -lt $frame.Length; $offset++) {
        if ($offset -in 1, 3) {
            $category = 'recorder RX'; $reference = 'RX 01 {0:X2} frame offset 0x003' -f $sourceTransactions[$page]
        } elseif ($offset -in 4, 5, 6) {
            $category = 'recorder RX'; $reference = ('RX 01 {0:X2} frame offset 0x{1:X3}' -f $sourceTransactions[$page], $offset)
        } elseif ($offset -eq 7) {
            $category = 'calculated field/checksum'; $reference = 'generated payload length'
        } elseif ($offset -ge 9 -and $offset -lt $frame.Length - 3) {
            $source = $txSources[$payloadCursor + $offset - 9]
            $category = $source.SourceCategory; $reference = $source.SourceReference
        } elseif ($offset -eq $frame.Length - 2) {
            $category = 'calculated field/checksum'; $reference = 'payload sum modulo 256'
        } else {
            $category = 'protocol constant'; $reference = 'proven frame/header constant'
        }
        $provenanceRows.Add([pscustomobject]@{
            Event = $blockedEvents[$page + 1]; FrameOffset = '0x{0:X3}' -f $offset
            Value = '{0:X2}' -f $frame[$offset]
            SourceCategory = $category; SourceReference = $reference
        })
    }
    $payloadCursor += $payloadLength
}

Write-Output 'Complete START provenance counts:'
$provenanceRows | Group-Object SourceCategory | Sort-Object Name | ForEach-Object {
    Write-Output ('{0}={1}' -f $_.Name, $_.Count)
}

$liveRead = Get-Event $newEvents 294
$liveClose = Get-Event $newEvents 991
$stopCreateDown = Get-Event $newEvents 993
$stopCreateUp = Get-Event $newEvents 994
$stopBaud = Get-Event $newEvents 1011
$stopRts = Get-Event $newEvents 1013
$stopDtr = Get-Event $newEvents 1015
$stopLine = Get-Event $newEvents 1017
$stopPurge1 = Get-Event $newEvents 1025
$stopPurge2 = Get-Event $newEvents 1029
$stopPurge3 = Get-Event $newEvents 1031
$stopQuery = Get-Event $newEvents 1033
$stopFinal = Get-Event $newEvents 1245
$stopFinalClose = Get-Event $newEvents 1247
[byte[]]$lastLiveFrame = [byte[]]@(0xFB,0xFB,0x38,0x8F,0x38,0x90,0x38,0x8F,0x38,0x90,0xFF)
if (-not (Format-Bytes $liveRead.Bytes.ToArray()).EndsWith((Format-Bytes $lastLiveFrame))) {
    throw 'The expected final realtime frame is not the tail of HHD read 000294.'
}

$stopRows = [System.Collections.Generic.List[object]]::new()
function Add-StopRow([object]$event, [string]$operation, [string]$bytes, [string]$timing) {
    $stopRows.Add([pscustomobject]@{
        Event = '{0:D6}' -f $event.Event
        Timestamp = $event.TimestampText
        Operation = $operation
        Bytes = $bytes
        Timing = $timing
    })
}
Add-StopRow $liveRead 'Last recorded realtime RX buffer' (Format-Bytes $liveRead.Bytes.ToArray()) '0xD3 bytes; final complete frame shown in report'
Add-StopRow $liveClose 'Close live DTR-high COM session' '-' 'No intervening TX, purge, RTS, or DTR IOCTL after realtime RX'
Add-StopRow $stopCreateDown 'Open COM for STOP cleanup' '-' (('{0} ms after live close' -f (Get-Milliseconds $liveClose.Timestamp $stopCreateDown.Timestamp)))
Add-StopRow $stopCreateUp 'COM open completed' '-' (('{0} ms after live close' -f (Get-Milliseconds $liveClose.Timestamp $stopCreateUp.Timestamp)))
Add-StopRow $stopBaud 'Set baud rate 38400' '-' '-'
Add-StopRow $stopRts 'RTS LOW' '-' '-'
Add-StopRow $stopDtr 'DTR LOW' '-' '-'
Add-StopRow $stopLine 'Set 8 data bits, no parity, 1 stop bit' '-' '-'
Add-StopRow $stopPurge1 'Purge RX/TX requests and buffers' '-' '-'
Add-StopRow $stopPurge2 'Purge RX/TX requests and buffers' '-' '-'
Add-StopRow $stopPurge3 'Purge RX/TX requests and buffers' '-' '-'
Add-StopRow $stopQuery 'Begin full STOP cleanup exchange with 01 01' (Format-Bytes $stopQuery.Bytes.ToArray()) (('{0} ms after live close' -f (Get-Milliseconds $liveClose.Timestamp $stopQuery.Timestamp)))
Add-StopRow $stopFinal 'Final 01 07 after complete 01 13' (Format-Bytes $stopFinal.Bytes.ToArray()) '-'
Add-StopRow $stopFinalClose 'Final COM close; no subsequent reopen observed' '-' (('{0} ms after final 01 07' -f (Get-Milliseconds $stopFinal.Timestamp $stopFinalClose.Timestamp)))

Write-Output 'STOP summary:'
'last live frame={0}' -f (Format-Bytes $lastLiveFrame)
'live close -> cleanup open DOWN={0} ms; live close -> cleanup open UP={1} ms; cleanup query={2} ms; final 01 07 -> close={3} ms' -f
    (Get-Milliseconds $liveClose.Timestamp $stopCreateDown.Timestamp),
    (Get-Milliseconds $liveClose.Timestamp $stopCreateUp.Timestamp),
    (Get-Milliseconds $liveClose.Timestamp $stopQuery.Timestamp),
    (Get-Milliseconds $stopFinal.Timestamp $stopFinalClose.Timestamp)
'TX between live reopen and live close={0}; STOP cleanup protocol frames={1}; reopen after final STOP close={2}' -f
    (@($newEvents | Where-Object { $_.Kind -eq 'Write Request (DOWN)' -and $_.Event -gt 253 -and $_.Event -lt 991 }).Count),
    $stopTx.Count,
    (@($newEvents | Where-Object { $_.Kind -eq 'Create Request (DOWN)' -and $_.Event -gt 1247 }).Count -gt 0)

if ($WriteCsv) {
    $unknownOutput = Join-Path $repoRoot 'docs\CLASS171_OTMR_HHD_UNKNOWN_BYTES_20260824_20260825.csv'
    $rows | Export-Csv -LiteralPath $unknownOutput -NoTypeInformation -Encoding UTF8
    Write-Output "Wrote $unknownOutput"

    $exchangeOutput = Join-Path $repoRoot 'docs\CLASS171_OTMR_HHD_EXCHANGE_COMPARISON_20260824_20260825.csv'
    $exchangeRows = for ($index = 0; $index -lt 19; $index++) {
        [pscustomobject]@{
            FrameIndex = $index
            Transaction = '01 {0:X2}' -f $oldTx[$index][1]
            Length = $oldTx[$index].Length
            Tx20260824Sha256 = Get-Hash $oldTx[$index]
            Tx20260825StartSha256 = Get-Hash $startTx[$index]
            Tx20260825StopSha256 = Get-Hash $stopTx[$index]
            AllTxIdentical = (Test-EqualBytes $oldTx[$index] $startTx[$index]) -and (Test-EqualBytes $oldTx[$index] $stopTx[$index])
            Rx20260824Sha256 = Get-Hash $oldRx[$index]
            Rx20260825StartSha256 = Get-Hash $startRx[$index]
            Rx20260825StopSha256 = Get-Hash $stopRx[$index]
            AllRxIdentical = (Test-EqualBytes $oldRx[$index] $startRx[$index]) -and (Test-EqualBytes $oldRx[$index] $stopRx[$index])
        }
    }
    $exchangeRows | Export-Csv -LiteralPath $exchangeOutput -NoTypeInformation -Encoding UTF8
    Write-Output "Wrote $exchangeOutput"

    $stopOutput = Join-Path $repoRoot 'docs\CLASS171_OTMR_STOP_TIMELINE_20260825.csv'
    $stopRows | Export-Csv -LiteralPath $stopOutput -NoTypeInformation -Encoding UTF8
    Write-Output "Wrote $stopOutput"

    $provenanceOutput = Join-Path $repoRoot 'docs\CLASS171_OTMR_GENERATED_START_PROVENANCE.csv'
    $provenanceRows | Export-Csv -LiteralPath $provenanceOutput -NoTypeInformation -Encoding UTF8
    Write-Output "Wrote $provenanceOutput"
}
