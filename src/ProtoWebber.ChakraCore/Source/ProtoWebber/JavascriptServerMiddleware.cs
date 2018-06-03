using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using Chakra.Hosting;
using System.Collections.Specialized;

namespace ProtoWebber
{
    public class JavascriptServerMiddleware : IWebServerMiddleware
    {
        private string _rootDirectory;
        private bool _stopProcessing;
        private Action<string> _logger;
        private Predicate<HttpListenerContext> _acceptRequestFunc;
        private bool _isDisposing = false;

        private JavaScriptRuntime _jsRuntime;
        private JavaScriptSourceContext currentSourceContext = JavaScriptSourceContext.FromIntPtr(IntPtr.Zero);

        // We have to hold on to the delegates on the managed side of things so that the
        // delegates aren't collected while the script is running.
        private readonly JavaScriptNativeFunction echoDelegate;
        private readonly JavaScriptNativeFunction runScriptDelegate;
        private readonly JavaScriptNativeFunction readFileDelegate;
        private readonly JavaScriptNativeFunction writeFileDelegate;

        private IList<string> _serverSideJavascriptExtension = new List<string>()
        {
            ".js",
            ".jsx"
        };

        private string _serverSideJavascriptDefaultPage = "index.js";

        public JavascriptServerMiddleware(string rootDir)
            : this(rootDir, null, false)
        {
        }

        public JavascriptServerMiddleware(string rootDir, Func<HttpListenerContext, bool> acceptRequest)
            : this(rootDir, new Predicate<HttpListenerContext>(acceptRequest), true)
        {
        }

        public JavascriptServerMiddleware(string rootDir, Predicate<HttpListenerContext> acceptRequest)
            : this(rootDir, acceptRequest, false)
        {
        }

        private JavascriptServerMiddleware(string rootDir, Predicate<HttpListenerContext> acceptRequest, bool predicateCompat)
        {
            if (string.IsNullOrEmpty(rootDir))
                throw new ArgumentNullException("rootDir");

            _rootDirectory = rootDir;

            if (acceptRequest == null)
                _acceptRequestFunc = (ctx => !ctx.Request.RawUrl.StartsWith("/assets"));
            else
                _acceptRequestFunc = acceptRequest;

            echoDelegate = JSEcho;
            runScriptDelegate = JSRunScript;
            writeFileDelegate = JSWriteFile;
            readFileDelegate = JSReadFile;

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
            // todo websocket
            if (context.Request.IsWebSocketRequest)
                return;

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
            // remove leading /
            string filename = request.Url.AbsolutePath.Substring(1);
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
                    if (File.Exists(filename + jsExtension))
                    {
                        filename = filename + jsExtension;
                        break;
                    }
                }
            }

            // file not found, fallback to index.js
            if (!File.Exists(filename))
            {
                Log(string.Format("[CHAKRA-REDIR] {0} -> {1}", filename, this.JavascriptDefaultPage));
                filename = Path.Combine(this.RootDirectory, this.JavascriptDefaultPage);
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

        // --- [Javascripting ] ---

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
