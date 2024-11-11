#!/bin/bash

# This script builds and publishes the Sitnef.Pgo.* nuget packages.
# It is run by a manually triggered GitLab job in the publish stage.
# It is not expected to work outside SINTEF's GitLab environment.

#=========== Config section

pack_folder="nugetPack"
changelog="Sintef.Pgo/REST API/Sintef.Pgo.REST/wwwroot/Changelog.txt"  
git_tag_base="Release_"
nuget_package_folder="packages"
nuget_org_url="https://api.nuget.org/v3/index.json"

# This table contains the packages to build and publish.
# The key is package Id, the value is path to project folder

declare -A packages
packages["Sintef.Pgo.Api"]="Sintef.Pgo/.NET API/Sintef.Pgo.Api"
packages["Sintef.Pgo.Api.Factory"]="Sintef.Pgo/.NET API/Sintef.Pgo.Api.Factory"
packages["Sintef.Pgo.Api.Impl"]="Sintef.Pgo/.NET API/Sintef.Pgo.Api.Impl"
packages["Sintef.Pgo.DataContracts"]="Sintef.Pgo/Core/Sintef.Pgo.DataContracts"
packages["Sintef.Pgo.Server"]="Sintef.Pgo/Core/Sintef.Pgo.Server"
packages["Sintef.Pgo.Core"]="Sintef.Pgo/Core/Sintef.Pgo.Core"

#=========== End config section

# Verify current directory
if [[ "$PWD-" =~ "/Scripts-" ]]; then
	echo Restarting from solution root
	echo
	(cd ..; bash Scripts/publish_nuget.sh)
	exit 0
fi
if [ ! -d Scripts ]; then
	  echo Please run the script from solution root or the Scripts folder
	  exit 1
fi

# Codes for colouring output:
Red='\033[1;31m'
Green='\033[1;32m'
Reset='\033[0m'   


# GitLab supplies these variables to the build.
# If testing the script manually, you can set them here, (unless they're already in your environment):

# NUGET_PUBLISH_API_KEY=

# Check necessary variables are present
if [ "${NUGET_PUBLISH_API_KEY}" = "" ]; then
	echo -e "$Red"Missing variable NUGET_PUBLISH_API_KEY
	exit 1
fi

# Find the version we're publishing from the Changelog
version=`egrep '^Version' "$changelog" | head -1 | sed -e 's/Version  *//' | sed -e 's/ .*//' | sed -e 's/\r//'` 

echo -e "$Green"The PGO version is $version "(found from $changelog)".
echo -e Building nuget packages
echo -e $Reset

# Clean output
rm -rf $pack_folder
rm -rf $nuget_package_folder

dotnet restore --packages $nuget_package_folder

# Build&pack each project
for package in "${!packages[@]}"; do
	project_path="${packages[$package]}"
	package_file="$pack_folder/$package.$version.nupkg"

	echo dotnet pack \"$project_path\"/*.csproj -c Release -o $pack_folder --no-restore
	dotnet pack "$project_path"/*.csproj -c Release -o $pack_folder --no-restore

	if [ ! -e "$package_file" ]; then
		echo -e $Red
		echo Expected the file $package_file to be built, but cannot find it. Exiting.
		echo The following packages were built:
		echo -e $Reset
		ls $pack_folder
		exit 1
	fi
done

echo -e $Green
echo These are the nuget packages that have been built:
echo -e $Reset
ls -w 1 $pack_folder

# Verify that we're at a git tag that matches the version found in Changelog
required_git_tag=$git_tag_base$version
tag_at_commit=`git tag --points-at HEAD`

if [ ! "$tag_at_commit" == "$required_git_tag" ]; then
	echo -e $Red
	echo "We are publishing release \"$version\" (found in $changelog)."
	echo "This requires the current commit to have tag \"$required_git_tag\","
	echo "but we found \"$tag_at_commit\"."
	echo -e $Reset
	exit 1
fi

echo -e $Green
echo Packages will now be pushed.
echo -e $Reset

# Push packages
dotnet nuget push --skip-duplicate $pack_folder/*.nupkg --api-key $NUGET_PUBLISH_API_KEY --source $nuget_org_url

echo -e $Green
echo Done
echo -e $Reset
