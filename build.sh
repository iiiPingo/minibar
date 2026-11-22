#!/bin/sh
# Builds Minibar.exe, Minibar-Setup-<version>.exe and the release zip into ./release
# (Linux: needs dotnet-sdk-8.0 + mono-devel; on Windows run the two dotnet builds by hand).
set -e
cd "$(dirname "$0")"
V=$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' src/Minibar.csproj)
dotnet build src -c Release -nologo -v q
dotnet build setup -c Release -nologo -v q -p:Version="$V"
rm -rf release && mkdir -p release/Minibar
cp src/bin/Release/net48/Minibar.exe src/bin/Release/net48/Minibar.exe.config docs/README.txt release/Minibar/
cp setup/bin/Release/net48/Minibar-Setup.exe "release/Minibar-Setup-$V.exe"
cp src/bin/Release/net48/Minibar.exe release/Minibar.exe
(cd release && rm -f "Minibar-$V.zip" && zip -qr "Minibar-$V.zip" Minibar)
ls -l release
