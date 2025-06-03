# Power Grid Optimizer

Power Grid Optimizer (PGO) is a software for power grid topology optimization. That is, it can be used to configure the network by choosing the open/closed settings for switches in the network. The optimizer searches for a switch configuration that minimizes the cost of operating the network, of which line power loss is usually a major component.

## License

PGO is licensed under the GNU Lesser General Public License (LGPL). See the file [LICENSE](LICENSE).

## Getting started

PGO can be used either in-process through a .NET API, or as a standalone server with an HTTP REST API.

### .NET API

- In a .NET project, reference the NuGet package `Sintef.Pgo.Api.Factory`.
- Create the PGO server object by calling `ServerFactory.CreateServer()`.
- Use the server object to manage networks and create session objects.

### HTTP API

- Start the PGO server. You can do this either in Docker, or as a local application:
    - **Docker**: Pull the docker image `sintef/pgo`. Then start it with `docker run -p5000:80 -p5001:443 sintef/pgo`.    
    - **Application**: Using Visual Studio, build and start the project `Sintef.Pgo.REST` using launch profile `PgoREST`.
- Navigate to `http://localhost:5000` or `https://localhost:5001` to see the server's main page.
  The service endpoints are found under the `/api/` path (or `/api/cim/` when using CIM data).

### Data formats

PGO supports two different formats for input/output of power grids, power demands, switch settings, etc.

- PGO's internal JSON format. This format has been developed specifically for PGO and contains only the entities and
  properties that PGO supports. For documentation, start at the main type `PowerGrid`, in the `Sintef.Pgo.DataContracts` project.
  The same documentation is found in the Schemas section of the online API reference.
  
- CIM with JSON-LD. CIM (Common Information Model) is an industry standard for representing power networks and
  associated data. It is a UML model, defining entities, attributes and relations. JSON-LD is a standard for representing
  such entities in JSON.
  For documentation, start at the types `CimJsonLdNetworkData` and `CimNetwork`. The former describes the concrete
  data format (which includes JSON-LD), while the latter describes the entities that PGO uses at the CIM model level.

### Example programs

The folder `Sintef.Pgo/Examples` contains example programs showing different ways to interface PGO.

- `PgoNetApiDemo`: Uses the .NET API and an in-process server.
- `PgoRestApiDemo`: Uses the HTTP REST API against an external server.
- `PgoRestClientDemo`: Also uses the HTTP REST API, but through the helper class `PgoRestClient` rather than
  handling the details of HTTP directly.
- `PgoRestClientCimDemo`: Similar to `PgoRestClientDemo`, but exchanges data in CIM format. 
  The other three examples use the internal JSON format.

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
  
## Source control

The code is under version control with Git. The primary repo is hosted internally on SINTEF's GitLab.

The public version of the repo is a mirror on GitHub, at [https://github.com/SINTEF-Optimization/pgo].
If you wish to contribute to the software, please use the normal GitHub procedure of forking this repo and creating a pull request.

### Branch name convention

- `main`: This branch contains code that is complete, has passed review and will be part of the next release.
- `release/x.y`: Branch used for preparing the release of version x.y, and all subsequent x.y.z patch versions.
- `feature/issueId`: Branch used to develop code for a new feature (identified by the corresponding GitLab issue ID).
- `bug/issueId`: Branch used to investigate, identify and fix bugs related to an issue (identified by GitLab issue ID).
- `research/mytopic`: Branch used for larger, more long-term, research tasks.

## Routines

For further information about routines for the various processes involved in code development, quality control and release publishing,
see the file [Routines.md](Routines.md).

This is relevant mainly to developers within SINTEF.
