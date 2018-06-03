using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Net;
using ProtoWebber;

namespace TestWebber
{
	class Program
	{
		private static string _rootDirectory;
		private static string[] _prefixBinding;
		//private static bool _enableWebSocket = false;
		private static string[] _assetDir;
        private static string _serverScriptDir;
        internal static bool _verboseMode = false;
		private static List<string> _mimeRemove = new List<string>();
		private static Dictionary<string, string> _mimeAdd = new Dictionary<string, string>();
		private static bool _disableServerScript = false;
		private static bool _allowTransverseExecution = false;

		static void Main(string[] args)
		{
			bool continueRun = ProcessArgs(args);

			if (continueRun)
			{
				if (_rootDirectory == null)
					_rootDirectory = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");

				if (_prefixBinding == null)
					_prefixBinding = new string[] { "http://localhost:8080/" };

                if (_serverScriptDir == null)
                    _serverScriptDir = Path.Combine(_rootDirectory, "server");

                Predicate<HttpListenerContext> staticFileMiddlewareAcceptRequest = null;
                Predicate<HttpListenerContext> javascriptServerMiddlewareAcceptRequest = null;

                if ((_assetDir == null) || (_assetDir.Length == 0))
                {
					_assetDir = new string[] { Path.Combine(_rootDirectory, "assets") };
                	staticFileMiddlewareAcceptRequest = (ctx => ctx.Request.RawUrl.StartsWith("/assets"));
                	javascriptServerMiddlewareAcceptRequest = (ctx => !ctx.Request.RawUrl.StartsWith("/assets"));
                	Console.WriteLine("[VERBOSE] Everything under 'assets' folder will be served as static files.");
                }
                else
                {
                	List<string> assetDirRegexItems = new List<string>();
                	List<string> assetDirFullPathItems = new List<string>();
                	foreach (string assetDirItem in _assetDir)
                	{
                		assetDirRegexItems.Add(Regex.Escape(assetDirItem));
                		assetDirFullPathItems.Add(Path.Combine(_rootDirectory, assetDirItem));
                	}
                	string assetDirRegex = "^/(" + string.Join("|", assetDirRegexItems.ToArray()) + ").*$";
                	Console.WriteLine(string.Format("[VERBOSE] Static asset pages will be matched with this regex: {0}", assetDirRegex));
                	_assetDir = assetDirFullPathItems.ToArray();

                	staticFileMiddlewareAcceptRequest = (ctx => Regex.IsMatch(ctx.Request.RawUrl, assetDirRegex));
                	javascriptServerMiddlewareAcceptRequest = (ctx => !Regex.IsMatch(ctx.Request.RawUrl, assetDirRegex));
                }

				WebServer ws;
				if (_disableServerScript)
				{
					ws = new WebServer(_prefixBinding, new List<IWebServerMiddleware>()
					{
						new StaticFileMiddleware(_assetDir, staticFileMiddlewareAcceptRequest, _mimeRemove.ToArray(), _mimeAdd)
					});
				}
				else
				{
					ws = new WebServer(_prefixBinding, new List<IWebServerMiddleware>()
					{
						new StaticFileMiddleware(_assetDir, staticFileMiddlewareAcceptRequest, _mimeRemove.ToArray(), _mimeAdd),
	                    new JavascriptServerMiddleware(_serverScriptDir, javascriptServerMiddlewareAcceptRequest, _allowTransverseExecution)
					});
				}


				//if (!_enableWebSocket)
				//else
				//	ws = new WebSocketServer(_rootDirectory, _prefixBinding);

				// config

				Console.WriteLine("Press any key to stop the server...");

				ws.Start();
				Console.ReadKey();
				ws.Stop();
			}
		}

		private static void PrintMimeTypes()
		{
			Console.WriteLine("TestWebber");
			Console.WriteLine("A quick and simple web server for testing and development use.");
			Console.WriteLine("Copyright (c) 2017 Lizoc Inc. All rights reserved.");
			Console.WriteLine();	
			Console.WriteLine();	
			Console.WriteLine("Embedded mimetypes");
			Console.WriteLine("==================");

			var midware = new StaticFileMiddleware("dummy");
			foreach (string ext in midware.MimeTypes.Keys)
			{
				Console.WriteLine(string.Format("{0}\t{1}", ext, midware.MimeTypes[ext]));
			}
		}

		private static void PrintVersion()
		{
			Console.WriteLine("1.0");
		}

