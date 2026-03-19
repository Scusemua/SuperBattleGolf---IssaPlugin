[Setup]
AppName=Super Battle Golf Mod
AppVersion=1.0
DefaultDirName={code:GetGamePath}
DisableDirPage=no
OutputDir=Output
OutputBaseFilename=SuperBattleGolfModInstaller

[Files]
Source: "ModFiles\*"; DestDir: "{app}"; Flags: recursesubdirs ignoreversion

[Code]

function PosEx(SubStr, S: string; Offset: Integer): Integer;
var
  I: Integer;
begin
  Result := 0;

  for I := Offset to Length(S) - Length(SubStr) + 1 do
  begin
    if Copy(S, I, Length(SubStr)) = SubStr then
    begin
      Result := I;
      exit;
    end;
  end;
end;

function GetSteamPath(): string;
var
  SteamPath: string;
begin
  // 64-bit registry
  if RegQueryStringValue(HKLM, 'SOFTWARE\WOW6432Node\Valve\Steam', 'InstallPath', SteamPath) then
  begin
    Result := SteamPath;
    exit;
  end;

  // 32-bit registry
  if RegQueryStringValue(HKLM, 'SOFTWARE\Valve\Steam', 'InstallPath', SteamPath) then
  begin
    Result := SteamPath;
    exit;
  end;

  // Per-user install
  if RegQueryStringValue(HKCU, 'SOFTWARE\Valve\Steam', 'InstallPath', SteamPath) then
  begin
    Result := SteamPath;
    exit;
  end;

  Result := '';
end;

function ExtractSecondQuotedValue(Line: string): string;
var
  P1, P2, P3, P4: Integer;
begin
  Result := '';
  P1 := Pos('"', Line);
  if P1 > 0 then
  begin
    P2 := PosEx('"', Line, P1 + 1);
    if P2 > 0 then
    begin
      P3 := PosEx('"', Line, P2 + 1);
      if P3 > 0 then
      begin
        P4 := PosEx('"', Line, P3 + 1);
        if P4 > P3 then
          Result := Copy(Line, P3 + 1, P4 - P3 - 1);
      end;
    end;
  end;
end;

function ExtractLastQuotedValue(Line: string): string;
var
  I, StartPos, EndPos: Integer;
begin
  Result := '';
  EndPos := 0;

  // Find last quote
  for I := Length(Line) downto 1 do
  begin
    if Line[I] = '"' then
    begin
      EndPos := I;
      Break;
    end;
  end;

  if EndPos = 0 then exit;

  // Find previous quote
  for I := EndPos - 1 downto 1 do
  begin
    if Line[I] = '"' then
    begin
      StartPos := I;
      Result := Copy(Line, StartPos + 1, EndPos - StartPos - 1);
      exit;
    end;
  end;
end;

procedure AddLibraryPath(var Paths: TArrayOfString; Path: string);
var
  Len: Integer;
begin
  if Path = '' then exit;

  Len := GetArrayLength(Paths);
  SetArrayLength(Paths, Len + 1);
  Paths[Len] := Path;
end;

function GetSteamLibraryPaths(): TArrayOfString;
var
  SteamPath: string;
  VDFPath: string;
  Lines: TArrayOfString;
  I: Integer;
  Line, Value: string;
  Paths: TArrayOfString;
begin
  SetArrayLength(Paths, 0);

  SteamPath := GetSteamPath();
  if SteamPath = '' then
  begin
    Result := Paths;
    exit;
  end;

  // Always include main Steam path
  AddLibraryPath(Paths, SteamPath);

  VDFPath := AddBackslash(SteamPath) + 'steamapps\libraryfolders.vdf';

  if not FileExists(VDFPath) then
  begin
    Result := Paths;
    exit;
  end;

  if not LoadStringsFromFile(VDFPath, Lines) then
  begin
    Result := Paths;
    exit;
  end;

  for I := 0 to GetArrayLength(Lines) - 1 do
  begin
    Line := Trim(Lines[I]);

    // Old format: "1" "D:\\SteamLibrary"
    if (Pos('"', Line) > 0) and (Pos(':\', Line) > 0) then
    begin
      Value := ExtractLastQuotedValue(Line);
      if Value <> '' then
        StringChangeEx(Value, '\\', '\', True);
        AddLibraryPath(Paths, Value);
    end;

    // New format: "path" "D:\\SteamLibrary"
    if Pos('"path"', Lowercase(Line)) > 0 then
    begin
      Value := ExtractLastQuotedValue(Line);
      if Value <> '' then
        StringChangeEx(Value, '\\', '\', True);
        AddLibraryPath(Paths, Value);
    end;
  end;

  Result := Paths;
end;

function FindGameInLibraries(): string;
var
  Paths: TArrayOfString;
  I: Integer;
  Candidate: string;
begin
  Result := '';
  Paths := GetSteamLibraryPaths();
  for I := 0 to GetArrayLength(Paths) - 1 do
  begin
    Candidate := AddBackslash(Paths[I]) + 'steamapps\common\Super Battle Golf';

    if DirExists(Candidate) then
    begin
      Result := Candidate;
      exit;
    end;
  end;
end;

function GetGamePath(Param: string): string;
var
  FoundPath: string;
begin
  FoundPath := FindGameInLibraries();

  if FoundPath <> '' then
  begin
    Result := FoundPath;
    exit;
  end;

  // Fallback
  Result := ExpandConstant('{pf32}\Steam\steamapps\common\Super Battle Golf');
end;

function IsValidGameDir(Path: string): boolean;
begin
  Result :=
    FileExists(AddBackslash(Path) + 'Super Battle Golf.exe');
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;

  if CurPageID = wpSelectDir then
  begin
    if not IsValidGameDir(WizardDirValue) then
    begin
      if MsgBox(
        'This folder may not contain Super Battle Golf. Continue anyway? ' + WizardDirValue,
        mbConfirmation, MB_YESNO) = IDNO then
      begin
        Result := False;
      end;
    end;
  end;
end;