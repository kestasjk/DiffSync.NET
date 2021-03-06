
DiffSync.NET - A Differential Synchronization library for .NET
==============================================================


Overview
========
=> A small C# library with the core model in DiffSync.NET, providing a 
datastructure which will handle synchronization of objects between systems,
supporting client/server and also peer/perr (at reduced efficiency).

Easy to extend to use your own Diff/Patch/Merge algorithms and datasets, 
but with defaults to start with.

In a nutshell; whether you use this as a small wrapper around an unreliable 
server oe use it as the basis of your sync system, the system should be 
useful. 
Unsuitable use cases and technical notices are listed below, but it will
suit scenarious like the following:
- With multi-users working on the same datasets in real-time; 
- an unreliable connection; 
- the requirement that the user never have to wait for the network; 
- best-effort merging but no data-loss and rejected/conflict data-logging.
- .NET , basic managed system with few dependencies, easy to extend, 
	permissive LGPL licence allowing commercial use.


=> A demo / test / example app showing the system visually via WPF available 
in DiffSyncDemo.

Based on Neil Fraser's 2009 paper describing the system. Watch the YouTube
video and read the page while experimenting with the demo app to gain a 
quick understanding.



Target Audience / Applications
==============================

==> Drop in real-time synchronization tool, which is robust and simple, and
allows multi-user simultaneous editing with custom diff/merge/ and conflict
resolution.
System tolerates multiple failures such as dropped packets, long delayed 
responses, obsolete messages reappearing.

==> A learning tool / reference implementation to understand the exact 
details of the system. The design attempts to match the presented layout,
and the DiffSyncDemo app lays out the client / server datasets as per
the video presentation, allowing easy comparison.



Limitations / Warnings
======================
The system guards against multiple packets being sent at the same time, 
but this requires 

This is a simple client / server and basic peer to peer synchornization system
which is a very close implemtation of the data structure 

Not yet been used in a production environment, getting its first full use in 
early 2020.

Does not include serialization code, disk backup, etc; only the required
structures, and extensions for Web API, JSON, local caching / recovery
will be available when possible.



Example use cases:

- Real time gaming on an ASP.NET site, without node.js.

- A business application where the application keeps its own local database 
and synchronizes changes back to the main database later, ensuring no delays 
while using the application.

Copyright (C) 2019 Kestas J. Kuliukas
