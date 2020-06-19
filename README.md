# Unity TimedTicketAuthenticator

Chris Greenhalgh, The University of Nottingham, 2020.

This is an Authenticator for the Mirror networking system for Unity.

status: 
- seemed ok, but relatively untested, especially with many clients

## use

Add the TimedTicketAuthenticator component to yor NetworkManager GameObject.
- set it as the 'authenticator' in the NetworkManager component.

Set it's properties:
- event name - must be the same on client and server
- secret env var - must be set on server to env var holding ticket signing secret
- secret - unless you put it right here in the unity project; probably bad!
- timeout - how long server allows new connections without authentication (like TimeoutAuthenticator)
- ticket string - ticket client will present; usually set from your client connect script
- max seats - max total authenticated users (but if you give out more allocated tickets it can go over this); this is separate to the NetworkServer max connections/clients
- max wildcard seats - max number of unassigned seats active


Client must provide a valid ticket in order to authenticate.
Each ticket has an associated 'seat', and only one client can be in each 'seat' at once (any current client will be kicked if a new one joins on the same seat).

Tickets look like `x-ticket:1:intothedepths:20200618T160000Z:3600:1:XYZ`, where the parts are:
- `x-ticket` - scheme, required
- `1` - ticket version, required, currently 1
- event name - must match server
- start time - in UTC, in format above, "yyyyMMdd'T'hhmmssZ"
- duration (seconds) - e.g. 1 hour above
- seat - integer (actually a string), arbitrary but usually 1, 2, .... Note, `-` is the special "wildcard" seat, and multiple clients can join with it.
- signature - signed with server key

For testing, if the server key is set as 'secret' in the client and the ticket is rejected then the client will print a correctly signed key on the debug console.

## behaviour

Note that assigned seat tickets have precedence over unassigned tickets, so unassigned clients may be kicked if clients arrive with a valid assigned ticket.

Also note that within each group (assigned and unassigned), tickets with short durations have priority over tickets with long durations. This allows you to have long term, low priority access that is temporarily overriden by more specific tickets.

If a client presents a valid ticket for an assigned seat that is already in use then the current client in that seat is kicked UNLESS the current client's ticket duration is shorter (in which case the arriving client is rejected).

So if you give the same assigned seat ticket to more than one person then they will be able to repeatedly join and kick the other out. This also means that if they join from a second client using the same ticket then it will kick the first out.

If a client presents a valid non-assigned ticket and there aren't too many clients (total or wildcard) then they can join. In addition, if a current client has an unassigned ticket with a longer duration then they are kicked so the client with a shorter duration ticket can join.

So if you give the same unassigned ticket to lots of people then it works first-come, first-served, until the configured capacity is reached, after which new clients will be rejected. And if the same person joins on a second client then they will take another unassigned seat.

## tips

So you could use specific "seats" to mark particular roles, e.g. control interface. But if so, be careful not to re-use or pass on those tickets, as your original clients will be kicked by others using those ticket(s).

## sample client

ClientUI.js is a simple client GUI that shows how a ticket could be read/used.
Typically, add it to the same NetworkManager object.
Note the use of PrecheckTicket to avoid initiating network connections before/after events.

Also note that the sample client uses NetworkManager.StartClient(uri) with its own serverUri value rather than the NetworkManager default URL. This is needed (e.g.) to specify a path for the websocket client transport (the NetworkManager default is then still used for the server). 

### getting ticket from URL

Note, the sample client will set the ticket from the URL parameter 'ticket' if built for WebGL. This uses the javascript native plugin Plugins/jshelpers.jslib.

