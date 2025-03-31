# PowerShell script to automate creation and management of ReferenceFile.cs

# Path to the project and files
$projectPath = "S:\OneDrive\Documents\Repositories\Cliparino\Cliparino\"
$cphInlineFile = Join-Path $projectPath "CPHInline.cs"
$referenceFile = Join-Path $projectPath "ReferenceFile.cs"
$csprojFile = Get-ChildItem -Path $projectPath -Filter *.csproj

# Function to create or update ReferenceFile.cs
function Make-ReferenceFile
{
    param (
        [Parameter(Mandatory = $true)]
        [string]$inlineFile,
        [Parameter(Mandatory = $true)]
        [string]$refFile
    )

    # Check if ReferenceFile.1cs needs to be created or updated
    if (Test-Path $inlineFile)
    {
        $inlineContent = Get-Content $inlineFile
        $refContent = @"
/// <summary>
/// This file is autogenerated for reference purposes and is excluded from build.
/// </summary>
$inlineContent
"@
        Set-Content -Path $refFile -Value $refContent -Force
        Write-Host "`nReference file has been created/updated at $refFile."
    }
    else
    {
        Write-Error "CPHInline.cs not found at $inlineFile."
    }
}

# Function to modify .csproj to exclude ReferenceFile.cs
function Exclude-ReferenceFileFromCompilation
{
    param (
        [string]$projFile,
        [string]$refFileName
    )

    if (Test-Path $projFile)
    {
        $projContent = Get-Content $projFile
        $excludeTag = "<Compile Remove=`"$refFileName`" />"
        $includeTag = "<None Include=`"$refFileName`" />"

        if (-not ($projContent -match [regex]::Escape($excludeTag)))
        {
            $projectXml = [xml](Get-Content $projFile -Raw)
            $itemGroup = $projectXml.CreateElement("ItemGroup")
            $compileNode = $projectXml.CreateElement("Compile")
            $compileNode.SetAttribute("Remove", $refFileName)
            $itemGroup.AppendChild($compileNode) | Out-Null
            $noneNode = $projectXml.CreateElement("None")
            $noneNode.SetAttribute("Include", $refFileName)
            $itemGroup.AppendChild($noneNode) | Out-Null
            $projectXml.Project.AppendChild($itemGroup) | Out-Null
            $projectXml.Save($projFile)
            Write-Host "`n.csproj file has been updated to exclude $refFileName from compilation."
        }
        else
        {
            Write-Host "`n.csproj file already excludes $refFileName from compilation."
        }
    }
    else
    {
        Write-Error ".csproj file not found at $projFile."
    }
}

# Main script execution
Make-ReferenceFile -inlineFile $cphInlineFile -refFile $referenceFile
$referenceFileName = Split-Path -Leaf $referenceFile
Exclude-ReferenceFileFromCompilation -projFile $csprojFile.FullName -refFileName $referenceFileName