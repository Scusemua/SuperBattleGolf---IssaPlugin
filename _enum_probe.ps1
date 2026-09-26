$asm = [Reflection.Assembly]::LoadFrom('G:\Documents\SuperBattleGolfPlugin\Assembly\GameAssembly.dll')
$mv = $asm.GetType('PlayerMovement')
$flags = [Reflection.BindingFlags]'Instance,Static,Public,NonPublic,DeclaredOnly'
foreach ($p in $mv.GetProperties($flags)) {
  if ($p.Name -match 'Walk|Speed') { Write-Output ("PROP " + $p.PropertyType.Name + " " + $p.Name) }
}
$m = $null
foreach ($cand in $mv.GetMethods($flags)) {
  if ($cand.Name -eq 'get_WalkSpeedFactor') { $m = $cand; break }
}
Write-Output ("getter IL " + $m.GetMethodBody().GetILAsByteArray().Length)
$module = $m.Module
$body = $m.GetMethodBody().GetILAsByteArray()
$i = 0
while ($i -lt $body.Length) {
  $op = $body[$i]
  if ($op -eq 0x7B -or $op -eq 0x28 -or $op -eq 0x6F) {
    $token = [BitConverter]::ToInt32($body, $i+1)
    try {
      if ($op -eq 0x7B) { $f = $module.ResolveField($token); Write-Output ("field " + $f.FieldType.Name + " " + $f.Name) }
      else { $c = $module.ResolveMethod($token); Write-Output ("call " + $c.DeclaringType.Name + "::" + $c.Name) }
    } catch {}
    $i += 5; continue
  }
  $i += 1
}
