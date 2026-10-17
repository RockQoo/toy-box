param($Root)
Get-ChildItem $Root -Recurse -Filter *.ogg | ForEach-Object {
  $b=[IO.File]::ReadAllBytes($_.FullName)
  $vi=[Text.Encoding]::ASCII.GetString($b,0,[Math]::Min(200,$b.Length)).IndexOf("vorbis")
  $rate=[BitConverter]::ToUInt32($b,$vi+11)
  $last=-1; for($i=$b.Length-27;$i -ge 0;$i--){ if($b[$i] -eq 0x4F -and $b[$i+1] -eq 0x67 -and $b[$i+2] -eq 0x67 -and $b[$i+3] -eq 0x53){$last=$i;break} }
  $gp=[BitConverter]::ToInt64($b,$last+6)
  $rel=$_.FullName.Substring($Root.Length+1).Replace([char]92,[char]47)
  "{0}`t{1:N2}" -f $rel, ($gp/$rate)
}
