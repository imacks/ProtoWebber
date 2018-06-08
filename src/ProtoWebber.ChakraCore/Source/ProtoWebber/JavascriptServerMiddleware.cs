using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Net.WebSockets;
using System.Threading;
using Chakra.Hosting;
using System.Threading.Tasks;

namespace ProtoWebber
{
    public class JavascriptServerMiddleware : IWebServerMiddleware
    {
        private string _rootDirectory;
        private bool _stopProcessing;
        private Action<string> _logger;
        private Predicate<HttpListenerContext> _acceptRequestFunc;
        private bool _isDisposing = false;
        private bool _enableDirectoryTransversalExecution = false;
        private bool _enableWebSocket = false;
        private int _websocketConnectionCount = 0;
        private WebSocketConnectionManager _webSocketClients = new WebSocketConnectionManager();

        private JavaScriptRuntime _jsRuntime;
        private JavaScriptSourceContext currentSourceContext = JavaScriptSourceContext.FromIntPtr(IntPtr.Zero);

        // We have to hold on to the delegates on the managed side of things so that the
        // delegates aren't collected while the script is running.
        private readonly JavaScriptNativeFunction echoDelegate;
        private readonly JavaScriptNativeFunction runScriptDelegate;
        private readonly JavaScriptNativeFunction readFileDelegate;
        private readonly JavaScriptNativeFunction writeFileDelegate;
        private readonly JavaScriptNativeFunction getWebSocketClientIdDelegate;
        private readonly JavaScriptNativeFunction pushWebSocketMessageDelegate;

        private IList<string> _serverSideJavascriptExtension = new List<string>()
        {
            ".js",
            ".jsx"
        };

        private string _serverSideJavascriptDefaultPage = "index.js";

        public JavascriptServerMiddleware(string rootDir)
            : this(rootDir, null, false, false, false)
        {
        }

        public JavascriptServerMiddleware(string rootDir, Func<HttpListenerContext, bool> acceptRequest)
            : this(rootDir, new Predicate<HttpListenerContext>(acceptRequest), false, false, true)
        {
        }

        public JavascriptServerMiddleware(string rootDir, Predicate<HttpListenerContext> acceptRequest)
            : this(rootDir, acceptRequest, false, false, false)
        {
        }

        public JavascriptServerMiddleware(string rootDir, Func<HttpListenerContext, bool> acceptRequest, bool transversalExecution)
            : this(rootDir, new Predicate<HttpListenerContext>(acceptRequest), transversalExecution, false, true)
        {
        }

        public JavascriptServerMiddleware(string rootDir, Predicate<HttpListenerContext> acceptRequest, bool transversalExecution)
            : this(rootDir, acceptRequest, transversalExecution, false, false)
        {
        }

        public JavascriptServerMiddleware(string rootDir, Func<HttpListenerContext, bool> acceptRequest, bool transversalExecution, bool enableWebsocket)
            : this(rootDir, new Predicate<HttpListenerContext>(acceptRequest), transversalExecution, enableWebsocket, true)
        {
        }

        public JavascriptServerMiddleware(string rootDir, Predicate<HttpListenerContext> acceptRequest, bool transversalExecution, bool enableWebsocket)
            : this(rootDir, acceptRequest, transversalExecution, enableWebsocket, false)
        {
        }

        private JavascriptServerMiddleware(string rootDir, Predicate<HttpListenerContext> acceptRequest, bool transversalExecution, bool enableWebSocket, bool predicateCompat)
        {
            if (string.IsNullOrEmpty(rootDir))
                throw new ArgumentNullException("rootDir");

            _rootDirectory = rootDir;
            _enableDirectoryTransversalExecution = transversalExecution;
            _enableWebSocket = enableWebSocket;

            if (acceptRequest == null)
                _acceptRequestFunc = (ctx => !ctx.Request.RawUrl.StartsWith("/assets"));
            else
                _acceptRequestFunc = acceptRequest;

            echoDelegate = JSEcho;
            runScriptDelegate = JSRunScript;
            writeFileDelegate = JSWriteFile;
            readFileDelegate = JSReadFile;
            getWebSocketClientIdDelegate = JSGetPushClients;
            pushWebSocketMessageDelegate = JSPushMessage;

            // initialize javascript runtime
            // Create the runtime. We're only going to use one runtime for this host.
            _jsRuntime = JavaScriptRuntime.Create();
        }

