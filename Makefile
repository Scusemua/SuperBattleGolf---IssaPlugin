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

build:
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
	powershell -NoProfile -Command "\
		$$f = 'IssaPlugin.csproj'; \
		$$c = Get-Content $$f -Raw; \
		$$v = [regex]::Match($$c, '<Version>([^<]+)</Version>').Groups[1].Value; \
		$$p = $$v -split '\.'; \
		if ('$(BUMP)' -eq 'major') { $$p[0] = [int]$$p[0]+1; $$p[1]=0; $$p[2]=0 } \
		elseif ('$(BUMP)' -eq 'minor') { $$p[1] = [int]$$p[1]+1; $$p[2]=0 } \
		else { $$p[2] = [int]$$p[2]+1 }; \
		$$n = $$p -join '.'; \
		$$c = $$c -replace '<Version>[^<]+</Version>', \"<Version>$$n</Version>\"; \
		Set-Content $$f $$c; \
		$$pi = 'PluginInfo.cs'; \
		$$pic = Get-Content $$pi -Raw; \
		$$pic = $$pic -replace 'PLUGIN_VERSION = \"[^\"]+\"', \"PLUGIN_VERSION = \`"$$n\`"\"; \
		Set-Content $$pi $$pic; \
		Write-Host \"Bumped version $$v -> $$n\""

release: bump-version build
	$(eval VERSION := $(shell powershell -NoProfile -Command "(Select-String -Path IssaPlugin.csproj -Pattern '<Version>([^<]+)</Version>').Matches[0].Groups[1].Value"))
	@echo Creating release v$(VERSION)...
	if exist $(RELEASE_DIR) rmdir /S /Q $(RELEASE_DIR)
	mkdir $(RELEASE_DIR)
	xcopy /E /I /Y $(BUNDLE) $(RELEASE_DIR)\IssaPluginBundle
	copy /Y $(DLL) $(RELEASE_DIR)
	copy /Y $(PDB) $(RELEASE_DIR)
	powershell -NoProfile -Command "Compress-Archive -Path '$(RELEASE_DIR)\*' -DestinationPath 'IssaMod-v$(VERSION).zip' -Force"
	gh release create v$(VERSION) IssaMod-v$(VERSION).zip --title "v$(VERSION)" --generate-notes
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