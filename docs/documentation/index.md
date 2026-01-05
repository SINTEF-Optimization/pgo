---
title: Documentation
nav_order: 3
has_toc: false ## explicit list is part of the text
---
# Overview of PGO documentation

Below you will find an overview of the various sources of information that will help you to use the PGO service, and to integrate it into your own DSO systems solution. 

In general, introductionary material, tutorials, and API documentation may be found here at the [PGO web site](https://pgo.sintef.no). More technical information related to PGO development, the open-source code, and how to use it, may be found at [github](https://github.com/SINTEF-Optimization/pgo).

## High level documentation
The high level PGO documentation is organized as follows:

* An "at a glance" overview is given at the [PGO home page](../index.md)
* A more detailed introduction to the PGO, what it can do, and how it can be used, is given in the [Introduction to the PGO](../background.md).
* More detailed information is provided about the [conceptual model](./model.md), along with some simple how-to [modelling examples](./modelling_examples.md).
* A [simple web application](https://pgosintef.azurewebsites.net/#/) is available to give some idea about how the PGO can be used. The functionality of this is explained in the [web application user manual](./web_app_user_manual.md).

## Technical documentation
The PGO can be accessed through two types of API: a .NET API for in-process integration, 
and a HTTP-based API for hosting the PGO as a web service. Both APIs give access to the same functionality for setting up one or several grid configuration problems, running the PGO algorithms, monitor the search progress, and retrieving the resulting grid configurations.

 The following API documentation is given for the HTTP API: 
 
* [Getting started using the API](api_getting_started.md)
* [Full API reference](https://pgosintef.azurewebsites.net/swagger/index.html)

The corresponding documentation for the .NET API is given in the code, see the [Readme file on github](https://github.com/SINTEF-Optimization/pgo/blob/main/README.md).

Finally, [the PGO github repository](https://github.com/SINTEF-Optimization/pgo) gives you access to the open source code, as well as more technical information about how to use it. If you do not wish to compile the code, the Readme in the same repository describes how you can download the PGO Docker image, instead.

