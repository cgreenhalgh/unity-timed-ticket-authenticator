using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Mirror;
using System;

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

        [Header("Server Properties")]
        
        /// <summary>
        /// Name of environment variable holding signing secret.
        /// </summary>
        [Tooltip("Environment variable for (server) signing secret.")]
        public string secretEnvVar;

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
        [Tooltip("Default performance duration (seconds).")]
        public double defaultDurationSeconds;
        
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
                    for (int i=1; i<=defaultSeats; i++) {
                        string ticket = TimedTicket.MakeTicket(eventName, start, defaultDurationSeconds, i, null, key);
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
            public int seat;
            public TimedTicket ticket;
            public NetworkConnection conn;
        }
        
        /// server, all seats
        Dictionary<int,SeatInfo> seats = new Dictionary<int,SeatInfo>();
        
        public override void OnStartServer()
        {
            // register a handler for the authentication request we expect from client
            NetworkServer.RegisterHandler<AuthRequestMessage>(OnAuthRequestMessage, false);
        }

        public override void OnStartClient()
        {
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

        public override void OnClientAuthenticate(NetworkConnection conn)
        {
            if (String.IsNullOrEmpty(ticketString)) {
                logger.LogFormat(LogType.Error, "No ticket");
                conn.isAuthenticated = false;
                // disconnect the client
                conn.Disconnect();
                return;
            }
            TimedTicket t = new TimedTicket();
            try {
                t.Parse(ticketString);
                
            } catch (FormatException e) {
                logger.LogFormat(LogType.Error, "Ticket not valid: {0}, {1}", ticketString, e.ToString());
                conn.isAuthenticated = false;
                // disconnect the client
                conn.Disconnect();
                return;
            }
            DateTime now = DateTime.UtcNow;
            if (!t.ClientCurrentAndValid(eventName, now)) {
                if (t.eventName != eventName) {
                    logger.LogFormat(LogType.Error, "Ticket for wrong event: {0}, expect {1}", ticketString, eventName);
                } else if (t.Finished(now)) {
                    logger.LogFormat(LogType.Error, "Event has finished: {0}, now {1}", ticketString, now);
                } else if (t.SecondsUntilStart(now)>0) {
                    logger.LogFormat(LogType.Error, "Event starts in {0} seconds: {1}, now {2}", t.SecondsUntilStart(now), ticketString, now);
                } else {
                    logger.LogFormat(LogType.Error, "Ticket not current/valid: {0}, now {1}", ticketString, now);
                }
                conn.isAuthenticated = false;
                // disconnect the client
                conn.Disconnect();
                return;
            }
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
                SeatInfo si = null;
                // check/assign seat
                if (!seats.ContainsKey(t.seat)) {
                    // new seat
                    si = new SeatInfo {
                        seat = t.seat,
                        ticket = t,
                        conn = conn
                    };
                    seats.Add(t.seat, si);
                    if (logger.LogEnabled()) logger.LogFormat(LogType.Log, "Assigned new seat {0} to connection {1}", t.seat, conn.connectionId);
                } else {
                    si = seats[t.seat];
                    if (logger.LogEnabled()) logger.LogFormat(LogType.Log, "Kick connection {0} from seat {1}, valid? {2}", si.conn.connectionId, si.seat, si.ticket.ServerCurrentAndValid(eventName, now, getServerKey()));
                    sendError(si.conn, 201, String.Format("Someone else has taken seat {0}", t.seat));
                    si.ticket = t;
                    si.conn = conn;
                    if (logger.LogEnabled()) logger.LogFormat(LogType.Log, "Assigned existing seat {0} to connection {1}", t.seat, conn.connectionId);
                }
                
                conn.authenticationData = t;
                
                // create and send msg to client so it knows to proceed
                AuthResponseMessage authResponseMessage = new AuthResponseMessage
                {
                    code = 100,
                    message = "Success"
                };

                conn.Send(authResponseMessage);
                 
                // check expire...
                StartCoroutine(MonitorTicketExpired(conn, si));

                // Invoke the event to complete a successful authentication
                OnServerAuthenticated.Invoke(conn);
            }
            else
            {
                sendError(conn, 200, "Invalid Credentials");
            }
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

                // Invoke the event to complete a successful authentication
                OnClientAuthenticated.Invoke(conn);
            }
            else
            {
                logger.LogFormat(LogType.Error, "Authentication Response: {0}", msg.message);

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
            double elapsed = now.Subtract(si.ticket.startTime).TotalSeconds;
            double delay = si.ticket.durationSeconds-elapsed;
            if (delay > 0) {
                if (logger.LogEnabled()) logger.Log(String.Format("Wait {0} for connection {1} expiry on seat {2}", delay, conn.connectionId, si.seat));
                
                yield return new WaitForSecondsRealtime((float)delay);
            }
            if (si.conn != conn) {
                if (logger.LogEnabled()) logger.Log(String.Format("Connection {0} already lost seat {1}", conn.connectionId, si.seat));
                yield break;
            }
            if (logger.LogEnabled()) logger.Log(String.Format("Connection {0} expires seat {1} ({2}/{3} seconds)", conn.connectionId, si.seat, elapsed, si.ticket.durationSeconds));
            si.conn = null;
            si.ticket = null;
            conn.isAuthenticated = false;
            conn.Disconnect();
        }
    }
}
