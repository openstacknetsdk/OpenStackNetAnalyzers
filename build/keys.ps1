# Note: these values may only change during major release

If ($Version.Contains('-')) {

	# Use the development keys
	$Keys = @{
		'portable-net45' = '2c3f425faba47628'
	}

} Else {

	# Use the final release keys
	$Keys = @{
		'portable-net45' = '2bddc237b520a058'
	}

}

function Resolve-FullPath() {
	param([string]$Path)
	[System.IO.Path]::GetFullPath((Join-Path (pwd) $Path))
}
