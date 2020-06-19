using System;
using System.Text.RegularExpressions;
//using UnityEngine;
using System.Security.Cryptography;

namespace MRL.Authenticators
{
    /// <summary>
    /// A string-encoded timed ticket.
    /// </summary>
    [Serializable()]
    public class TimedTicket
    {
        /// <summary>
        /// ticket scheme name (unofficial)
        /// </summary>
        public static readonly string SCHEME = "x-ticket";
        
        /// <summary>
        /// ticket version number
        /// </summary>
        public string version;
        
        /// <summary>
        /// current version number
        /// </summary>
        public static readonly string CURRENT_VERSION = "1";
        
        /// <summary>
        /// event name/ID
        /// </summary>
        public string eventName;
        
        /// <summary>
        /// start date/time (UTC)
        /// </summary>
        public DateTime startTime;
        
        /// <summary>
        /// duration (minutes)
        /// </summary>
        public double durationMinutes;
        
        /// <summary>
        /// seat number (normally counts from 1; * allows multiple)
        /// </summary>
        public string seat;
        
        /// <summary>
        /// ticket check string
        /// </summary>
        public string check;
        
        /// <summary>
        /// cache of ticket check valid
        /// </summary>
        [NonSerialized()]
        public bool valid;
        
        /// <summary>
        /// event URL (optional)
        /// </summary>
        public Uri serverUrl;
        
        /// <summary>
        /// default URL scheme (https)
        /// </summary>
        public static readonly string DEFAULT_SERVER_URL_SCHEME = "https:";
        
        public static readonly string DATE_FORMAT = "yyyyMMdd'T'HHmmssK";
        public override string ToString() {
            string basic = String.Format("{0}:{1}:{2}:{3}:{4}:{5}:{6}", SCHEME, Uri.EscapeDataString(version), Uri.EscapeDataString(eventName), startTime.ToUniversalTime().ToString(DATE_FORMAT), durationMinutes, Uri.EscapeDataString(seat), Uri.EscapeDataString(check));
            if (serverUrl != null) {
                string url = serverUrl.ToString();
                if (url.StartsWith(DEFAULT_SERVER_URL_SCHEME)) {
                    url = url.Substring(DEFAULT_SERVER_URL_SCHEME.Length);
                }
                return basic+":"+url;
            } else {
                return basic;
            }
        }
        /// <summary>
        /// parse a ticket uri into this object
        /// </summary>
        /// <exception cref="System.FormatException">Thrown if format doesn't match</exception>
        public void Parse(String s) {
            // check scheme & version first
            Regex prefix = new Regex("^"+SCHEME+":([^:]+):(.*)$");
            Match bmatch = prefix.Match(s);
            if (!bmatch.Success) {
                throw new FormatException(String.Format("Not a valid {0}: {1}", SCHEME, s));
            }
            version = Uri.UnescapeDataString(bmatch.Groups[1].Value);
            string rest = bmatch.Groups[2].Value;
            if (version == "1") {
                ParseVersion1(rest);
            } else {
                throw new FormatException(String.Format("Unsupported ticket version: {0} (currently {1})", version, CURRENT_VERSION));
            }
        }
        /// <exception cref="System.FormatException">Thrown if format doesn't match</exception>
        void ParseVersion1(String rest) {
            //eventName, time, duration, seat, check, ?rest
            Regex v1rest = new Regex("^([^:]+):([^:]+):([^:]+):([^:]+):([^:]+)(:([^:/]*:))?(.*)$");
            Match rmatch = v1rest.Match(rest);
            if (!rmatch.Success) {
                throw new FormatException(String.Format("Not a valid {0} v {1}: ...{2}", SCHEME, "1", rest));
            }
            eventName = Uri.UnescapeDataString(rmatch.Groups[1].Value);
            // also throws FormatException
            startTime = DateTime.ParseExact(rmatch.Groups[2].Value, DATE_FORMAT, System.Globalization.CultureInfo.InvariantCulture).ToUniversalTime();
            durationMinutes = Convert.ToDouble(Uri.UnescapeDataString(rmatch.Groups[3].Value));
            seat = Uri.UnescapeDataString(rmatch.Groups[4].Value);
            check = Uri.UnescapeDataString(rmatch.Groups[5].Value);
            string scheme = rmatch.Groups[7].Value;
            if (scheme.Length == 0) {
                scheme = DEFAULT_SERVER_URL_SCHEME;
            }
            string url = rmatch.Groups[8].Value;
            if (url.Length == 0) {
                serverUrl = null;
            } else {
                serverUrl = new Uri(scheme+url);
            }
            
            //Debug.Log(String.Format("parse ticket ...{0} -> {1}", rest, this.ToString()));
        }
        
