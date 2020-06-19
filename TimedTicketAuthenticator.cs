using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Mirror;
using System;
using System.Linq;

namespace MRL.Authenticators
{
    /// <summary>
    /// Mirror authenticator which supports signed, numbered, timed "tickets", first come, first served.
    /// </summary>
    /// <remarks>
    /// Based on Mirror.Authenticators.BasicAuthenticator & TimeoutAuthenticator.
    /// </remarks>
    [AddComponentMenu("Network/Authenticators/TimedTicketAuthenticator")]
    public class TimedTicketAuthenticator : NetworkAuthenticator
    {
        static readonly ILogger logger = LogFactory.GetLogger(typeof(TimedTicketAuthenticator));

        [Header("Global Properties")]

        /// <summary>
        /// Unique name/ID of event/server - must match
        /// </summary>
        [Tooltip("Unique name/ID of event.")]
        public string eventName;
        
        /// <summary>
        /// Server(only) signing secret (better to use secretEnvVar).
        /// </summary>
        [Tooltip("Signing secret (better to use server env var).")]
        public string secret;
        
        [Header("Client Properties")]

        /// <summary>
        /// Ticket (client, only) - normally set dynamically
        /// </summary>
        [Tooltip("Ticket (client) - normally set dynamically.")]
        public string ticketString;

        public string clientStatus = "Waiting for ticket";
        
        [Header("Server Properties")]
        
        /// <summary>
        /// Name of environment variable holding signing secret.
        /// </summary>
        [Tooltip("Environment variable for (server) signing secret.")]
        public string secretEnvVar;

        /// <summary>
        /// Max seat count (=authenticated users)
        /// </summary>
        [Tooltip("Maximum seat count.")]
        public int maxSeats = 50;

        /// <summary>
        /// Max unassigeed seat count (=wildcard users)
        /// </summary>
        [Tooltip("Maximum unassigned seat count.")]
        public int maxWildcardSeats = 50;

        /// <summary>
        /// time to disconnect unauthenticated client
        /// </summary>
        [Range(0, 600), Tooltip("Timeout to auto-disconnect in seconds. Set to 0 for no timeout.")]
        public float timeout = 30;

        [Header("Default tickets")]
        
        /// <summary>
        /// default performance start times (strings)
        /// </summary>
        [Tooltip("Default performance start time (yyyyMMddThhmssZ).")]
        public string defaultStartTime;
        
        /// <summary>
        /// default performance duration
        /// </summary>
        [Tooltip("Default performance duration (minutes).")]
        public double defaultDurationMinutes;
        
        /// <summary>
        /// default seat count
        /// </summary>
        [Tooltip("Default seat count.")]
        public int defaultSeats;

        void Start() {
            // generate default tickets?
            string key = getServerKey();
            if (defaultSeats > 0 && !String.IsNullOrEmpty(key)) {
                try {
                    DateTime start = DateTime.ParseExact(defaultStartTime, TimedTicket.DATE_FORMAT, System.Globalization.CultureInfo.InvariantCulture).ToUniversalTime();
                    Debug.Log("Default tickets:");
                    string ticket = TimedTicket.MakeTicket(eventName, start, defaultDurationMinutes, WILDCARD_SEAT, null, key);
                    Debug.Log(ticket);
                    for (int i=1; i<=defaultSeats; i++) {
                        ticket = TimedTicket.MakeTicket(eventName, start, defaultDurationMinutes, Convert.ToString(i), null, key);
                        Debug.Log(ticket);
                    }
                    
                } catch (FormatException e) {
                    logger.LogFormat(LogType.Error, $"start time format error {e}");
                }
            }
        }
        
        /// message to server
        public class AuthRequestMessage : MessageBase
        {
            public string ticketString;
        }

        /// response from server
        public class AuthResponseMessage : MessageBase
        {
            /// 100 = ok
            public byte code;
            public string message;
        }

        /// server record of "seat" occupancy
        class SeatInfo {
            public bool wildcard;
            public string seat;
            public TimedTicket ticket;
            public NetworkConnection conn;
        }
        
        /// server, all non-wildcard seats
        Dictionary<string,SeatInfo> seats = new Dictionary<string,SeatInfo>();
        /// server, wildcard seats, in joining order
        List<SeatInfo> wildcardSeats = new List<SeatInfo>();
        
        public override void OnStartServer()
        {
            // register a handler for the authentication request we expect from client
            NetworkServer.RegisterHandler<AuthRequestMessage>(OnAuthRequestMessage, false);
        }

        public override void OnStartClient()
        {
            clientStatus = "Connecting";
            // register a handler for the authentication response we expect from server
            NetworkClient.RegisterHandler<AuthResponseMessage>(OnAuthResponseMessage, false);
        }

