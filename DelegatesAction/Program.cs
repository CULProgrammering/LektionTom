using System;
using System.Collections.Generic;
using System.Net;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;

class WebSocketServer
{
    // List to store connected WebSocket clients
    private static readonly List<WebSocket> clients = new List<WebSocket>();
    // Object for thread-safe operations on the clients list
    private static readonly object lockObj = new object();

    public static async Task Main(string[] args)
    {
        // Set up the HTTP listener
        var listener = new HttpListener();
        listener.Prefixes.Add("http://localhost:8080/");
        listener.Start();
        Console.WriteLine("Server started. Listening on http://localhost:8080/");

        // Main loop to accept incoming connections
        while (true)
        {
            var context = await listener.GetContextAsync();
            if (context.Request.IsWebSocketRequest)
            {
                // Accept WebSocket connection
                var webSocketContext = await context.AcceptWebSocketAsync(null);
                var webSocket = webSocketContext.WebSocket;

                // Add client to the list (thread-safe)
                lock (lockObj)
                {
                    clients.Add(webSocket);
                }

                // Start handling this client in a separate task
                _ = HandleClientAsync(webSocket);
            }
            else
            {
                // Reject non-WebSocket requests
                context.Response.StatusCode = 400;
                context.Response.Close();
            }
        }
    }

    // Handle communication with a single client
    private static async Task HandleClientAsync(WebSocket webSocket)
    {
        try
        {
            var buffer = new byte[1024];
            while (webSocket.State == WebSocketState.Open)
            {
                // Receive message from client
                var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                if (result.MessageType == WebSocketMessageType.Text)
                {
                    var message = System.Text.Encoding.UTF8.GetString(buffer, 0, result.Count);
                    if (message == "ping")
                    {
                        // Broadcast "ping" to other clients
                        await BroadcastAsync("ping", webSocket);
                    }
                }
                else if (result.MessageType == WebSocketMessageType.Close)
                {
                    // Handle client disconnection
                    await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
                    lock (lockObj)
                    {
                        clients.Remove(webSocket);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            // Handle unexpected disconnections or errors
            Console.WriteLine($"Exception: {ex.Message}");
            lock (lockObj)
            {
                clients.Remove(webSocket);
            }
        }
    }

    // Broadcast message to all clients except the sender
    private static async Task BroadcastAsync(string message, WebSocket sender)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(message);
        var segment = new ArraySegment<byte>(bytes);

        // Create a copy of the clients list to avoid locking during sends
        List<WebSocket> toSend;
        lock (lockObj)
        {
            toSend = new List<WebSocket>(clients);
        }

        // Send message to all other clients
        foreach (var client in toSend)
        {

                if (client.State == WebSocketState.Open)
                {
                    await client.SendAsync(segment, WebSocketMessageType.Text, true, CancellationToken.None);
                }

        }
    }
}