param($Out, [string[]]$Files)
Add-Type -A System.Drawing
$cell=150; $cols=10; $rows=[math]::Ceiling($Files.Count/$cols)
$o=New-Object Drawing.Bitmap ($cols*$cell),($rows*($cell+16)); $g=[Drawing.Graphics]::FromImage($o); $g.Clear([Drawing.Color]::FromArgb(255,150,150,160))
$f=New-Object Drawing.Font "Arial",8
for($i=0;$i -lt $Files.Count;$i++){ $im=[Drawing.Image]::FromFile($Files[$i]); $s=[math]::Min(($cell-6)/$im.Width,($cell-6)/$im.Height)
 $x=($i%$cols)*$cell; $y=[math]::Floor($i/$cols)*($cell+16); $g.DrawImage($im,$x+3,$y+3,[int]($im.Width*$s),[int]($im.Height*$s)); $g.DrawString([IO.Path]::GetFileNameWithoutExtension($Files[$i]),$f,[Drawing.Brushes]::Black,$x+2,$y+$cell); $im.Dispose() }
$o.Save($Out)