        public override void OnServerAuthenticate(NetworkConnection conn)
        {
            // do nothing...wait for AuthRequestMessage from client
            if (timeout > 0) {
                // max time? -> kick
                StartCoroutine(CheckAuthenticated(conn));
            }
        }

        public bool PrecheckTicket(out string status) {
            if (String.IsNullOrEmpty(ticketString)) {
                 clientStatus = status = "No ticket";
                return false;
            }
            TimedTicket t = new TimedTicket();
            try {
                t.Parse(ticketString);
                
            } catch (FormatException) {
                clientStatus = status = "Ticket badly formatted";
                return false;
            }
            DateTime now = DateTime.UtcNow;
            if (!t.ClientCurrentAndValid(eventName, now)) {
                if (t.eventName != eventName) {
                    clientStatus = status = "Ticket for wrong event";
                } else if (t.Finished(now)) {
                    clientStatus = status = "Event has finished";
                } else if (t.MinutesUntilStart(now)>0) {
                    clientStatus = status = "Event hasn't started yet";
                } else {
                    clientStatus = status = "Ticket not valid";
                }
                return false;
            }
            clientStatus = status = "Ticket is ready to send";
            return true;
        }

        public override void OnClientAuthenticate(NetworkConnection conn)
        {
            string precheckStatus;
            if ( !PrecheckTicket(out precheckStatus) ) {
                logger.LogFormat(LogType.Error, "Ticket failed pre-check: {0} for {1}", precheckStatus, ticketString);
                conn.isAuthenticated = false;
                // disconnect the client - doing it synchronously hangs!
                StartCoroutine(DelayedDisconnect(conn, 0.01f));
                return;
            }
            clientStatus = "Checking ticket";
            // proceed
            AuthRequestMessage authRequestMessage = new AuthRequestMessage
            {
                ticketString = ticketString
            };

            conn.Send(authRequestMessage);
        }
        
        string getServerKey() {
            string val = null;
            if (!String.IsNullOrEmpty(secretEnvVar)) {
                val = Environment.GetEnvironmentVariable(secretEnvVar);
            }
            if (!String.IsNullOrEmpty(secret)) {
                val = secret;
            }
            if (String.IsNullOrEmpty(val)) {
                Debug.Log("Warning: Ticket signing secret not set");
                return "";
            }
            return val;
        }
        // false => remove
        bool CheckSeatConnection(SeatInfo si) {
            if (si.conn != null ) {
                if (!si.conn.isAuthenticated) {
                    si.conn = null;
                    si.ticket = null;
                    return false;
                }
                if (NetworkServer.connections.ContainsKey(si.conn.connectionId)) {
                    // OK
                    return true;
                } else {
                    // presume connection lost
                    si.conn.isAuthenticated = false;
                    if (logger.LogEnabled()) logger.LogFormat(LogType.Log, "Authentication: seat {0} presume {1} disconnected", si.seat, si.conn.connectionId);
                    si.conn = null;
                    si.ticket = null;
                    return false;
                }
            }
            else {
                return false;
            }
        }
        void CheckCurrentSeatConnections() {
            // go through current seats and handle any disconnected clients
            // note can't trust connection state due to races
            foreach (SeatInfo si in seats.Values) {
                CheckSeatConnection(si);
            }
            for(int i=0; i<wildcardSeats.Count; i++) {
                if (!CheckSeatConnection(wildcardSeats[i])) {
                    wildcardSeats.RemoveAt(i);
                    i--;
                }
            }
        }
        
        public static readonly string WILDCARD_SEAT = "-";
        int nextWildcardSeat = 1;
        
