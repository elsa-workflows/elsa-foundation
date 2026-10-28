param([string]$OldPath,[string]$CurrentPath,[string]$OutputPath)
$ErrorActionPreference = 'Stop'
function Read-PublicTypes([string]$Path) {
    $stream = [System.IO.File]::OpenRead($Path)
    $pe = [System.Reflection.PortableExecutable.PEReader]::new($stream)
    try {
        $reader = [System.Reflection.Metadata.PEReaderExtensions]::GetMetadataReader($pe)
        $names = @(foreach ($handle in $reader.TypeDefinitions) {
            $type = $reader.GetTypeDefinition($handle)
            $visibility = [int]$type.Attributes -band [int][System.Reflection.TypeAttributes]::VisibilityMask
            if ($visibility -in @([int][System.Reflection.TypeAttributes]::Public,[int][System.Reflection.TypeAttributes]::NestedPublic)) {
                $name = $reader.GetString($type.Name)
                $ns = $reader.GetString($type.Namespace)
                if ($ns) { "$ns.$name" } else { $name }
            }
        }) | Sort-Object
        [ordered]@{ assemblyVersion = [System.Reflection.AssemblyName]::GetAssemblyName($Path).Version.ToString(); sha256 = (Get-FileHash $Path -Algorithm SHA256).Hash.ToLowerInvariant(); publicTypes = $names }
    } finally { $pe.Dispose(); $stream.Dispose() }
}
$old = Read-PublicTypes $OldPath
$current = Read-PublicTypes $CurrentPath
[ordered]@{ schemaVersion = 1; scope = 'PE metadata only; assemblies are not loaded or executed. Public type inventory, not full member ABI equivalence.'; legacy = $old; current = $current; removedPublicTypes = @($old.publicTypes | Where-Object { $_ -cnotin $current.publicTypes }); addedPublicTypes = @($current.publicTypes | Where-Object { $_ -cnotin $old.publicTypes }); retainedPublicTypes = @($current.publicTypes | Where-Object { $_ -cin $old.publicTypes }) } | ConvertTo-Json -Depth 10 | Set-Content $OutputPath
