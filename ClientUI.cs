using System.Collections;
using System;
using System.Collections.Generic;
using UnityEngine;
using Mirror;
using MRL.Authenticators;
using System.Web;
using System.Runtime.InteropServices;

public class ClientUI : MonoBehaviour
{
    public NetworkManager manager;
    public TimedTicketAuthenticator tta;
    /// <summary>
    /// The horizontal offset in pixels to draw the HUD runtime GUI at.
    /// </summary>
    public int offsetX;

    /// <summary>
    /// The vertical offset in pixels to draw the HUD runtime GUI at.
    /// </summary>
    public int offsetY;

    /// <summary>
    /// Server Uri for client connect (does not use NetworkManager default)
    /// e.g. to include websocket path component.
    /// </summary>
    public string serverUri;
    
    string ticket;
    
    void OnGUI()
    {
        if (manager == null) {
            manager = GetComponent<NetworkManager>();
        }
        if (tta == null) {
            tta =  GetComponent<TimedTicketAuthenticator>();
        }
        
        GUIStyle button = new GUIStyle(GUI.skin.GetStyle("button"));
        //button.fontSize = 32;
        GUIStyle label = new GUIStyle(GUI.skin.GetStyle("label"));
        //label.fontSize = 32;
        GUILayout.BeginArea(new Rect(10 + offsetX, 40 + offsetY, 600, 9999));
        if (!NetworkClient.isConnected) {
            if (!NetworkClient.active)
            {
                GUILayout.Label("Ticket:", label);
                ticket = GUILayout.TextField(ticket /*, label*/);
                //Debug.Log(String.Format("Ticket: {0}", ticket));
                if (tta != null && !String.IsNullOrEmpty(ticket)) {
                    tta.ticketString = ticket;
                }
                string precheckStatus;
                if (tta.PrecheckTicket(out precheckStatus) && GUILayout.Button("Join with ticket", button))
                {
                    Debug.Log("Start client with ticket "+ticket);
                    manager.StartClient(new Uri(serverUri));
                }
            }
            else
            {
                // Connecting
                GUILayout.Label("Joining...", label);
                if (GUILayout.Button("Cancel join Attempt", button))
                {
                    manager.StopClient();
                }
            }
        } else {
            if (GUILayout.Button("Leave", button))
            {
                manager.StopClient();
            }
        }
        GUILayout.Label("Status: "+tta.clientStatus, label);
        GUILayout.EndArea();
    }
    
    #if (UNITY_WEBGL && !UNITY_EDITOR)
    // NB this is webgl-specific, to access browser URL, and is in
    // Assets/Plugins/jshelpers.jslib
    [DllImport("__Internal")]
    private static extern string getWindowLocation();
    #endif
    
    // Start is called before the first frame update
    void Start()
    {
        #if (UNITY_WEBGL && !UNITY_EDITOR)
        string location = getWindowLocation();
        Debug.Log(String.Format("Window location = {0}", location));
        try {
            Uri url = new Uri(location);
            string ticket = HttpUtility.ParseQueryString(url.Query).Get("ticket");
            if (!String.IsNullOrEmpty(ticket)) {
                Debug.Log(String.Format("ticket = {0}", ticket));
                if (tta == null) {
                    tta =  GetComponent<TimedTicketAuthenticator>();
                }
                tta.ticketString = ticket;
            }
        }
        catch (Exception e) {
            Debug.Log(String.Format("Error processing url {0}: {1}", location, e));
        }
        #endif
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