        public void OnAuthRequestMessage(NetworkConnection conn, AuthRequestMessage msg)
        {
            if (logger.LogEnabled()) logger.LogFormat(LogType.Log, "Authentication Request: ticket {0}", msg.ticketString);
            TimedTicket t = new TimedTicket();
            try {
                t.Parse(msg.ticketString);
            } catch (Exception e) {
                if (logger.LogEnabled()) logger.LogFormat(LogType.Log, "Error parsing: ticket {0}: {1}", msg.ticketString, e);
                sendError(conn, 200, "Could not parse ticket");
                return;
            }
            DateTime now = DateTime.UtcNow;
            // check the ticket
            if ( t.ServerCurrentAndValid(eventName, now, getServerKey()) )
            {
                // update for any recently disconnected cliens
                CheckCurrentSeatConnections();
                int numAuthenticated = NetworkServer.connections.Count(kv => kv.Value.isAuthenticated);
                
                SeatInfo si = null;
                if (t.seat != WILDCARD_SEAT) {
                    // if a non-wildcard user joins and they are not kicking
                    // someone then eject wildcard users in LIFO(?) order
                     // check/assign seat
                    if (!seats.ContainsKey(t.seat)) {
                        // new seat
                        si = new SeatInfo {
                            wildcard = false,
                            seat = t.seat,
                            ticket = t,
                            conn = conn
                        };
                        seats.Add(t.seat, si);
                        if (logger.LogEnabled()) logger.LogFormat(LogType.Log, "Assigned new seat {0} to connection {1}", t.seat, conn.connectionId);
                        // kick wildcard users if necessary
                        if (numAuthenticated >= maxSeats) {
                            if (!KickWildcardSeats( numAuthenticated+1-maxSeats)) {
                                // do we block numbered seats at the quota?!
                                // just note that people shouldn't send out
                                // more seats than set!
                            }
                        }
                    } else {
                        si = seats[t.seat];
                        if (si.conn != null) {
                            if (t.durationMinutes > si.ticket.durationMinutes) {
                                // current occupant has shorter duration => precedence
                                if (logger.LogEnabled()) logger.LogFormat(LogType.Log, "Current seat {0} holder has precedence, {1} vs {2} minutes", si.seat, si.ticket.durationMinutes, t.durationMinutes);
                                sendError(conn, 200, "Seat is temporarily in use");
                                return;
                            }
                            if (logger.LogEnabled()) logger.LogFormat(LogType.Log, "Kick connection {0} from seat {1}, valid? {2}", si.conn.connectionId, si.seat, si.ticket.ServerCurrentAndValid(eventName, now, getServerKey()));
                            sendError(si.conn, 201, String.Format("Someone else has taken seat {0}", t.seat));
                        }
                        si.ticket = t;
                        si.conn = conn;
                        if (logger.LogEnabled()) logger.LogFormat(LogType.Log, "Assigned existing seat {0} to connection {1}", t.seat, conn.connectionId);
                    }
                } else {
                    // would we need to kick a wildcard with longer duration?
                    if (numAuthenticated >= maxSeats || wildcardSeats.Count>= maxWildcardSeats) {
                        // kick a wildcard with longer duration?!
                        if (wildcardSeats.Count>0 && wildcardSeats[wildcardSeats.Count-1].ticket.durationMinutes > t.durationMinutes) {
                            if (KickWildcardSeats( 1 )) {
                                // that made a space!
                                numAuthenticated--;
                            }
                            else {
                                if (logger.LogEnabled()) logger.LogFormat(LogType.Error, "Unable to kick long duration wildcard");
                            }
                        }
                    }
                    // add a wildcard user if there is space.
                    if (numAuthenticated >= maxSeats || wildcardSeats.Count>= maxWildcardSeats) {
                        if (logger.LogEnabled()) logger.LogFormat(LogType.Log, "No capacity for new wildcard seat, {0}/{1} and {2}/{3} total", wildcardSeats.Count, maxWildcardSeats, numAuthenticated, maxSeats);
                        sendError(conn, 200, "No space available");
                        return;
                    }
                    // new seat
                    si = new SeatInfo {
                        wildcard = true,
                        seat = t.seat+(nextWildcardSeat++),
                        ticket = t,
                        conn = conn
                    };
                    // keep in increasing duration order, so longer tickets are kicked first
                    int shorter = wildcardSeats.Count(s => (s.ticket.durationMinutes <= t.durationMinutes));
                    wildcardSeats.Insert(shorter, si);
                    if (logger.LogEnabled()) logger.LogFormat(LogType.Log, "Assigned new seat {0} to connection {1}", si.seat, conn.connectionId);
                }
                // OK
                conn.authenticationData = t;
                
                // create and send msg to client so it knows to proceed
                AuthResponseMessage authResponseMessage = new AuthResponseMessage
                {
                    code = 100,
                    message = "Success"
                };

                conn.Send(authResponseMessage);
                 
                // check expire...
                if (si != null ) {
                    StartCoroutine(MonitorTicketExpired(conn, si));
                }
                // Invoke the event to complete a successful authentication
                OnServerAuthenticated.Invoke(conn);
            }
            else
            {
                sendError(conn, 200, "Invalid Credentials");
            }
        }
        // return OK
        bool KickWildcardSeats( int target) {
            // try to kick #target seats
            // eject last, first
            while (target > 0 && wildcardSeats.Count > 0) {
                SeatInfo si = wildcardSeats[wildcardSeats.Count-1];
                wildcardSeats.RemoveAt(wildcardSeats.Count-1);
                if (si.conn!=null) {
                    if (logger.LogEnabled()) logger.LogFormat(LogType.Log, "Kick wildcard seat {0}, connection {1}", si.seat, si.conn.connectionId);
                    
                    si.conn.isAuthenticated = false;
                    try {
                        si.conn.Disconnect();
                    } catch(Exception) {
                        // nothing we can do or want to
                    }
                    target--;
                }
            }
            if (target>0) {
                if (logger.LogEnabled()) logger.LogFormat(LogType.Log, "Warning: no wildcard seats left with kick target {0}", target);
                return false;
            }
            return true;
        }
        void sendError(NetworkConnection conn, byte code, string message) {
            // create and send msg to client so it knows to disconnect
            AuthResponseMessage authResponseMessage = new AuthResponseMessage
            {
                code = code,
                message = message
            };

            try {
                conn.Send(authResponseMessage);
            } catch (Exception e) {
                if (logger.LogEnabled()) logger.LogFormat(LogType.Log, "Error sending auth error to connection {0}: {1}", conn.connectionId, e);
                
            }
            // must set NetworkConnection isAuthenticated = false
            conn.isAuthenticated = false;

            // disconnect the client after 1 second so that response message gets delivered
            StartCoroutine(DelayedDisconnect(conn, 1));
        }
   
