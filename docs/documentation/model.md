---
title: Model
nav_order: 1
parent: Documentation
---

# The PGO model
This text describes the configuration optimization problem and the models relied upon to solve it. For an overview of how to set up such a model in code, please see the [main PGO documentation page](index.md).

## The optimization problem
An important problem a distribution network operator needs to solve is how to configure and operate the network. The Power Grid Optimizer aims to assist the operator in this respect, in particular with respect to the switch settings. A full set of switch settings and other choices (like transformer operation modes) will be referred to as a grid configuration. 

A time step (or periodized) sequence of such configurations is referred to as a configuration schedule, or solution. The core problem in this manual is the Multiperiod Distribution grid configuration Problem, or MDGCP:

*For a given power network and a periodized demand forecast, what is the optimal configuration schedule?*

An optimal configuration schedule must satisfy certain conditions. Between periods, the required changes to the configuration must be doable in terms of resources and time, and for any particular period the configuration must provide the required power in a feasible manner. The configuration must also satisfy constraints related to the resulting power flow (see below).

The present version of the software is limited to _radial_ grid configurations, with radiality understood to mean that every load must draw its power from exactly one source, and there can only be one path through the network from a given source to a given load.

In addition, the model supports the following constraints:

 * Voltage limits for consumers, lines, and transformers.
 * Current limits for lines.
 * Power capacities for substations.

There are several ways in which one might measure solution quality. The software supports (weighted combinations of) the following quality measures:

 * Expected cost of energy not delivered (called KILE in Norway).
 * Switching cost (between periods and provided initial/final configurations).
 * Total power loss.
 * Avoiding the current limit (i.e. one might want to operate at the most at 80% of the current limit for any given line).

To set up an MDGCP, the user must provide at minimum the following two things:

 * A description of the power network.
 * A demand forecast.

If the problem to be solved is a static one (i.e. a _default configuration_; “normaldele” in Norway) one sets up a forecast with only a single period, presumably based on aggregated/averaged data, and runs the model for this single period.

In addition, a start and end configuration may be provided, describing the state the network will be configured in just prior to and just after the time period for which the optimization is run. In the next section we describe the content of these models.

## Model concepts
In this section we will describe the model concepts more generally, leaving the technical details to the API documentation. In the [modelling examples](./modelling_examples.md) we show some simple illustrations of how to express a problem on the PGO JSON format.

### The power grid

The PGO software for grid configuration does not need to consider every aspect of a power network to provide grid configurations. Therefore, a model that is simplified “just enough” is used, representing the power network as a graph, most of whose edges correspond to real-world AC power lines[^1] and whose nodes come in three kinds:

 * *Provider*: Node that provides power. How much power is drawn in a provider node is computed based on the provided demands and the grid configuration.
 * *Consumer*: Node that consumes power, of some kind. Consumers have power demands, and the demand is given separately to the rest of the data as a time series.
 * *Transition*: Nodes that are just connection points in the network. Here net injected power is assumed to equal net ejected power.

**Switches** are assumed to have the practical effect of removing an edge from the network, and so they are associated to edges. Edges with one (or more) associated switches are called switchable. An edge also has information required for computing KILE costs. The relevant information for this is whether the edge is a breaker, its fault frequency, sectioning time and repair time.

In addition, the model has a simplified concept of a **transformer**. A transformer connects 2 or 3 nodes in the grid, and comes with a specification of how voltages may be transformed in the various connections. Any switching in the transformer must be modeled with switchable lines exterior to the transformer in this model, while losses are modeled using a power factor. Transformers have two modes of operation:

 * *Fixed*: The transformer converts voltage with a fixed ratio.
 * *Automatic*: The transformer will always produce a fixed downstream voltage.

#### Transformers, providers and consumers

The description of “provider” and “consumer” nodes above is purposefully left vague. In the distribution network, both of these may be transformers – typically the provider nodes represent transformers that connect the high-voltage regional grid with the distribution grid. Often, it is also efficient to let an “aggregated” consumer node represent an aggregate of all consumers in a local low-voltage grid. That is, rather than including the low-voltage grid (and its input transformer) to the model, one can put an “aggregate” consumer in its place.

The key point is that the internal workings of these provider and consumer nodes as transformers are not very relevant to the planning task at hand – the upstream transformers act only as sources of power with a particular voltage, while the downstream ones can represent aggregate demand in an area of the network. As long as we do not need a more detailed model, these simple provider/consumer concepts can be used.

However, some transformers are intermediate transformers in the distribution grid, or three-winding transformers that in practice allow a consumer to receive at two different voltage levels, or similar. For these situations, in which the specific working of the transformer is relevant, the actual transformer concept must be used.

#### Reliability calculations for aggregate consumers

Some grid reliability measures, such as the “cost of energy not delivered”, are computed based on the “type” (such as “agriculture” or “household”) of each consumer. Where a consumer in the model represents an aggregate of actual consumers as discussed above, and these consumers are of different types, it is possible to define a fraction of the total (aggregated) demand that comes from each of these types. See the API documentation for details.

### Demand forecasts

Compared to the power grid the forecast is simpler to describe. It has two components:

 * A list of periods, which are named intervals in time. These are presumed to be consecutive.
 * For every consumer, the forecasted power demand in each period. Note that it is possible to omit active and/or reactive power demand for a consumer, but if a demand is given, it must be given for each period.

## Further reading

For more information, see also the list of [modelling examples](modelling.md), as well as the [API reference](https://pgosintef.azurewebsites.net/swagger/index.html) (requires login). A full overview of PGO documentation can be found [here](./index.md).


[^1]: One or several – if two or more are connected in series with no branching an equivalent representing line may be used internally.

