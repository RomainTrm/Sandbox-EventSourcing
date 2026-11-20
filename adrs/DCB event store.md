Two tables : 

1. ``dcb_events`` that store events
2. ``dcb_flags`` that store flags for event streams

An event in ``dcb_events`` can be referenced by multiple streams in ``dcb_flags``.  

## ``dcb_flags_lite``

I chose to add a ``sequence_number`` but it seems that it doesn't match the description in [dcb.events](https://dcb.events/#writing-events).

Cons:

1. Using a ``sequence_number`` means it should load the whole stream each time a stream id is used.
2. In the website, it seems that only usefull events are loaded for a given stream, meaning we can run concurent commands on the same stream as long as new events should not be included in the stream loaded by the concurent command.

Pros:

1. Database serialization is delegated to Postgresql, meaning there's no need for a lock in the code.

## ``dcb_flags_full``

It doesn't contains ``sequence_number`` and is closer to DCB as described in [dcb.events](https://dcb.events).  
This way, it allows concurent commands on the same stream, but it requires a lock on writes in the C# code.
