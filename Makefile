ISCC="C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
SCRIPT=AutoInstaller/installer.iss

MODFILES=AutoInstaller\ModFiles
BUNDLE=Bundle
DLL=bin\Debug\netstandard2.1\IssaPlugin.dll

all: build-issamod build-autoinstaller

build-autoinstaller: # stage-files
	$(ISCC) $(SCRIPT)

build-issamod:
	dotnet build

stage-files: clean-modfiles build-issamod
	@echo Staging mod files...

	if not exist $(MODFILES) mkdir $(MODFILES)

	xcopy /E /I /Y $(BUNDLE) $(MODFILES)
	copy /Y $(DLL) $(MODFILES)

clean-modfiles:
	if exist $(MODFILES) rmdir /S /Q $(MODFILES)

clean:
	if exist Output rmdir /S /Q Output
	if exist $(MODFILES) rmdir /S /Q $(MODFILES)