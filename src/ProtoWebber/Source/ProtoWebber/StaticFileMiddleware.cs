using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net;

namespace ProtoWebber
{
    public class StaticFileMiddleware : IWebServerMiddleware
    {
        private string _rootDirectory;
        private bool _stopProcessing;
        private Action<string> _logger;
        private Predicate<HttpListenerContext> _acceptRequestFunc;

        private IList<string> _indexFiles = new List<string>()
        {
            "index.html",
            "index.htm",
            "Default.html",
            "default.htm"
        };

        private IDictionary<string, string> _mimeTypeMappings = new Dictionary<string, string>(StringComparer.InvariantCultureIgnoreCase)
        {
			// taken from https://developer.mozilla.org/en-US/docs/Web/HTTP/Basics_of_HTTP/MIME_types/Complete_list_of_MIME_types
			{".aac", "audio/aac"},
            {".abw", "application/x-abiword"},
            {".arc", "application/octet-stream"},
            {".avi", "video/x-msvideo"},
            {".azw", "application/vnd.amazon.ebook"},
            {".bin", "application/octet-stream"},
            {".bz", "application/x-bzip"},
            {".bz2", "application/x-bzip2"},
            {".csh", "application/x-csh"},
            {".css", "text/css"},
            {".csv", "text/csv"},
            {".doc", "application/msword"},
            {".eot", "application/vnd.ms-fontobject"},
            {".epub", "application/epub+zip"},
            {".gif", "image/gif"},
            {".htm", "text/html"},
            {".html", "text/html"},
            {".ico", "image/x-icon"},
            {".ics", "text/calendar"},
            {".jar", "application/java-archive"},
            {".jpeg", "image/jpeg"},
            {".jpg", "image/jpeg"},
            {".js", "application/javascript"},
            {".json", "application/json"},
            {".mid", "audio/midi"},
            {".midi", "audio/midi"},
            {".mpeg", "video/mpeg"},
            {".mpkg", "application/vnd.apple.installer+xml"},
            {".odp", "application/vnd.oasis.opendocument.presentation"},
            {".ods", "application/vnd.oasis.opendocument.spreadsheet"},
            {".odt", "application/vnd.oasis.opendocument.text"},
            {".oga", "audio/ogg"},
            {".ogv", "video/ogg"},
            {".ogx", "application/ogg"},
            {".otf", "font/otf"},
            {".png", "image/png"},
            {".pdf", "application/pdf"},
            {".ppt", "application/vnd.ms-powerpoint"},
            {".rar", "application/x-rar-compressed"},
            {".rtf", "application/rtf"},
            {".sh", "application/x-sh"},
            {".svg", "image/svg+xml"},
            {".swf", "application/x-shockwave-flash"},
            {".tar", "application/x-tar"},
            {".tif", "image/tiff"},
            {".tiff", "image/tiff"},
            {".ts", "application/typescript"},
            {".ttf", "font/ttf"},
            {".vsd", "application/vnd.visio"},
            {".wav", "audio/x-wav"},
            {".weba", "audio/webm"},
            {".webm", "video/webm"},
            {".webp", "image/webp"},
            {".woff", "font/woff"},
            {".woff2", "font/woff2"},
            {".xhtml", "application/xhtml+xml"},
            {".xls", "application/vnd.ms-excel"},
            {".xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"},
            {".xml", "application/xml"},
            {".xul", "application/vnd.mozilla.xul+xml"},
            {".zip", "application/zip"},
            {".3gp", "video/3gpp"},
            {".3g2", "video/3gpp2"},
            {".7z", "application/x-7z-compressed"},

			// additional
			{".asf", "video/x-ms-asf"},
            {".asx", "video/x-ms-asf"},
            {".cco", "application/x-cocoa"},
            {".crt", "application/x-x509-ca-cert"},
            {".deb", "application/octet-stream"},
            {".der", "application/x-x509-ca-cert"},
            {".dll", "application/octet-stream"},
            {".dmg", "application/octet-stream"},
            {".ear", "application/java-archive"},
            {".exe", "application/octet-stream"},
            {".flv", "video/x-flv"},
            {".hqx", "application/mac-binhex40"},
            {".htc", "text/x-component"},
            {".img", "application/octet-stream"},
            {".iso", "application/octet-stream"},
            {".jardiff", "application/x-java-archive-diff"},
            {".jng", "image/x-jng"},
            {".jnlp", "application/x-java-jnlp-file"},
            {".mml", "text/mathml"},
            {".mng", "video/x-mng"},
            {".mp3", "audio/mpeg"},
            {".mpg", "video/mpeg"},
            {".msi", "application/octet-stream"},
            {".msm", "application/octet-stream"},
            {".msp", "application/octet-stream"},
            {".pdb", "application/x-pilot"},
            {".pem", "application/x-x509-ca-cert"},
            {".pl", "application/x-perl"},
            {".pm", "application/x-perl"},
            {".prc", "application/x-pilot"},
            {".ra", "audio/x-realaudio"},
            {".rpm", "application/x-redhat-package-manager"},
            {".rss", "text/xml"},
            {".run", "application/x-makeself"},
            {".sea", "application/x-sea"},
            {".shtml", "text/html"},
            {".sit", "application/x-stuffit"},
            {".tcl", "application/x-tcl"},
            {".tk", "application/x-tcl"},
            {".txt", "text/plain"},
            {".war", "application/java-archive"},
            {".wbmp", "image/vnd.wap.wbmp"},
            {".wmv", "video/x-ms-wmv"},
            {".xpi", "application/x-xpinstall"}
        };