		private static void PrintHelp()
		{
			Console.WriteLine("TestWebber");
			Console.WriteLine("A quick and simple web server for testing and development use.");
			Console.WriteLine("Copyright (c) 2017 Macks L. All rights reserved.");
			Console.WriteLine();	
			Console.WriteLine();	
			//Console.WriteLine("testwebber [-d path] [-h hostname] [-a asset] [-s websocket] [-m extension:mimetype] [-v]");
			Console.WriteLine("testwebber [-d path] [-h hostname] [-!ss | -s server] [-a asset] [-m extension:mimetype] [-v]");
			Console.WriteLine("testwebber --show-mimetypes");
			Console.WriteLine("testwebber -ver");
			Console.WriteLine("testwebber -h");
			Console.WriteLine();
			Console.WriteLine(@"-d|--wwwroot      Path to files and scripts where the web server will serve files from");
			Console.WriteLine(@"                  Default is <CurrentDir>\wwwroot");
			Console.WriteLine(@"-i|--hostname     Host name to listen. May be specified more than once.");
			Console.WriteLine(@"                  Default is http://localhost:8080/");
            //Console.WriteLine(@"-s|--websocket    Enables websocket feature.");
            Console.WriteLine(@"-!ss|--disableserverscript");
            Console.WriteLine(@"                  Disables server side scripting.");
            Console.WriteLine(@"-t|--transverseexecution");
            Console.WriteLine(@"                  Enables transverse execution of server side scripts.");
            Console.WriteLine(@"                  e.g. Run http://localhost:8080/foo[.js] if wwwroot\server\foo.js exists.");
            Console.WriteLine(@"                  Without this, all non-static requests are routed to server\index.js");
            Console.WriteLine(@"-s|--server       Name of server side scripting directory in wwwroot.");
            Console.WriteLine(@"                  Default is 'server'.");
            Console.WriteLine(@"-a|--asset        Name of assets directory in wwwroot. May be specified more than once.");
			Console.WriteLine(@"                  Default is 'assets'.");
			Console.WriteLine(@"-m|--mime         Additional mime types. To remove, -<extension>. May be specified more than once.");
			Console.WriteLine(@"                  To get a list of stock mime type mappings, use: testwebber --show-mimetypes");
			Console.WriteLine(@"--show-mimetypes  Shows a list of stock mimetypes. Not to be used with any other parameters.");
			Console.WriteLine();
			Console.WriteLine("Common arguments:");
			Console.WriteLine("-ver|--version    Shows version number of this application");
			Console.WriteLine("-v|--verbose      Displays verbose information");
			Console.WriteLine("-h|/?|--help      Shows this help screen.");
			Console.WriteLine();
			Console.WriteLine();
			Console.WriteLine("EXAMPLES");
			Console.WriteLine("========");
            Console.WriteLine("# Run a web server on http://localhost:8080.");
            Console.WriteLine("# - If request starts with '/assets':");
            Console.WriteLine("#   - look for the file in (CurrentDir)/wwwroot/assets and serve as static file.");
            Console.WriteLine("#   - if not found, return 404");
            Console.WriteLine("# - Otherwise, webserver will look for files ending in .js or .jsx in (CurrentDir)/wwwroot/server, and execute it on on server side if found.");
            Console.WriteLine("# - No luck? webserver will try to execute (CurrentDir)/wwwroot/server/index.js or (CurrentDir)/wwwroot/server/index.jsx");
            Console.WriteLine("# - If all else fails, webserver will return a 404 error.");
            Console.WriteLine("testwebber");
			Console.WriteLine();
            Console.WriteLine("# Disables server side scripting. Serves everything under (CurrentDir)/wwwroot/assets and (CurrentDir)/wwwroot/pages as static files.");
            Console.WriteLine("testwebber -a assets -a pages -!ss");
			Console.WriteLine();
			Console.WriteLine("# Removes the file extension .tiff mimetype");
			Console.WriteLine("testwebber -m -.tiff");
			Console.WriteLine();
			Console.WriteLine("# Removes the file extension .tiff mimetype; adds custom extension .foo and map to text/csv mimetype.");
			Console.WriteLine("testwebber -m -.tiff -m .foo:text/csv");
			Console.WriteLine();
			Console.WriteLine();
		}

