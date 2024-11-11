#!/bin/bash

# This script builds and publishes the sintef/pgo Docker image.
# It is run by a manually triggered GitLab job in the publish stage.
# It is not expected to work outside SINTEF's GitLab environment.

changelog='Sintef.Pgo/REST API/Sintef.Pgo.REST/wwwroot/Changelog.txt'
git_tag_base="Release_"
docker_image="sintef/pgo"


if [ ! "$1" == "release-image" -a ! "$1" == "commit-image" ]; then
	echo "Please give one argument:"
	echo "  \"release-image\": Build a release image (with Docker tag e.g. 1.2.3). "
	echo "                   Requires a matching git tag at the current commit."
	echo "   \"commit-image\": Build an image tagged with the current commit hash."
	exit 1
fi

command=$1


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

# DOCKER_REGISTRY_SERVER_USERNAME=
# DOCKER_REGISTRY_SERVER_PASSWORD=
# CI_COMMIT_SHORT_SHA=

# Check necessary variables are present
if [ "${DOCKER_REGISTRY_SERVER_USERNAME}" = "" ]; then
	echo -e "$Red"Missing variable DOCKER_REGISTRY_SERVER_USERNAME
	exit 1
fi
if [ "${DOCKER_REGISTRY_SERVER_PASSWORD}" = "" ]; then
	echo -e "$Red"Missing variable DOCKER_REGISTRY_SERVER_PASSWORD
	exit 1
fi
if [ "${CI_COMMIT_SHORT_SHA}" = "" ]; then
	echo -e "$Red"Missing variable CI_COMMIT_SHORT_SHA
	exit 1
fi

# Find tags for docker image
if [ "$command" == "release-image" ]; then

	# Find the latest version number from Changelog

	version=`egrep '^Version' "$changelog" | head -1 | sed -e 's/Version  *//' | sed -e 's/ .*//' | sed -e 's/\r//'`

	echo -e "$Green"The PGO version is $version "(found from $changelog)".$Reset

	required_git_tag=$git_tag_base$version
	docker_tag=$version
	latest_tag=latest

	# Verify that we're at a git tag that matches the version found in Changelog

	tag_at_commit=`git tag --points-at HEAD`

	if [ ! "$tag_at_commit" == "$required_git_tag" ]; then
		echo -e $Red
		echo "We are publishing release \"$version\" (found in $changelog)."
		echo "This requires the current commit to have tag \"$required_git_tag\","
		echo "but we found \"$tag_at_commit\"."
		echo -e $Reset
		exit 1
	fi

else  # command = commit-image

	# The docker image is tagged based on the git commit
	docker_tag=build-$CI_COMMIT_SHORT_SHA
	
	# Also, we tag it 'web_build' instead of 'latest'
	latest_tag=web_build
	
	# Add a line to the changelog, specifying the commit built from
	sed -i "1s/$/ (build $CI_COMMIT_SHORT_SHA on $(date -Iminutes))/" "$changelog"

fi

# Build Docker image
echo -e $Green
echo Building image $docker_image:$docker_tag
echo -e $Reset

docker build -t $docker_image:$docker_tag .

# Tag and push to Docker hub

echo -e $Green
echo Tagging docker image
echo -e $Reset

docker tag $docker_image:$docker_tag $docker_image:$latest_tag

echo -e $Green
echo Pushing docker image
echo -e $Reset

docker login -u $DOCKER_REGISTRY_SERVER_USERNAME -p $DOCKER_REGISTRY_SERVER_PASSWORD
docker push $docker_image:$docker_tag
docker push $docker_image:$latest_tag

echo -e $Green
echo Done
echo -e $Reset
