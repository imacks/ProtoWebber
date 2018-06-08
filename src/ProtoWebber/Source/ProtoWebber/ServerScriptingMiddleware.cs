using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Net.WebSockets;
using System.Threading;

namespace ProtoWebber
{
    public class ServerScriptingMiddleware : IWebServerMiddleware
    {
        private bool _stopProcessing;
        private Action<string> _logger;
        private Predicate<HttpListenerContext> _acceptRequestFunc;
        private bool _isDisposing = false;
        private bool _enableWebsocket = false;
        private int _websocketConnectionCount = 0;
        private WebSocketConnectionManager _webSocketClients = new WebSocketConnectionManager();

        private Func<HttpListenerRequest, WebResponseData> _processRequest;
        private Func<string, string, string> _processWebsocketTextRequest;
        private Func<string, Stream, Stream> _processWebsocketBinaryRequest;

        public ServerScriptingMiddleware(Func<HttpListenerContext, bool> acceptRequest, bool enableWebsocket,
            Func<HttpListenerRequest, WebResponseData> processRequest, 
            Func<string, string, string> processWebsocketTextRequest, 
            Func<string, Stream, Stream> processWebsocketBinaryRequest)
            : this(new Predicate<HttpListenerContext>(acceptRequest), enableWebsocket, processRequest, processWebsocketTextRequest, processWebsocketBinaryRequest)
        {
        }

        public ServerScriptingMiddleware(Predicate<HttpListenerContext> acceptRequest, bool enableWebsocket, 
            Func<HttpListenerRequest, WebResponseData> processRequest, 
            Func<string, string, string> processWebsocketTextRequest, 
            Func<string, Stream, Stream> processWebsocketBinaryRequest)
        {
            _enableWebsocket = enableWebsocket;

            if (acceptRequest == null)
                _acceptRequestFunc = (ctx => !ctx.Request.RawUrl.StartsWith("/assets"));
            else
                _acceptRequestFunc = acceptRequest;

            _processRequest = processRequest;
            _processWebsocketTextRequest = processWebsocketTextRequest;
            _processWebsocketBinaryRequest = processWebsocketBinaryRequest;
        }

        public void Dispose()
        {
            if (_isDisposing == false)
            {
                _isDisposing = true;
            }

            return;
        }

        public bool StopProcessing
        {
            get { return _stopProcessing; }
            set { _stopProcessing = value; }
        }

        public Action<string> Log
        {
            get { return _logger; }
            set { _logger = value; }
        }

        public bool EnableWebsocket
        {
            get { return _enableWebsocket; }
            set { _enableWebsocket = value; }
        }

        public WebSocketConnectionManager WebSocketClients
        {
            get { return _webSocketClients; }
        }

        public virtual bool AcceptRequest(HttpListenerContext context)
        {
            return _acceptRequestFunc(context);
        }

