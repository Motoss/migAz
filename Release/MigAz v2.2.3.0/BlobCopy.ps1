# Parameters
param (
    [Parameter(Mandatory = $true)] 
    $ResourcegroupName,

    [Parameter(Mandatory = $true)] 
    $DetailsFilePath,

    [ValidateSet("StartBlobCopy", "MonitorBlobCopy", "CancelBlobCopy")]
    [Parameter(Mandatory = $true)] 
    $StartType,

    $RefreshInterval = 10
)

# Check Azure.Storage version - minimum 2.0.1 required
if ((get-command New-AzureStorageContext).Version.ToString() -lt '2.0.1')
{
    Write-Host 'Please update Azure.Storage module to 2.0.1 or higher before running this script' -ForegroundColor Yellow
    Exit
}

# Check AzureRM.Storage version - minimum 2.0.1 required
if ((get-command Get-AzureRmStorageAccountKey).Version.ToString() -lt '2.0.1')
{
    Write-Host 'Please update AzureRM.Storage module to 2.0.1 or higher before running this script' -ForegroundColor Yellow
    Exit
}

# Load blob copy details file (ex: copyblobdetails.json)
$copyblobdetails = Get-Content -Path $DetailsFilePath -Raw | ConvertFrom-Json
$copyblobdetailsout = @()


# If Initiating the copy of all blobs
If ($StartType -eq "StartBlobCopy")
{
    # Initiate the copy of all blobs
    foreach ($copyblobdetail in $copyblobdetails)
    {
        # Create source storage account context
        $source_context = New-AzureStorageContext -StorageAccountName $copyblobdetail.SourceSA -StorageAccountKey $copyblobdetail.SourceKey -Environment $copyblobdetail.SourceEnvironment
    
        # Create destination storage account context
        $copyblobdetail.DestinationKey = (Get-AzureRmStorageAccount -ResourceGroupName $resourcegroupname -Name $copyblobdetail.DestinationSA | Get-AzureRmStorageAccountKey).Value[0]
        $destination_context = New-AzureStorageContext -StorageAccountName $copyblobdetail.DestinationSA -StorageAccountKey $copyblobdetail.DestinationKey

        # Create destination container if it does not exist
        $destination_container = Get-AzureStorageContainer -Context $destination_context -Name $copyblobdetail.DestinationContainer -ErrorAction SilentlyContinue
        if ($destination_container -eq $null)
        { 
            New-AzureStorageContainer -Context $destination_context -Name $copyblobdetail.DestinationContainer
        }

        #Get a reference to a blob
        $blob = Get-AzureStorageBlob -Context $source_context -Container $copyblobdetail.SourceContainer -Blob $copyblobdetail.SourceBlob
        #Create a snapshot of the blob
        $snap = $blob.ICloudBlob.CreateSnapshot()
        $copyblobdetail.SnapshotTime = $snap.SnapshotTime.DateTime.ToString()
        # Get just created snapshot
        $snapshot = Get-AzureStorageBlob –Context $source_context -Container $copyblobdetail.SourceContainer -Prefix $copyblobdetail.SourceBlob | Where-Object  { $_.Name -eq $copyblobdetail.SourceBlob -and $_.SnapshotTime -eq $snap.SnapshotTime }
        # Convert to CloudBlob object type
        $snapshot = [Microsoft.WindowsAzure.Storage.Blob.CloudBlob] $snapshot.ICloudBlob
            
        # Initiate blob snapshot copy job
        Start-AzureStorageBlobCopy –Context $source_context -ICloudBlob $snapshot -DestContext $destination_context -DestContainer $copyblobdetail.DestinationContainer -DestBlob $copyblobdetail.DestinationBlob -Verbose

        # Updates $copyblobdetailsout array
        $copyblobdetail.StartTime = Get-Date -Format u
        $copyblobdetailsout += $copyblobdetail

        # Updates screen table
        # cls
        $copyblobdetails | select DestinationSA, DestinationContainer, DestinationBlob, Status, BytesCopied, TotalBytes, StartTime, EndTime | Format-Table -AutoSize
    }

    # Updates file with data from $copyblobdetailsout
    $copyblobdetailsout | ConvertTo-Json -Depth 100 | Out-File $DetailsFilePath
}