        public IEnumerator DelayedDisconnect(NetworkConnection conn, float waitTime)
        {
            yield return new WaitForSeconds(waitTime);
            try {
                conn.Disconnect();
            } catch (Exception e) {
                 if (logger.LogEnabled()) logger.LogFormat(LogType.Log, "Error doing delayed disconnect of connection {0}: {1}", conn.connectionId, e);
                 
             }
        }

        public void OnAuthResponseMessage(NetworkConnection conn, AuthResponseMessage msg)
        {
            if (msg.code == 100)
            {
                if (logger.LogEnabled()) logger.LogFormat(LogType.Log, "Authentication Response: {0}", msg.message);
                clientStatus = "Ticket accepted";
                // Invoke the event to complete a successful authentication
                OnClientAuthenticated.Invoke(conn);
            }
            else
            {
                logger.LogFormat(LogType.Error, "Authentication Response: {0}", msg.message);
                clientStatus = String.Format("Ticket rejected ({0})", msg.message);
                
                // Set this on the client for local reference
                conn.isAuthenticated = false;

                // disconnect the client
                conn.Disconnect();
                
                // debug/test...
                if (!String.IsNullOrEmpty(getServerKey())) {
                    TimedTicket t = new TimedTicket();
                    try {
                        t.Parse(ticketString);
                        t.Sign(getServerKey());
                        string ts = t.ToString();
                        logger.LogFormat(LogType.Error, "Self-sign ticket: {0}", ts);
                         
                    } catch (FormatException e) {
                        logger.LogFormat(LogType.Error, "problem making self-sign ticket: {0}", e);
                    }
                }
            }
        }
        /// <summary>
        /// co-routine to kick unauthenticated connection(s)
        /// </summary>
        IEnumerator CheckAuthenticated(NetworkConnection conn)
        {
            if (logger.LogEnabled()) logger.Log($"Authentication countdown started {conn} {timeout}");

            yield return new WaitForSecondsRealtime(timeout);

            if (!conn.isAuthenticated)
            {
                if (logger.LogEnabled()) logger.Log($"Authentication Timeout {conn}");

                conn.Disconnect();
            }
        }
        /// <summary>
        /// co-routine to kick timed out ticket connection(s)
        /// </summary>
        IEnumerator MonitorTicketExpired(NetworkConnection conn, SeatInfo si)
        {
            // If clients can ever change their ticket this will need updating
            if (si.conn != conn) {
                if (logger.LogEnabled()) logger.Log(String.Format("Connection {0} already lost seat {1}", conn.connectionId, si.seat));
                yield break;
            }
            DateTime now = DateTime.UtcNow;
            double elapsed = now.Subtract(si.ticket.startTime).TotalMinutes;
            double delay = si.ticket.durationMinutes-elapsed;
            if (delay > 0) {
                if (logger.LogEnabled()) logger.Log(String.Format("Wait {0} for connection {1} expiry on seat {2}", delay, conn.connectionId, si.seat));
                
                yield return new WaitForSecondsRealtime((float)(60*delay));
            }
            if (si.conn != conn) {
                if (logger.LogEnabled()) logger.Log(String.Format("Connection {0} already lost seat {1}", conn.connectionId, si.seat));
                yield break;
            }
            if (logger.LogEnabled()) logger.Log(String.Format("Connection {0} expires seat {1} ({2}/{3} minutes)", conn.connectionId, si.seat, elapsed, si.ticket.durationMinutes));
            si.conn = null;
            si.ticket = null;
            conn.isAuthenticated = false;
            conn.Disconnect();
        }
    }
}
