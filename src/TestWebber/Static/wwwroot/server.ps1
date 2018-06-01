Param($WebRequest) 

$requestUrl = $WebRequest.Url.AbsolutePath 

$outputContent = "how silvia $requestUrl"


# don't touch this!

return @{
	'StatusCode' = 200
	'Headers' = @{
		'Powered-By' = 'Powershell'
	}
	'MimeType' = 'text/html'
	'Stream' = [System.IO.MemoryStream]::new([System.Text.Encoding]::UTF8.GetBytes($outputContent))
}