        public void Dispose()
        {
            if (_isDisposing == false)
            {
                _jsRuntime.Dispose();
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

        public string RootDirectory
        {
            get { return _rootDirectory; }
            set { _rootDirectory = value; }
        }

        public bool EnableDirectoryTransversalExecution
        {
            get { return _enableDirectoryTransversalExecution; }
            set { _enableDirectoryTransversalExecution = value; }
        }

        public bool EnableWebSocket
        {
            get { return _enableWebSocket; }
            set { _enableWebSocket = value; }
        }

        public WebSocketConnectionManager WebSocketClients
        {
            get { return _webSocketClients; }
        }

        public string JavascriptDefaultPage
        {
            get { return _serverSideJavascriptDefaultPage; }
            set { _serverSideJavascriptDefaultPage = value; }
        }

        public IList<string> JavascriptFileExtension
        {
            get { return _serverSideJavascriptExtension; }
        }

        public virtual bool AcceptRequest(HttpListenerContext context)
        {
            return _acceptRequestFunc(context);
        }

        public virtual void ProcessRequest(HttpListenerContext context)
        {
            if (context.Request.IsWebSocketRequest)
            {
                if (this.EnableWebSocket)
                    ProcessWebSocketRequest(context);
                    
                return;
            }

            try
            {
                Log(string.Format("[CHAKRA-REQ] {0}", context.Request.RawUrl));

                // process the request
                WebResponseData rdata = ProcessFileRequest(context.Request);

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

        protected virtual WebResponseData ProcessFileRequest(HttpListenerRequest request)
        {
            string filename = null;
            if (_enableDirectoryTransversalExecution == false)
            {
                filename = Path.Combine(this.RootDirectory, this.JavascriptDefaultPage);                
            }
            else
            {
                // remove leading /
                filename = request.Url.AbsolutePath.Substring(1);
                bool requestSpecifiedExtension = false;

                // checks if url contains .js or .jsx
                foreach (string jsExtension in this.JavascriptFileExtension)
                {
                    if (filename.EndsWith(jsExtension))
                    {
                        requestSpecifiedExtension = true;
                        break;
                    }
                }

                // url does not contain .js or .jsx, append it if file exists (first come basis).
                if (requestSpecifiedExtension == false)
                {
                    foreach (string jsExtension in this.JavascriptFileExtension)
                    {
                        if (File.Exists(Path.Combine(this.RootDirectory, filename + jsExtension)))
                        {
                            filename = Path.Combine(this.RootDirectory, filename + jsExtension);
                            break;
                        }
                    }
                }
                else
                {
                    filename = Path.Combine(this.RootDirectory, filename);
                }

                // file not found, fallback to index.js
                if (!File.Exists(filename))
                {
                    Log(string.Format("[CHAKRA-REDIR] {0} -> {1}", filename, this.JavascriptDefaultPage));
                    filename = Path.Combine(this.RootDirectory, this.JavascriptDefaultPage);
                }
            }

            // index.js not found
            if (!File.Exists(filename))
            {
                Log(string.Format("[CHAKRA-404] {0}", filename));

                return new WebResponseData()
                {
                    StatusCode = (int)HttpStatusCode.NotFound
                };
            }

            try
            {
                // Similarly, create a single execution context. Note that we're putting it on the stack here,
                // so it will stay alive through the entire run.
                JavaScriptContext context = CreateHostContext(_jsRuntime, request);

                // Now set the execution context as being the current one on this thread.
                using (new JavaScriptContext.Scope(context))
                {
                    // Load the script from the disk.
                    string script = File.ReadAllText(filename);

                    // Run the script.
                    JavaScriptValue result;
                    try
                    {
                        result = JavaScriptContext.RunScript(script, currentSourceContext++, filename);
                    }
                    catch (JavaScriptScriptException e)
                    {
                        PrintScriptException(e.Error);
                        return new WebResponseData()
                        {
                            StatusCode = (int)HttpStatusCode.InternalServerError
                        };
                    }
                    catch (Exception e)
                    {
                        Log(string.Format("[CHAKRA-500] failed to run script: {0}", e.Message));

                        return new WebResponseData()
                        {
                            StatusCode = (int)HttpStatusCode.InternalServerError
                        };
                    }

                    // Convert the return value.

                    string mimeType = result.HasProperty(JavaScriptPropertyId.FromString("mimeType"))
                        ? result.GetProperty(JavaScriptPropertyId.FromString("mimeType")).ToString()
                        : "application/octet-stream";

                    int statusCode = result.HasProperty(JavaScriptPropertyId.FromString("statusCode"))
                        ? result.GetProperty(JavaScriptPropertyId.FromString("statusCode")).ToInt32()
                        : (int)HttpStatusCode.OK;

                    Hashtable headers = null;
                    if (result.HasProperty(JavaScriptPropertyId.FromString("headers")))
                    {
                        // todo
                    }
                    else
                    {
                        headers = new Hashtable()
                        {
                            { "Date",  DateTime.Now.ToString("r") },
                            { "X-Powered-By", "ProtoWebber" }
                        };
                    }

                    MemoryStream output = null;
                    if (result.HasProperty(JavaScriptPropertyId.FromString("body")))
                    {
                        // string implementation
                        string outputStringValue = result.GetProperty(JavaScriptPropertyId.FromString("body")).ToString();
                        output = new MemoryStream(Encoding.UTF8.GetBytes(outputStringValue ?? string.Empty));
                    }
 
                    return new WebResponseData()
                    {
                        StatusCode = statusCode,
                        MimeType = mimeType,
                        Headers = headers,
                        Stream = output
                    };
                }
            }
            catch (Exception ex)
            {
                Log(string.Format("[CHAKRA-ERR] {0} {1}", filename, ex.Message));
            }

            return new WebResponseData()
            {
                StatusCode = (int)HttpStatusCode.InternalServerError
            };
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
            string webSocketId = WebSocketClients.AddSocket(webSocket);

            try
            {
                Log("[VERBOSE] Websocket is receiving");

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

                        Log("[VERBOSE] Websocket graceful close");
                        await WebSocketClients.RemoveSocket(WebSocketClients.GetId(webSocket));
                        //await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, string.Empty, CancellationToken.None);
                    }
                    else if (receiveResult.MessageType == WebSocketMessageType.Text)
                    {
                        // We received text!

                        if (msText == null)
                        {
                            Log("[VERBOSE] Websocket text frame");
                            msText = new MemoryStream();
                        }
                        else
                        {
                            Log("[VERBOSE] Websocket text frame (append)");
                        }

                        msText.Write(receiveBuffer.Array, receiveBuffer.Offset, receiveResult.Count);

                        if (receiveResult.EndOfMessage)
                        {
                            msText.Seek(0, SeekOrigin.Begin);
                            using (var reader = new StreamReader(msText, Encoding.UTF8))
                            {
                                string receiveText = reader.ReadToEnd();
                                string sendText = ProcessWebSocketTextRequest(receiveText);
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
                            Log("[VERBOSE] Websocket bin frame");
                            msBin = new MemoryStream();
                        }
                        else
                        {
                            Log("[VERBOSE] Websocket bin frame (append)");
                        }

                        msBin.Write(receiveBuffer.Array, receiveBuffer.Offset, receiveResult.Count);

                        if (receiveResult.EndOfMessage)
                        {
                            msBin.Seek(0, SeekOrigin.Begin);
                            Stream sendStream = ProcessWebSocketBinaryRequest(msBin);
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
                Log(string.Format("[ERROR] {0}", ex.Message));
            }
            finally
            {
                // Clean up by disposing the WebSocket once it is closed/aborted.
                if (webSocket != null)
                {
                    Log("[VERBOSE] Websocket is being disposed");
                    webSocket.Dispose();
                }
            }
        }

        protected virtual string ProcessWebSocketTextRequest(string text)
        {
            string filename = null;
            foreach (string extension in this.JavascriptFileExtension)
            {
                filename = Path.Combine(this.RootDirectory, "websocket.js");
                if (File.Exists(filename))
                    break;
            }

            if (!File.Exists(filename))
                return "[ERROR 500] Unable to find websocket.js";

            try
            {
                JavaScriptContext context = CreateHostWebsocketContext(_jsRuntime, text, null, false);

                using (new JavaScriptContext.Scope(context))
                {
                    string script = File.ReadAllText(filename);
                    JavaScriptValue result;
                    try
                    {
                        result = JavaScriptContext.RunScript(script, currentSourceContext++, filename);
                    }
                    catch (JavaScriptScriptException e)
                    {
                        PrintScriptException(e.Error);
                        return string.Format("[ERROR {0}]", HttpStatusCode.InternalServerError);
                    }
                    catch (Exception e)
                    {
                        Log(string.Format("[CHAKRA-500] failed to run script: {0}", e.Message));
                        return string.Format("[ERROR {0}]", HttpStatusCode.InternalServerError);
                    }

                    return result.ToString();
                }
            }
            catch (Exception e)
            {
                Log(string.Format("[CHAKRA-ERR] {0} {1}", filename, e.Message));
            }

            return string.Format("[ERROR {0}]", HttpStatusCode.InternalServerError);
        }

        protected virtual Stream ProcessWebSocketBinaryRequest(Stream input)
        {
            // not implemented yet #todo
            return input;
        }





        // --- [Javascripting ] ---

        private JavaScriptValue JSPushMessage(JavaScriptValue callee, bool isConstructCall, JavaScriptValue[] arguments, ushort argumentCount, IntPtr callbackData)
        {
            if (arguments.Length < 3)
            {
                JSThrowException("not enough arguments");
                return JavaScriptValue.Invalid;
            }

            string socketId = arguments[1].ToString();
            string message = arguments[2].ToString();

            // #todo make this async?
            WebSocketClients.SendMessageAsync(socketId, message);

            return JavaScriptValue.Invalid;
        }

        private JavaScriptValue JSGetPushClients(JavaScriptValue callee, bool isConstructCall, JavaScriptValue[] arguments, ushort argumentCount, IntPtr callbackData)
        {
            List<string> clientIds = new List<string>(this.WebSocketClients.GetAll().Keys);
            JavaScriptValue outValue = JavaScriptValue.CreateArray((uint)clientIds.Count);

            for (int i = 0; i < clientIds.Count; i++)
            {
                outValue.SetIndexedProperty(JavaScriptValue.FromInt32(i), JavaScriptValue.FromString(clientIds[i]));
            }

            return outValue;
        }

        private void JSThrowException(string errorString)
        {
            // We ignore error since we're already in an error state.
            JavaScriptValue errorValue = JavaScriptValue.FromString(errorString);
            JavaScriptValue errorObject = JavaScriptValue.CreateError(errorValue);
            JavaScriptContext.SetException(errorObject);
        }

        private JavaScriptValue JSReadFile(JavaScriptValue callee, bool isConstructCall, JavaScriptValue[] arguments, ushort argumentCount, IntPtr callbackData)
        {
            if (argumentCount < 2)
            {
                JSThrowException("not enough arguments");
                return JavaScriptValue.Invalid;
            }

            // Convert filename.
            string filename = arguments[1].ToString();

            // Load the file from the disk.
            try
            {
                string fileText = File.ReadAllText(filename);
                return JavaScriptValue.FromString(fileText);
            }
            catch (Exception ex)
            {
                JSThrowException(ex.Message);
                return JavaScriptValue.Invalid;
            }
        }

        private JavaScriptValue JSWriteFile(JavaScriptValue callee, bool isConstructCall, JavaScriptValue[] arguments, ushort argumentCount, IntPtr callbackData)
        {
            if (argumentCount < 3)
            {
                JSThrowException("not enough arguments");
                return JavaScriptValue.Invalid;
            }

            // Convert filename and content.
            string filename = arguments[1].ToString();
            string fileText = arguments[2].ToString();

            // Load the file from the disk.
            try
            {
                File.WriteAllText(filename, fileText);
                return JavaScriptValue.Invalid;
            }
            catch (Exception ex)
            {
                JSThrowException(ex.Message);
                return JavaScriptValue.Invalid;
            }
        }

        private JavaScriptValue JSEcho(JavaScriptValue callee, bool isConstructCall, JavaScriptValue[] arguments, ushort argumentCount, IntPtr callbackData)
        {
            for (uint index = 1; index < argumentCount; index++)
            {
                if (index > 1)
                    Console.Write(" ");

                Console.Write(arguments[index].ConvertToString().ToString());
            }

            Console.WriteLine();

            return JavaScriptValue.Invalid;
        }

        private JavaScriptValue JSRunScript(JavaScriptValue callee, bool isConstructCall, JavaScriptValue[] arguments, ushort argumentCount, IntPtr callbackData)
        {
            if (argumentCount < 2)
            {
                JSThrowException("not enough arguments");
                return JavaScriptValue.Invalid;
            }

            // Convert filename.
            string filename = arguments[1].ToString();

            // Load the script from the disk.
            string script = File.ReadAllText(filename);
            if (string.IsNullOrEmpty(script))
            {
                JSThrowException("invalid script");
                return JavaScriptValue.Invalid;
            }

            // Run the script.
            return JavaScriptContext.RunScript(script, currentSourceContext++, filename);
        }

        private void DefineHostCallback(JavaScriptValue globalObject, string callbackName, JavaScriptNativeFunction callback, IntPtr callbackData)
        {
            // Get property ID.
            JavaScriptPropertyId propertyId = JavaScriptPropertyId.FromString(callbackName);

            // Create a function
            JavaScriptValue function = JavaScriptValue.CreateFunction(callback, callbackData);

            // Set the property
            globalObject.SetProperty(propertyId, function, true);
        }

        private JavaScriptContext CreateHostWebsocketContext(JavaScriptRuntime runtime, string text, Stream binaryStream, bool isStream)
        {
            // Create the context. Note that if we had wanted to start debugging from the very
            // beginning, we would have called JsStartDebugging right after context is created.
            JavaScriptContext context = runtime.CreateContext();

            // Now set the execution context as being the current one on this thread.
            using (new JavaScriptContext.Scope(context))
            {
                // Create the host object the script will use.
                JavaScriptValue hostObject = JavaScriptValue.CreateObject();

                // Get the global object
                JavaScriptValue globalObject = JavaScriptValue.GlobalObject;

                // Get the name of the property ("host") that we're going to set on the global object.
                JavaScriptPropertyId hostPropertyId = JavaScriptPropertyId.FromString("host");

                // Set the property.
                globalObject.SetProperty(hostPropertyId, hostObject, true);

                // Now create the host callbacks that we're going to expose to the script.
                DefineHostCallback(hostObject, "echo", echoDelegate, IntPtr.Zero);
                DefineHostCallback(hostObject, "runScript", runScriptDelegate, IntPtr.Zero);
                DefineHostCallback(hostObject, "readFile", readFileDelegate, IntPtr.Zero);
                DefineHostCallback(hostObject, "writeFile", writeFileDelegate, IntPtr.Zero);
                DefineHostCallback(hostObject, "websocketClients", getWebSocketClientIdDelegate, IntPtr.Zero);
                DefineHostCallback(hostObject, "websocketPush", pushWebSocketMessageDelegate, IntPtr.Zero);

                JavaScriptValue requestParams = JavaScriptValue.CreateObject();
                if (isStream)
                {
                    requestParams.SetProperty(JavaScriptPropertyId.FromString("blob"), JavaScriptValue.FromString(text), true);
                    // not implemented yet #todo
                }
                else
                {
                    requestParams.SetProperty(JavaScriptPropertyId.FromString("text"), JavaScriptValue.FromString(text), true);
                }

                hostObject.SetProperty(JavaScriptPropertyId.FromString("request"), requestParams, true);
            }

            return context;
        }

        private JavaScriptContext CreateHostContext(JavaScriptRuntime runtime, HttpListenerRequest request)
        {
            // Create the context. Note that if we had wanted to start debugging from the very
            // beginning, we would have called JsStartDebugging right after context is created.
            JavaScriptContext context = runtime.CreateContext();

            // Now set the execution context as being the current one on this thread.
            using (new JavaScriptContext.Scope(context))
            {
                // Create the host object the script will use.
                JavaScriptValue hostObject = JavaScriptValue.CreateObject();

                // Get the global object
                JavaScriptValue globalObject = JavaScriptValue.GlobalObject;

                // Get the name of the property ("host") that we're going to set on the global object.
                JavaScriptPropertyId hostPropertyId = JavaScriptPropertyId.FromString("host");

                // Set the property.
                globalObject.SetProperty(hostPropertyId, hostObject, true);

                // Now create the host callbacks that we're going to expose to the script.
                DefineHostCallback(hostObject, "echo", echoDelegate, IntPtr.Zero);
                DefineHostCallback(hostObject, "runScript", runScriptDelegate, IntPtr.Zero);
                DefineHostCallback(hostObject, "readFile", readFileDelegate, IntPtr.Zero);
                DefineHostCallback(hostObject, "writeFile", writeFileDelegate, IntPtr.Zero);

                // Create an object for request.
                JavaScriptValue requestParams = JavaScriptValue.CreateObject();

                if (request.RawUrl != null)
                    requestParams.SetProperty(JavaScriptPropertyId.FromString("rawUrl"), JavaScriptValue.FromString(request.RawUrl), true);
                if (request.UserAgent != null)
                    requestParams.SetProperty(JavaScriptPropertyId.FromString("userAgent"), JavaScriptValue.FromString(request.UserAgent), true);
                if (request.UserHostName != null)
                    requestParams.SetProperty(JavaScriptPropertyId.FromString("userHostName"), JavaScriptValue.FromString(request.UserHostName), true);
                if (request.UserHostAddress != null)
                    requestParams.SetProperty(JavaScriptPropertyId.FromString("userHostAddress"), JavaScriptValue.FromString(request.UserHostAddress), true);
                if (request.ServiceName != null)
                    requestParams.SetProperty(JavaScriptPropertyId.FromString("serviceName"), JavaScriptValue.FromString(request.ServiceName), true);
                if (request.HttpMethod != null)
                    requestParams.SetProperty(JavaScriptPropertyId.FromString("httpMethod"), JavaScriptValue.FromString(request.HttpMethod), true);
                if (request.ContentType != null)
                    requestParams.SetProperty(JavaScriptPropertyId.FromString("contentType"), JavaScriptValue.FromString(request.ContentType), true);
                if (request.ContentEncoding != null)
                    requestParams.SetProperty(JavaScriptPropertyId.FromString("contentEncoding"), JavaScriptValue.FromString(request.ContentEncoding.WebName), true);

                requestParams.SetProperty(JavaScriptPropertyId.FromString("keepAlive"), JavaScriptValue.FromBoolean(request.KeepAlive), true);
                requestParams.SetProperty(JavaScriptPropertyId.FromString("isWebSocketRequest"), JavaScriptValue.FromBoolean(request.IsWebSocketRequest), true);
                requestParams.SetProperty(JavaScriptPropertyId.FromString("isSecureConnection"), JavaScriptValue.FromBoolean(request.IsSecureConnection), true);
                requestParams.SetProperty(JavaScriptPropertyId.FromString("isLocal"), JavaScriptValue.FromBoolean(request.IsLocal), true);
                requestParams.SetProperty(JavaScriptPropertyId.FromString("isAuthenticated"), JavaScriptValue.FromBoolean(request.IsAuthenticated), true);
                requestParams.SetProperty(JavaScriptPropertyId.FromString("hasEntityBody"), JavaScriptValue.FromBoolean(request.HasEntityBody), true);
                requestParams.SetProperty(JavaScriptPropertyId.FromString("contentLength64"), JavaScriptValue.FromDouble(request.ContentLength64), true);

                // need to call begingetclientcertificate
                //requestParams.SetProperty(JavaScriptPropertyId.FromString("clientCertificateError"), JavaScriptValue.FromInt32(request.ClientCertificateError), true);

                if (request.UrlReferrer != null)
                    requestParams.SetProperty(JavaScriptPropertyId.FromString("urlReferrer"), JavaScriptValue.FromString(request.UrlReferrer.ToString()), true);
                if (request.RequestTraceIdentifier != null)
                    requestParams.SetProperty(JavaScriptPropertyId.FromString("requestTraceIdentifier"), JavaScriptValue.FromString(request.RequestTraceIdentifier.ToString()), true);
                if (request.RemoteEndPoint != null)
                    requestParams.SetProperty(JavaScriptPropertyId.FromString("remoteEndPoint"), JavaScriptValue.FromString(request.RemoteEndPoint.ToString()), true);
                if (request.ProtocolVersion != null)
                    requestParams.SetProperty(JavaScriptPropertyId.FromString("protocolVersion"), JavaScriptValue.FromString(request.ProtocolVersion.ToString()), true);
                if (request.LocalEndPoint != null)
                    requestParams.SetProperty(JavaScriptPropertyId.FromString("localEndPoint"), JavaScriptValue.FromString(request.LocalEndPoint.ToString()), true);

                if (request.UserLanguages != null)
                {
                    JavaScriptValue userLanguages = JavaScriptValue.CreateArray((uint)request.UserLanguages.Length);
                    for (int i = 0; i < request.UserLanguages.Length; i++)
                    {
                        userLanguages.SetIndexedProperty(JavaScriptValue.FromInt32(i), JavaScriptValue.FromString(request.UserLanguages[i]));
                    }
                    requestParams.SetProperty(JavaScriptPropertyId.FromString("userLanguages"), userLanguages, true);
                }

                if (request.AcceptTypes != null)
                {
                    JavaScriptValue acceptTypes = JavaScriptValue.CreateArray((uint)request.AcceptTypes.Length);
                    for (int i = 0; i < request.AcceptTypes.Length; i++)
                    {
                        acceptTypes.SetIndexedProperty(JavaScriptValue.FromInt32(i), JavaScriptValue.FromString(request.AcceptTypes[i]));
                    }
                    requestParams.SetProperty(JavaScriptPropertyId.FromString("acceptTypes"), acceptTypes, true);
                }

                if (request.QueryString != null)
                {
                    JavaScriptValue queryString = JavaScriptValue.CreateArray((uint)request.QueryString.Count);
                    for (int i = 0; i < request.QueryString.Count; i++)
                    {
                        JavaScriptValue qsItem = JavaScriptValue.CreateObject();

                        qsItem.SetProperty(JavaScriptPropertyId.FromString("name"), JavaScriptValue.FromString(request.QueryString.GetKey(i) ?? string.Empty), false);
                        qsItem.SetProperty(JavaScriptPropertyId.FromString("value"), JavaScriptValue.FromString(request.QueryString[i] ?? string.Empty), false);

                        queryString.SetIndexedProperty(JavaScriptValue.FromInt32(i), qsItem);
                    }
                    requestParams.SetProperty(JavaScriptPropertyId.FromString("queryString"), queryString, true);
                }

                if (request.Headers != null)
                {
                    JavaScriptValue headers = JavaScriptValue.CreateArray((uint)request.Headers.Count);
                    for (int i = 0; i < request.Headers.Count; i++)
                    {
                        JavaScriptValue headerItem = JavaScriptValue.CreateObject();
                        headerItem.SetProperty(JavaScriptPropertyId.FromString("name"), JavaScriptValue.FromString(request.Headers.GetKey(i) ?? string.Empty), false);
                        headerItem.SetProperty(JavaScriptPropertyId.FromString("value"), JavaScriptValue.FromString(request.Headers[i] ?? string.Empty), false);

                        headers.SetIndexedProperty(JavaScriptValue.FromInt32(i), headerItem);
                    }
                    requestParams.SetProperty(JavaScriptPropertyId.FromString("headers"), headers, true);
                }

                // #todo
                // Stream InputStream
                // CookieCollection Cookies

                hostObject.SetProperty(JavaScriptPropertyId.FromString("request"), requestParams, true);
            }

            return context;
        }

        private void PrintScriptException(JavaScriptValue exception)
        {
            // Get message.
            JavaScriptPropertyId messageName = JavaScriptPropertyId.FromString("message");
            JavaScriptValue messageValue = exception.GetProperty(messageName);
            string message = messageValue.ToString();
            Log(string.Format("chakrahost: exception: {0}", message));
        }
    }
}
