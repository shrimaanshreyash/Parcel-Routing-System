$ErrorActionPreference = 'Stop'

# These generated files prove production-sized limits without permanently
# storing multi-megabyte or ten-thousand-row artifacts in the repository.
$outputDirectory = Join-Path $PSScriptRoot 'generated'
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null

$largePadding = 'x' * 2100000
$largeDocument = @"
<Container><parcels><Parcel><Weight>1</Weight><Value>10</Value><Country>GB</Country><Receipient><Note>$largePadding</Note></Receipient></Parcel></parcels></Container>
"@
[System.IO.File]::WriteAllText(
    (Join-Path $outputDirectory '09-over-2mb.xml'),
    $largeDocument,
    [System.Text.UTF8Encoding]::new($false))

$characterPadding = 'x' * 2000001
$characterDocument = @"
<Container><parcels><Parcel><Weight>1</Weight><Value>10</Value><Country>GB</Country><Receipient><Note>$characterPadding</Note></Receipient></Parcel></parcels></Container>
"@
[System.IO.File]::WriteAllText(
    (Join-Path $outputDirectory '10-over-2000000-characters.xml'),
    $characterDocument,
    [System.Text.UTF8Encoding]::new($false))

$row = '<Parcel><Weight>1</Weight><Value>10</Value><Country>GB</Country></Parcel>'
$rowDocument = '<Container><parcels>' + ($row * 10001) + '</parcels></Container>'
[System.IO.File]::WriteAllText(
    (Join-Path $outputDirectory '11-over-10000-rows.xml'),
    $rowDocument,
    [System.Text.UTF8Encoding]::new($false))

Get-ChildItem -LiteralPath $outputDirectory | Select-Object Name, Length
