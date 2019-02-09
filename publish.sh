#!/usr/bin/env bash
set -u

readonly VERSION="1.0"
if [[ "$(uname)" == 'Darwin' ]]; then
  readonly SCRIPT_DIR_PATH=$(dirname $(greadlink -f $0))
else
  readonly SCRIPT_DIR_PATH=$(dirname $(readlink -f $0))
fi

cd $SCRIPT_DIR_PATH/LNTestFramework

dotnet pack -c Release --include-symbols -p:SymbolPackageFormat=snupkg
dotnet nuget push bin/Release/LNTestFramework.1.0.12.nupkg -k ${NUGET_API_KEY_LNTESTFRAMEWORK} -s https://api.nuget.org/v3/index.json