        public virtual void ProcessRequest(HttpListenerContext context)
        {
            if (context.Request.IsWebSocketRequest)
            {
                if (_enableWebsocket)
                    ProcessWebSocketRequest(context);
                    
                return;
            }

            try
            {
                Log(string.Format("[DYNAMIC-REQ] {0}", context.Request.RawUrl));

                // process the request
                WebResponseData rdata = _processRequest(context.Request);

                // convert response to stream
                Stream responseData = rdata.Stream;

                if (rdata.MimeType != null)
                    context.Response.ContentType = rdata.MimeType;

                if (rdata.Headers != null)
                {
                    foreach (string header in rdata.Headers.Keys)
                    {
                        context.Response.AddHeader(header, (string)rdata.Headers[header]);
                    }
                }

                if (responseData != null)
                {
                    context.Response.ContentLength64 = responseData.Length;
                    byte[] buffer = new byte[1024 * 16];
                    int nbytes;
                    while ((nbytes = responseData.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        context.Response.OutputStream.Write(buffer, 0, nbytes);
                    }
                    responseData.Close();
                }

                context.Response.StatusCode = rdata.StatusCode;

                context.Response.OutputStream.Flush();
            }
            catch (Exception ex)
            {
                context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
                Log(string.Format("[ERROR] {0}", ex));
            }
            finally
            {
                // always close the stream
                context.Response.OutputStream.Close();
            }
        }

        public virtual async void ProcessWebSocketRequest(HttpListenerContext context)
        {
            WebSocketContext webSocketContext = null;
            try
            {
                // When calling `AcceptWebSocketAsync` the negotiated subprotocol must be specified. For simplicity now we assumes that no subprotocol 
                // was requested. 
                webSocketContext = await context.AcceptWebSocketAsync(null);
                if (_websocketConnectionCount == int.MaxValue)
                {
                    Log("[VERBOSE] Websocket reset connection count");
                    _websocketConnectionCount = 0;
                }
                Interlocked.Increment(ref _websocketConnectionCount);
                Log(string.Format("[VERBOSE] Websocket #{0}", _websocketConnectionCount));
            }
            catch (Exception ex)
            {
                // The upgrade process failed somehow. For simplicity lets assume it was a failure on the part of the server and indicate this using 500.
                context.Response.StatusCode = 500;
                context.Response.Close();
                Log(string.Format("[ERROR] {0}", ex.Message));
                return;
            }

            WebSocket webSocket = webSocketContext.WebSocket;
            string clientId = WebSocketClients.AddSocket(webSocket);

            try
            {
                Log(string.Format("[VERBOSE] Websocket client {0} is receiving", clientId));

                //### Receiving
                // Define a receive buffer to hold data received on the WebSocket connection. The buffer will be reused as we only need to hold on to the data
                // long enough to send it back to the sender.
                ArraySegment<byte> receiveBuffer = new ArraySegment<byte>(new byte[1024]);  // 8192;
                MemoryStream msText = null;
                MemoryStream msBin = null;

                // While the WebSocket connection remains open run a simple loop that receives data and sends it back.
                while (webSocket.State == WebSocketState.Open)
                {
                    // The first step is to begin a receive operation on the WebSocket. `ReceiveAsync` takes two parameters:
                    //
                    // * An `ArraySegment` to write the received data to. 
                    // * A cancellation token. In this example we are not using any timeouts so we use `CancellationToken.None`.
                    //
                    // `ReceiveAsync` returns a `Task<WebSocketReceiveResult>`. The `WebSocketReceiveResult` provides information on the receive operation that was just 
                    // completed, such as:                
                    //
                    // * `WebSocketReceiveResult.MessageType` - What type of data was received and written to the provided buffer. Was it binary, utf8, or a close message?                
                    // * `WebSocketReceiveResult.Count` - How many bytes were read?                
                    // * `WebSocketReceiveResult.EndOfMessage` - Have we finished reading the data for this message or is there more coming?
                    WebSocketReceiveResult receiveResult = await webSocket.ReceiveAsync(receiveBuffer, CancellationToken.None);

                    if (receiveResult.MessageType == WebSocketMessageType.Close)
                    {
                        // The WebSocket protocol defines a close handshake that allows a party to send a close frame when they wish to gracefully shut down the connection.
                        // The party on the other end can complete the close handshake by sending back a close frame.
                        //
                        // If we received a close frame then lets participate in the handshake by sending a close frame back. This is achieved by calling `CloseAsync`. 
                        // `CloseAsync` will also terminate the underlying TCP connection once the close handshake is complete.
                        //
                        // The WebSocket protocol defines different status codes that can be sent as part of a close frame and also allows a close message to be sent. 
                        // If we are just responding to the client's request to close we can just use `WebSocketCloseStatus.NormalClosure` and omit the close message.

                        Log(string.Format("[VERBOSE] Websocket client {0} graceful close", clientId));
                        await WebSocketClients.RemoveSocket(clientId);
                    }
                    else if (receiveResult.MessageType == WebSocketMessageType.Text)
                    {
                        // We received text!

                        if (msText == null)
                        {
                            Log(string.Format("[VERBOSE] Websocket client {0} text frame", clientId));
                            msText = new MemoryStream();
                        }
                        else
                        {
                            Log(string.Format("[VERBOSE] Websocket client {0} text frame append", clientId));
                        }

                        msText.Write(receiveBuffer.Array, receiveBuffer.Offset, receiveResult.Count);

                        if (receiveResult.EndOfMessage)
                        {
                            msText.Seek(0, SeekOrigin.Begin);
                            using (var reader = new StreamReader(msText, Encoding.UTF8))
                            {
                                string receiveText = reader.ReadToEnd();
                                string sendText = _processWebsocketTextRequest(clientId, receiveText);
                                byte[] encoded = Encoding.UTF8.GetBytes(sendText);
                                var sendBuffer = new ArraySegment<byte>(encoded, 0, encoded.Length);
                                await webSocket.SendAsync(sendBuffer, WebSocketMessageType.Text, true, CancellationToken.None);
                            }

                            msText.Close();
                            msText.Dispose();
                            msText = null;
                        }
                    }
                    else
                    {
                        // We received binary data!

                        if (msBin == null)
                        {
                            Log(string.Format("[VERBOSE] Websocket client {0} bin frame", clientId));
                            msBin = new MemoryStream();
                        }
                        else
                        {
                            Log(string.Format("[VERBOSE] Websocket client {0} bin frame append", clientId));
                        }

                        msBin.Write(receiveBuffer.Array, receiveBuffer.Offset, receiveResult.Count);

                        if (receiveResult.EndOfMessage)
                        {
                            msBin.Seek(0, SeekOrigin.Begin);
                            Stream sendStream = _processWebsocketBinaryRequest(clientId, msBin);
                            sendStream.Seek(0, SeekOrigin.Begin);
                            byte[] sendBytes = new byte[sendStream.Length];
                            sendStream.Read(sendBytes, 0, (int)sendStream.Length);
                            var sendBuffer = new ArraySegment<byte>(sendBytes, 0, sendBytes.Length);
                            await webSocket.SendAsync(sendBuffer, WebSocketMessageType.Binary, true, CancellationToken.None);

                            msBin.Close();
                            msBin.Dispose();
                            msBin = null;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log(string.Format("[ERROR] client {0}: {1}", clientId, ex.Message));
            }
            finally
            {
                // Clean up by disposing the WebSocket once it is closed/aborted.
                if (webSocket != null)
                {
                    Log(string.Format("[VERBOSE] Websocket for client {0} is being disposed", clientId));
                    webSocket.Dispose();
                }
            }
        }
    }
}
