ifneq (,$(wildcard ./.env))
    include .env
    export
endif

ISCC="C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
SCRIPT=AutoInstaller/installer.iss
FIRST_TIME_SCRIPT=AutoInstaller/first_time_installer.iss

MODFILES=AutoInstaller\ModFiles
FIRST_TIME_MODFILES=AutoInstaller\FirstTime
BUNDLE=IssaPluginBundle
DLL=bin\Debug\netstandard2.1\IssaPlugin.dll
PDB=bin\Debug\netstandard2.1\IssaPlugin.pdb
RELEASE_DIR=release-staging
VERSION=$(shell powershell -NoProfile -Command "(Select-String -Path IssaPlugin.csproj -Pattern '<Version>([^<]+)</Version>').Matches[0].Groups[1].Value")
BUMP?=patch

all: build build-autoinstaller

build-autoinstaller: stage-files
	$(ISCC) $(SCRIPT)

build-first-time-autoinstaller: stage-first-time-files
	$(ISCC) $(SCRIPT)

build: build-issamod

build-issamod:
	dotnet build

stage-files: clean-modfiles build-issamod
	@echo Staging mod files...

	if not exist $(MODFILES) mkdir $(MODFILES)

	xcopy /E /I /Y $(BUNDLE) $(MODFILES)\IssaPluginBundle
	copy /Y $(DLL) $(MODFILES)

stage-first-time-files: build-issamod
	@echo Staging mod files...

	if not exist $(MODFILES) mkdir $(MODFILES)

	xcopy /E /I /Y $(BUNDLE) $(MODFILES)\IssaPluginBundle
	copy /Y $(DLL) $(MODFILES)

clean-modfiles:
	if exist $(MODFILES) rmdir /S /Q $(MODFILES)

bump-version:
	powershell -NoProfile -ExecutionPolicy Bypass -File bump_version.ps1 -Bump $(BUMP)

release: bump-version build
	$(eval VERSION := $(shell powershell -NoProfile -Command "(Select-String -Path IssaPlugin.csproj -Pattern '<Version>([^<]+)</Version>').Matches[0].Groups[1].Value"))
	@echo Signing DLL...
	powershell -NoProfile -ExecutionPolicy Bypass -File sign_release.ps1 -DllPath $(DLL)
	@echo Creating release v$(VERSION)...
	if exist $(RELEASE_DIR) rmdir /S /Q $(RELEASE_DIR)
	mkdir $(RELEASE_DIR)
	xcopy /E /I /Y $(BUNDLE) $(RELEASE_DIR)\IssaPluginBundle
	copy /Y $(DLL) $(RELEASE_DIR)
	copy /Y $(PDB) $(RELEASE_DIR)
	copy /Y $(DLL).sig $(RELEASE_DIR)\IssaPlugin.dll.sig
	powershell -NoProfile -Command "Compress-Archive -Path '$(RELEASE_DIR)\*' -DestinationPath 'IssaMod-v$(VERSION).zip' -Force"
	gh release create v$(VERSION) IssaMod-v$(VERSION).zip $(RELEASE_DIR)\IssaPlugin.dll $(RELEASE_DIR)\IssaPlugin.dll.sig --title "v$(VERSION)" --generate-notes
	if exist $(RELEASE_DIR) rmdir /S /Q $(RELEASE_DIR)

install: build
	@echo Installing mod to $(MOD_DIR)...
	xcopy /E /I /Y $(BUNDLE) $(MOD_DIR)\IssaPluginBundle
	copy /Y $(DLL) $(MOD_DIR)

clean:
	if exist Output rmdir /S /Q Output
	if exist $(MODFILES) rmdir /S /Q $(MODFILES)
	if exist $(RELEASE_DIR) rmdir /S /Q $(RELEASE_DIR)
	del /Q IssaMod-v*.zip 2>nul || true