		internal static bool ProcessArgs(string[] args)
		{
			int lastArg = 0;
			List<string> hostPrefix = new List<string>();
			List<string> assetPath = new List<string>();
            string serverScriptPath = null;
            bool disableServerScript = false;
            bool allowTransverseExecution = false;

            for (; lastArg < args.Length; lastArg++)
			{
				if (IsArg(args[lastArg], "v", "verbose"))
				{
					_verboseMode = true;
				}
				else if (IsArg(args[lastArg], "d", "wwwroot"))
				{
					_rootDirectory = args[lastArg + 1];
					lastArg += 1;
				}
				else if (IsArg(args[lastArg], "i", "hostname"))
				{
					if (args.Length <= (lastArg + 1))
					{
						Console.WriteLine("Argument -i/--hostname requires a value.");
						return false;
					}
					hostPrefix.Add(args[lastArg + 1].TrimEnd('/')  + "/");
					lastArg += 1;
				}
                else if (IsArg(args[lastArg], "!ss", "disableserverscript"))
                {
                    disableServerScript = true;
                }
                else if (IsArg(args[lastArg], "t", "transverseexecution"))
                {
                    allowTransverseExecution = true;
                }
                else if (IsArg(args[lastArg], "s", "server"))
                {
                    if (args.Length <= (lastArg + 1))
                    {
                        Console.WriteLine("Argument -s/--server requires a value.");
                        return false;
                    }
                    serverScriptPath = args[lastArg + 1].Trim('/', '\\');
                    lastArg += 1;
                }
                else if (IsArg(args[lastArg], "a", "asset"))
				{
					if (args.Length <= (lastArg + 1))
					{
						Console.WriteLine("Argument -a/--asset requires a value.");
						return false;
					}
					assetPath.Add(args[lastArg + 1].Trim('/', '\\'));
					lastArg += 1;
				}
				//else if (IsArg(args[lastArg], "s", "websocket"))
				//{
				//	_enableWebSocket = true;
				//}
				else if (IsArg(args[lastArg], "m", "mime"))
				{
					if (args.Length <= (lastArg + 1))
					{
						Console.WriteLine("Argument -m/--mime requires a value.");
						return false;
					}
					string mimeValue = args[lastArg + 1];
					if (mimeValue.StartsWith("-"))
					{
						_mimeRemove.Add(mimeValue.Substring(1));
					}
					else
					{
						if (!mimeValue.Contains(":"))
						{
							Console.WriteLine("Invalid syntax. To add a mimetype, use: -m <ext>:<mimetype>");
							Console.WriteLine("EXAMPLE: -m .woff:application/octet-stream");
							return false;
						}

						string fileExt = mimeValue.Split(':')[0];
						if (_mimeAdd.ContainsKey(fileExt))
							_mimeAdd[fileExt] = mimeValue.Substring(fileExt.Length + 1);
						else
							_mimeAdd.Add(fileExt, mimeValue.Substring(fileExt.Length + 1));
					}
					lastArg += 1;
				}
				else if (IsArg(args[lastArg], "show-mimetypes"))
				{
					PrintMimeTypes();
					return false;
				}
				else if (IsArg(args[lastArg], "ver", "version"))
				{
					PrintVersion();
					return false;
				}
				else if (IsArg(args[lastArg], "h", "help") || args[lastArg] == "/?")
				{
					PrintHelp();
					return false;
				}
				else
				{
					PrintHelp();
					return false;
				}
			}

			if (!disableServerScript)
			{
				foreach (string assetPathItem in assetPath)
				{
					if (assetPathItem == serverScriptPath)
					{
						Console.WriteLine("Asset folder name cannot be the same as the server script path name!");
						return false;
					}
				}
			}

			if (hostPrefix.Count > 0)
				_prefixBinding = hostPrefix.ToArray();
			if (assetPath != null)
				_assetDir = assetPath.ToArray();
            if (serverScriptPath != null)
                _serverScriptDir = serverScriptPath;
            if (disableServerScript == true)
            	_disableServerScript = true;
            if (allowTransverseExecution == true)
            	_allowTransverseExecution = true;

			return true;
		}

		private static bool IsArg(string candidate, string longName)
		{
			return IsArg(candidate, shortName: null, longName:longName);
		}

		private static bool IsArg(string candidate, string shortName, string longName)
		{
			return (shortName != null && candidate.Equals("-" + shortName)) || 
				(longName != null && candidate.Equals("--" + longName));
		}
	}
}
