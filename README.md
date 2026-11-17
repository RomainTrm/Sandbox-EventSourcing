# Sandbox-EventSourcing

Several implementations of the same problem to explore and compare some event sourcing strategies.

## Problem and strategies

The app allow a ``store`` for buying and selling ``item``s to ``customer``s.  
Every buying/selling is expressed in the form of an ``order``. An ``order`` can include as many ``item``s as needed.  
There are several ``store``s, each manage his own inventory of ``item``s, it can only sell ``item``s from his own inventory.  
A ``store`` can buy ``item``s to another ``store`` through a dedicated ``order``.  
When an ``item`` moves from one inventory to another, it should be considered as a new ``item`` with a new identity, old identity is saved in a property of the ``item`` for traceability. 

### 1. Classical "Aggregates" approach

Each ``aggregate`` own his stream of ``event``s, ``event``s are not shared between ``aggregate``s.  
A ``command`` load every ``event``s of the ``aggregate``, then a new ``event``s are appended to the stream.  
In case of a ``command`` that must handle several ``aggregate``s at once, the ``command handler`` is responsible for the orchestration of the ``aggregate``s.

### 2. Dynamic Consistency Boundary (DCB) approach

To details, [DCB](https://dcb.events/)

## How to run?

Start database : ``docker-compose -f docker-compose.yml up postgres admin``.  
Add new migrations scripts in ``Sandbox.Updater\Migrations`` then declare them in ``Sandbox.Updater\UpgrateSteps.cs``.  
Update database schema by executing ``Sandbox.Updater``.  
Explore database on [adminer](http://127.0.0.1:8381/?pgsql=postgres&username=postgres) with ``postgres`` as a password.  
