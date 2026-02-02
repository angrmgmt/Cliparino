$headers = @{
    "Authorization" = "Bearer blbxga0mw7occjqs5obyqb5s1bm5gw"
    "Client-ID" = "dnafsrivhw88gj7eltolrsq6794teq"
}

 $response = Invoke-WebRequest -Uri "https://api.twitch.tv/helix/clips?broadcaster_id=93177556" -Headers $headers
 $clipData = ($response.Content | ConvertFrom-Json).data
 $clipData