        public StaticFileMiddleware(string rootDir)
            : this(rootDir, null, null, null)
        {
        }

        public StaticFileMiddleware(string rootDir, Predicate<HttpListenerContext> acceptRequest)
            : this(rootDir, acceptRequest, null, null)
        {
        }

        public StaticFileMiddleware(string rootDir, string[] mimeRemove, IDictionary<string, string> mimeAdd)
            : this(rootDir, null, mimeRemove, mimeAdd)
        {
        }

        public StaticFileMiddleware(string rootDir, Predicate<HttpListenerContext> acceptRequest, string[] mimeRemove, IDictionary<string, string> mimeAdd)
        {
            if (string.IsNullOrEmpty(rootDir))
                throw new ArgumentNullException("rootDir");

            _rootDirectory = rootDir;

            if (acceptRequest == null)
                _acceptRequestFunc = (ctx => ctx.Request.RawUrl.StartsWith("/assets"));
            else
                _acceptRequestFunc = acceptRequest;

            if (mimeRemove != null)
            {
                foreach (string mime in mimeRemove)
                {
                    if (_mimeTypeMappings.ContainsKey(mime))
                        _mimeTypeMappings.Remove(mime);
                }
            }

            if (mimeAdd != null)
            {
                foreach (string mime in mimeAdd.Keys)
                {
                    if (_mimeTypeMappings.ContainsKey(mime))
                        _mimeTypeMappings[mime] = mimeAdd[mime];
                    else
                        _mimeTypeMappings.Add(mime, mimeAdd[mime]);
                }
            }
        }

        public void Dispose()
        {
            return;
        }

        public virtual bool AcceptRequest(HttpListenerContext context)
        {
            return _acceptRequestFunc(context);
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

        public IDictionary<string, string> MimeTypes
        {
            get { return _mimeTypeMappings; }
        }

        public IList<string> IndexFiles
        {
            get { return _indexFiles; }
        }

        public virtual void ProcessRequest(HttpListenerContext context)
        {
            if (context.Request.IsWebSocketRequest)
                return;

            try
            {
                Log(string.Format("[STATIC-REQ] {0}", context.Request.RawUrl));

                WebResponseData rdata = ProcessFileRequest(context.Request);
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
            string filename = request.Url.AbsolutePath.Substring(1);
            // prevents wwwroot\assets\assets/foo.txt
            // #todo this is a vulnerability
            filename = Path.Combine(Path.GetDirectoryName(_rootDirectory), filename);

            if (!File.Exists(filename))
            {
                if (Directory.Exists(filename))
                {
                    foreach (string indexFile in _indexFiles)
                    {
                        if (File.Exists(Path.Combine(filename, indexFile)))
                        {
                            filename = Path.Combine(filename, indexFile);
                            break;
                        }
                    }
                }
            }

            if (!File.Exists(filename))
            {
                Log(string.Format("[STATIC-404] {0}", filename));

                return new WebResponseData()
                {
                    StatusCode = (int)HttpStatusCode.NotFound
                };
            }

            Stream input = new FileStream(filename, FileMode.Open);

            string mime;
            string mimeType = _mimeTypeMappings.TryGetValue(Path.GetExtension(filename), out mime) ? mime : "application/octet-stream";

            Hashtable headers = new Hashtable()
            {
                { "Date",  DateTime.Now.ToString("r") },
                { "Last-Modified", File.GetLastWriteTime(filename).ToString("r") }
            };

            Log(string.Format("[STATIC] {0}", filename));

            return new WebResponseData()
            {
                StatusCode = (int)HttpStatusCode.OK,
                MimeType = mimeType,
                Headers = headers,
                Stream = input
            };
        }
    }
}