        // internal - calculate check value using provided key
        string GetCheck(string key) {
            // no URL, empty check
            string basic = String.Format("{0}:{1}:{2}:{3}:{4}:{5}:{6}", SCHEME, Uri.EscapeDataString(version), Uri.EscapeDataString(eventName), startTime.ToUniversalTime().ToString(DATE_FORMAT), durationMinutes, Uri.EscapeDataString(seat), "");
            // MD5 - enough? supported in Web?
            HMACMD5 hmac = new HMACMD5(System.Text.Encoding.UTF8.GetBytes(key));
            byte []hash = hmac.ComputeHash(System.Text.Encoding.UTF8.GetBytes(basic));
            // strip trailing '='s
            string base64 = Convert.ToBase64String(hash);
            while (base64.EndsWith("=")) {
                base64 = base64.Substring(0, base64.Length-1);
            }
            return base64;
        }
        
        /// <summary>
        /// sign this ticket with key
        /// </summary>
        public void Sign(string key) {
            if (String.IsNullOrEmpty(key)) {
                // can't be valid with no key!
                check = "";
                valid = false;
                return;
            }
            check = GetCheck(key);
            valid = true;
        }
        /// <summary>
        /// check if this ticket is valid with key (updates valid)
        /// </summary>
        public bool CheckValid(string key) {
            if (String.IsNullOrEmpty(key)) {
                // can't be valid with no key!
                valid = false;
                return false;
            }
            if (check == GetCheck(key)) {
                valid = true;
            } else {
                valid = false;
            }
            return valid;
        }
        /// <summary>
        /// check ticket time
        /// </summary>
        public bool Current(DateTime now) {
            double t = now.Subtract(startTime).TotalMinutes;
            return t>=0 && t<=durationMinutes;
        }
        /// <summary>
        /// time until start (or 0)
        /// </summary>
        public double MinutesUntilStart(DateTime now) {
            double t = startTime.Subtract(now).TotalMinutes;
            return t<0 ? 0 : t;
        }
        /// <summary>
        /// finished
        /// </summary>
        public bool Finished(DateTime now) {
            double t = now.Subtract(startTime).TotalMinutes;
            return t>=durationMinutes;
        }
        /// <summary>
        /// Could be ok (client check without signature)
        /// <summary>
        public bool ClientCurrentAndValid(string eventName, DateTime now) {
            if (this.eventName != eventName) {
                return false;
            }
            if (!Current(now)) {
                return false;
            }
            return true;
        }
        /// <summary>
        /// Is ok (server check with signature)
        /// <summary>
        public bool ServerCurrentAndValid(string eventName, DateTime now, string key) {
            if (this.eventName != eventName) {
                return false;
            }
            if (!Current(now)) {
                return false;
            }
            return CheckValid(key);
        }
        /// return a new signed ticket string
        /// </summary>
        public static string MakeTicket(string eventName, DateTime startTime, double durationMinutes, string seat, Uri serverUrl, string key) {
            TimedTicket t = new TimedTicket();
            t.version = CURRENT_VERSION;
            t.eventName = eventName;
            t.startTime = startTime.ToUniversalTime();
            t.durationMinutes = durationMinutes;
            t.seat = seat;
            t.serverUrl = serverUrl;
            t.Sign(key);
            return t.ToString();
        }
    }
}
