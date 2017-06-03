# Parameters
param (
    [Parameter(Mandatory = $true)] 
    $DetailsFilePath
)

# Load blob copy details file (ex: copyblobdetails.json)
$copyblobdetails = Get-Content -Path $DetailsFilePath -Raw | ConvertFrom-Json
$copyblobdetailsout = @()


    # Delete used snapshots
    foreach ($copyblobdetail in $copyblobdetails)
    {
        # Create source storage account context
        $source_context = New-AzureStorageContext -StorageAccountName $copyblobdetail.SourceSA -StorageAccountKey $copyblobdetail.SourceKey -Environment $copyblobdetail.SourceEnvironment

        $source_container = Get-AzureStorageContainer -Context $source_context -Name $copyblobdetail.SourceContainer
        $blobs = $source_container.CloudBlobContainer.ListBlobs($copyblobdetail.SourceBlob, $true, "Snapshots") | Where-Object { $_.SnapshotTime -ne $null }
        
        Write-Host $blobs.Count

        foreach ($snap in $blobs)
        {
            $snap.Delete()
        }
    }