# If waiting for all blobs to copy and get statistics
If ($StartType -eq "MonitorBlobCopy")
{
    # Waits for all blobs to copy and get statistics
    $continue = $true
    while ($continue)
    {
        $continue = $false
        foreach ($copyblobdetail in $copyblobdetails)
        {
            if ($copyblobdetail.Status -ne "Success" -and $copyblobdetail.Status -ne "Failed")
            {
                $destination_context = New-AzureStorageContext -StorageAccountName $copyblobdetail.DestinationSA -StorageAccountKey $copyblobdetail.DestinationKey
                $status = Get-AzureStorageBlobCopyState -Context $destination_context -Container $copyblobdetail.DestinationContainer -Blob $copyblobdetail.DestinationBlob

                $copyblobdetail.TotalBytes = "{0:N0} MB" -f ($status.TotalBytes / 1MB)
                $copyblobdetail.BytesCopied = "{0:N0} MB" -f ($status.BytesCopied / 1MB)
                $copyblobdetail.Status = $status.Status
                $copyblobdetail.EndTime = Get-Date -Format u

                $continue = $true
            }
        }

        $copyblobdetails | ConvertTo-Json -Depth 100 | Out-File $DetailsFilePath
        # cls
        $copyblobdetails | select DestinationSA, DestinationContainer, DestinationBlob, Status, BytesCopied, TotalBytes, StartTime, EndTime | Format-Table -AutoSize

        Start-Sleep -Seconds $refreshinterval
    }

    # Delete used snapshots
    foreach ($copyblobdetail in $copyblobdetails)
    {
        # Create source storage account context
        $source_context = New-AzureStorageContext -StorageAccountName $copyblobdetail.SourceSA -StorageAccountKey $copyblobdetail.SourceKey -Environment $copyblobdetail.SourceEnvironment

        $source_container = Get-AzureStorageContainer -Context $source_context -Name $copyblobdetail.SourceContainer
        $blobs = $source_container.CloudBlobContainer.ListBlobs($copyblobdetail.SourceBlob, $true, "Snapshots") | Where-Object { $_.SnapshotTime -ne $null -and $_.SnapshotTime.DateTime.ToString() -EQ $copyblobdetail.SnapshotTime }
        $blobs[0].Delete()
    }
}


# If to Cancel blobs copy
If ($StartType -eq "CancelBlobCopy")
{
    foreach ($copyblobdetail in $copyblobdetails)
    {
        if ($copyblobdetail.Status -ne "Success" -and $copyblobdetail.Status -ne "Failed")
        {
            $destination_context = New-AzureStorageContext -StorageAccountName $copyblobdetail.DestinationSA -StorageAccountKey $copyblobdetail.DestinationKey
            Stop-AzureStorageBlobCopy -Context $destination_context -Container $copyblobdetail.DestinationContainer -Blob $copyblobdetail.DestinationBlob -Force -Verbose

            $copyblobdetail.Status = "Canceled"
            $copyblobdetail.EndTime = Get-Date -Format u
        }
    }

    $copyblobdetails | ConvertTo-Json -Depth 100 | Out-File $DetailsFilePath
    # cls
    $copyblobdetails | select DestinationSA, DestinationContainer, DestinationBlob, Status, BytesCopied, TotalBytes, StartTime, EndTime | Format-Table -AutoSize

    # Delete used snapshots
    foreach ($copyblobdetail in $copyblobdetails)
    {
        # Create source storage account context
        $source_context = New-AzureStorageContext -StorageAccountName $copyblobdetail.SourceSA -StorageAccountKey $copyblobdetail.SourceKey -Environment $copyblobdetail.SourceEnvironment

        $source_container = Get-AzureStorageContainer -Context $source_context -Name $copyblobdetail.SourceContainer
        $blobs = $source_container.CloudBlobContainer.ListBlobs($copyblobdetail.SourceBlob, $true, "Snapshots") | Where-Object { $_.SnapshotTime -ne $null -and $_.SnapshotTime.DateTime.ToString() -EQ $copyblobdetail.SnapshotTime }
        $blobs[0].Delete()
    }
}
