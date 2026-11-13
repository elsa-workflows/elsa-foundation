# Shared behavioral oracles used by the actual journey and its retained-receipt controls.
function Assert-CommittedText($Detail, [string] $Expected) {
    if ($Detail.payload.preview -cne $Expected) {
        throw "Committed consumer input differs: expected '$Expected', got '$($Detail.payload.preview)'"
    }
}
function Assert-PublicationUnchanged($Before, $After) {
    $beforeJson = ConvertTo-Json -InputObject $Before -Depth 100 -Compress
    $afterJson = ConvertTo-Json -InputObject $After -Depth 100 -Compress
    if ($beforeJson -cne $afterJson) { throw 'Refused publication changed owned artifact/reference/publication/activation rows' }
}
