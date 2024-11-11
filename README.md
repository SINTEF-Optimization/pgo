# Power Grid Optimizer

Power Grid Optimizer (PGO) is a software for power grid topology optimization. That is, it can be used to configure the network by choosing the open/closed settings for a set of switches.

## License

PGO is licensed under the GNU Lesser General Public License (LGPL). See the file [LICENCE](LICENSE).

## Getting started

PGO can be used either in-process though a .NET API, or as a standalone server with a HTTP REST API.

### .NET API

- In a .NET project, reference the nuget package `Sintef.Pgo.Api.Factory`.
- Create the PGO server object by calling `ServerFactory.CreateServer()`.
- Use the server object to manage networks and create session objects.

### HTTP API

- Start the PGO server
    - **Docker**: Pull the docker image `sintef/pgo`. Then start it with `docker run -p5000:80 -p5001:443 sintef/pgo`.
    - **Application**: Using Visual Studio, build and start the project `Sintef.Pgo.REST` using launch profile `PgoREST`.
- Navigate to `http://localhost:5000` or `https://localhost:5001` to see the server's main page.
  The service endpoints are found under the `/api/` path (or `/api/cim/` when using CIM data).

### Data formats

PGO supports two different formats for input/output of power grids, power demands, switch settings etc.

- PGO's internal json format. This format has been developed specifically for PGO and contains only the entities and
  properties that PGO supports. For documentation, start at the main type `PowerGrid`, in the `Sintef.Pgo.DataContracts` project.
  The same documentation is found in the Schemas section of the online API reference.
  
- CIM with JsonLd. CIM (Common Information Model) is an industry standard for representing power networks and
  associated data. It is a UML model, defining entities, attribute and relations. JsonLd is a standard for representing
  such entities in Json.
  For documentation, start at the types `CimJsonLdNetworkData` and `CimNetwork`. The former describes the concrete
  data format (which includes JsonLd), while the latter describes the entites that PGO uses at the CIM model level.

### Example programs

The folder `Sintef.Pgo/Examples` contains example programs showing different ways to interface PGO.

- `PgoNetApiDemo`: Uses the .NET API and an in-process server.
- `PgoRestApiDemo`: Uses the HTTP REST API against an external server.
- `PgoRestClientDemo`: Also uses the HTTP REST API, but through the helper class `PgoRestClient` rather than
  handling the details of http directly.
- `PgoRestClientCimDemo`: Similar to `PgoRestClientDemo`, but exchanges data in CIM format. 
  The other three examples use the internal json format.

## Resources

