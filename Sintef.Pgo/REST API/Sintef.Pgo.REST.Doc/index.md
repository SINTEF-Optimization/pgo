# SINTEF's Power Grid Optimizer

The Power Grid Optimizer (PGO) is a software service for computing the optimal configuration of a power grid.

That is, how to use switches and other grid components to optimise reliability, loss, balance, power quality and so on.
This grid configuration is formulated as a combinatorial optimisation problem, and solved using a suite
of suitable optimisation methods, both exact and approximate. By solving variations of this problem 
efficiently, the technology enables considerable savings on several levels in DSO/TSO operations, 
ranging from long term investment analysis, through a dynamic adaptation of the grid based on short 
term prognosis, to here-and-now re-configuration during maintenance or fault corrections.

In particular, the current version is written to compute solutions to the following question:

> Given a power network and a periodized demand forecast, what is the
> best (cheapest, most reliable, lowest effort...) configuration schedule?

> [!IMPORTANT]
> As `SINTEF PGO` is under active development, all the details presented 
> here are subject to change.

## Getting started

See the [PGO web page](https://pgo.sintef.no/) for documentation.

* The service itself lives under the `/api/` path. For instance, you can [check the server status](/api/server).
* The API reference for the PGO service interface is available [here](/swagger). 
  The link provides you with the opportunity to test the interface manually, and to see the full API documentation.
* The demo web frontend can be found [here](/index_webapp.html).

## Version history

<object width="100%" height="500" type="text/plain" data="../Changelog.txt" border="0"></object>
