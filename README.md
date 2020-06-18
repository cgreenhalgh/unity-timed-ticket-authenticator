# Unity TimedTicketAuthenticator

Chris Greenhalgh, The University of Nottingham, 2020.

This is an Authenticator for the Mirror networking system for Unity.

status: 
- seemed ok, but client seems to be hanging at the moment.

## use

Add the TimedTicketAuthenticator component to yor NetworkManager GameObject.
- set it as the 'authenticator' in the NetworkManager component.

Set it's properties:
- event name - must be the same on client and server
- secret env var - must be set on server to env var holding ticket signing secret
- secret - unless you put it right here in the unity project; probably bad!
- timeout - how long server allows new connections without authentication (like TimeoutAuthenticator)
- ticket string - ticket client will present; usually set from your client connect script

Client must provide a valid ticket in order to authenticate.
Each ticket has an associated 'seat', and only one client can be in each 'seat' at once (any current client will be kicked if a new one joins on the same seat).

Tickets look like `x-ticket:1:intothedepths:20200618T160000Z:3600:1:XYZ`, where the parts are:
- `x-ticket` - scheme, required
- `1` - ticket version, required, currently 1
- event name - must match server
- start time - in UTC, in format above, "yyyyMMdd'T'hhmmssZ"
- duration (seconds) - e.g. 1 hour above
- seat - integer, arbitrary
- signature - signed with server key

For testing, if the server key is set as 'secret' in the client and the ticket is rejected then the client will print a correctly signed key on the debug console.

## sample client

ClientUI.js is a simple client GUI that shows how a ticket could be read/used.
Typically, add it to the same NetworkManager object.
Note the use of PrecheckTicket to avoid initiating network connections before/after events.

Also note that the sample client uses NetworkManager.StartClient(uri) with its own serverUri value rather than the NetworkManager default URL. This is needed (e.g.) to specify a path for the websocket client transport (the NetworkManager default is then still used for the server). 

### getting ticket from URL

Note, the sample client will set the ticket from the URL parameter 'ticket' if built for WebGL. This uses the javascript native plugin Plugins/jshelpers.jslib.