See the [PGO web page](https://pgo.sintef.no/) for further documentation, including an API reference for the HTTP interface.
Here, you can also use the API manually from a web browser.

Running the server locally will also give you this API reference, under the URL path `/swagger`. 

## Code organization

The projects in the PGO solution are as follows:

 - **Core**. 
   - `Sintef.Pgo.DataContracts`: Data structures for the APIs.
   - `Sintef.Pgo.Core`: PGO's internal network model and optimizer. The optimizer is built on SINTEF's Scoop library.
   - `Sintef.Pgo.Server`: Common server/session functionality used by both the REST API and the .NET API.
 - **REST API**. 
   - `Sintef.Pgo.REST` implements the REST API based on ASP.NET Core.
   - `Sintef.Pgo.Doc` contains documentation that is built by DocFX and copied into `Sintef.Pgo.REST` as static web pages.
   - `Sintef.Pgo.RestClient` contains `PgoRestClient`, which simplifies using the REST API from test code and demo applications.
 - **.NET API**. 
   - `Sintef.Pgo.Api` defines the interfaces `IServer` and `ISession`.
   - `Sintef.Pgo.Api.Impl` has the implementations `Server` and `Session`, based on `Sintef.Pgo.Server`.
   - `Sintef.Pgo.Api.Factory` contains the factory method for creating `IServer`s.
 - **Tests**.
   - A test project for each of the above folders.
 - **Examples**.
   - The example programs mentioned above.
  
## Code development

This section contains routines for the various processes involved in code development, quality control and release publishing.
Parts are relevant mainly to developers within SINTEF.

### Source control

The code is under source control under GIT. The primary repo is hosted internally on SINTEF's GitLab.
There is also a mirror on GitHub.

#### Branch name convention

- `main`: This branch contains code that is complete, has passed review and will be part of the next release.
- `release/x.y`: Branch used for preaparing the release of version x.y, and all subsequent x.y.z patch versions.
- `feature/issueId`: Branch used to develop code for a new feature (identified by the corresponding GitLab issue ID).
- `bug/issueId`: Branch used to investigate, identify and fix bugs related to an issue (identified by GitLab issue ID).
- `research/mytopic`: Branch used for larger, more long term, research tasks.

#### Tags

Commits used to produce a release, are tagged `Release_x.y.z`. (See Release routines, below.)

### Version numbers
Release version numbers follow the "semantic versioning" system, where a version is labeled by three numbers, "`x.y.z`":

- `x` is the major version counter. New major versions can contain breaking changes.
- `y` is the minor version counter. New minor versions can contain new functionality or features, but cannot contain breaking changes.
- `z` is the patch counter. New patches contains bug fixes, but do not change the intended functionality of the code.

Breaking changes are defined with respect to the public REST and .NET APIs and the data structures in `Sintef.Pgo.DataContracts`. 
For the REST API, compatibility at the http protocol level is relevant, while for the .NET API, we must consider
binary compatibility for the `Sintef.Pgo.Api` and `Sintef.Pgo.Api.Factory` assemblies.

### Quality assurance

Merging to the `main` branch is allowed only through merge requests. Each merge request must have at least one reviewer, who is responsible for:

- Reviewing the code changes.
- Checking that all functionality is covered by tests, and that the tests pass.
- Checking that there are tests that cover erroneous use of the APIs and that such use is reported with constructive error messages.
- Checking that the Changelog has been updated if the change is relevant for clients.

### Release routines

The procedure for making a new release (x.y.z) is as follows:

#### Preparing the release branch

1. Check that all feature/bugfix branches that should be included in the release have been merged to `main`.
2. Select a commit on which the release will be based. This is normally the latest commit on the `main` branch. However, it is possible
  to select an earlier commit in order to exclude recently added functionality.
3. Review the changes since the last release to determine which of the major or minor version number should be incremented.
4. Create the release branch `release/x.y` from the selected commit.
5. Review the functionality that is present on the relase branch. You may change what's included in the release by cherry picking
  from other branches (e.g. bugfixes), or exclude things by using `git revert`.
6. Update `Changelog.txt` (in OpfREST/wwwroot). Make sure that all changes since the last version are recorded. Then
  add a new line to the top with the version number of the new release.
  **Changelog format**:
  *The most recent version is on top. Each version has a heading 'Version x.y.z', and
  the following lines describe the relevant changes. You may add change lines above
  the latest version to document changes before a new version is made.*
7. Update the version number in the projects that produce nuget packages:
  - `Sintef.Pgo.Api`
  - `Sintef.Pgo.Api.Factory`
  - `Sintef.Pgo.Api.Impl`
  - `Sintef.Pgo.Core`
  - `Sintef.Pgo.DataContracts`
  - `Sintef.Pgo.Server`
8. Make sure all tests pass.
9. Add a git tag with name `Release_x.y.z`.
10. Push to GitLab and GitHub.

#### Publishing the release

The release is published by jobs in the `publish` stage of the GitLab pipeline. These jobs must be started manually and will only run
successfully from a commit with the git tag `Release_x.y.z`, where the version in the tag matches the version in `Changelog.txt`.

 - **publish-docker-release-job**.
   Builds the Docker image `sintef/pgo` with the REST API server and web frontend and pushes it to Docker Hub.
   Tags the image with `x.y.z` and `latest`. From there, it can be pulled by users or deployed to the PGO web site.
 - **publish-nuget-release-job**.
   Builds the nuget packages for the .NET API and pushes them to `nuget.org`. From there, they can be pulled by users.
   The job fails if the version numbers of the nuget packages do not match the git tag version.

#### Release completion

Consider if anything done on the release branch should be merged back to `main`. Normally, you want to merge back 
the updates to `Changelog.txt` and project file version numbers. Also, there might be bugfixes.
Use a merge request as normal.

#### Patch level releases

Further bugfix releases for the same major/minor version are performed on the same branch. So e.g. version 2.2.1 is made
from the 2.2 branch. Changes from one version to the next should only consist of bugfixes, which either have been fixed 
on `main` and are cherry picked to the release branch, or are fixed in the branch and then merged back to `main` if
relevant. Apart from that you don't need to make a new branch, the procedure is the same as above.

#### Publishing an unofficial web site version

There is a third job in the `publish` stage, called **publish-docker-commit-job**. This job is similar to **publish-docker-release-job** in that
it builds the Docker image and pushes to Docker Hub. However, it can be run from any commit, and it tags the image with the
commit hash instead of a release version. This allows us to build and test deploy to the web site without creating official releases.
