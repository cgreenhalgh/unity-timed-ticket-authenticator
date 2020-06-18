using System.Collections;
using System;
using System.Collections.Generic;
using UnityEngine;
using Mirror;
using MRL.Authenticators;

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
                GUILayout.BeginHorizontal();
                ticket = GUILayout.TextField(ticket /*, label*/);
                //Debug.Log(String.Format("Ticket: {0}", ticket));
                if (tta != null && !String.IsNullOrEmpty(ticket)) {
                    tta.ticketString = ticket;
                }
                if (tta.PrecheckTicket() && GUILayout.Button("Join with ticket", button))
                {
                    Debug.Log("Start client with ticket "+ticket);
                    manager.StartClient(new Uri(serverUri));
                }
                GUILayout.EndHorizontal();
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
    
    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
