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
        private static ProgramOptions _programOptions;

		static void Main(string[] args)
		{
            _programOptions = new ProgramOptions(args);

            if (_programOptions.ContinueRunning)
            {
                if (_programOptions.RootDirectory == null)
                    _programOptions.RootDirectory = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");

                if (_programOptions.PrefixBinding == null)
                    _programOptions.PrefixBinding = new string[] { "http://localhost:8080/" };

                if (_programOptions.ServerSideScriptDirectoryName == null)
                    _programOptions.ServerSideScriptDirectoryName = Path.Combine(_programOptions.RootDirectory, "server");

                Predicate<HttpListenerContext> staticFileMiddlewareAcceptRequest = null;
                Predicate<HttpListenerContext> javascriptServerMiddlewareAcceptRequest = null;

                if ((_programOptions.AssetDirectoryName == null) ||
                    (_programOptions.AssetDirectoryName.Length == 0))
                {
                    _programOptions.AssetDirectoryName = new string[] { Path.Combine(_programOptions.RootDirectory, "assets") };
                    staticFileMiddlewareAcceptRequest = (ctx => ctx.Request.RawUrl.StartsWith("/assets"));
                    javascriptServerMiddlewareAcceptRequest = (ctx => !ctx.Request.RawUrl.StartsWith("/assets"));
                    Console.WriteLine("[VERBOSE] Everything under 'assets' folder will be served as static files.");
                }
                else
                {
                    List<string> assetDirRegexItems = new List<string>();
                    List<string> assetDirFullPathItems = new List<string>();
                    foreach (string assetDirItem in _programOptions.AssetDirectoryName)
                    {
                        assetDirRegexItems.Add(Regex.Escape(assetDirItem));
                        assetDirFullPathItems.Add(Path.Combine(_programOptions.RootDirectory, assetDirItem));
                    }
                    string assetDirRegex = "^/(" + string.Join("|", assetDirRegexItems.ToArray()) + ").*$";
                    Console.WriteLine(string.Format("[VERBOSE] Static asset pages will be matched with this regex: {0}", assetDirRegex));
                    _programOptions.AssetDirectoryName = assetDirFullPathItems.ToArray();

                    staticFileMiddlewareAcceptRequest = (ctx => Regex.IsMatch(ctx.Request.RawUrl, assetDirRegex));
                    javascriptServerMiddlewareAcceptRequest = (ctx => !Regex.IsMatch(ctx.Request.RawUrl, assetDirRegex));
                }

                WebServer ws;
                if (!_programOptions.ServerSideScript)
                {
                    ws = new WebServer(_programOptions.PrefixBinding, new List<IWebServerMiddleware>()
                    {
                        new StaticFileMiddleware(_programOptions.AssetDirectoryName, staticFileMiddlewareAcceptRequest, _programOptions.RemovedMimeTypes, _programOptions.AddedMimeTypes)
                    });
                }
                else
                {
                    ws = new WebServer(_programOptions.PrefixBinding, new List<IWebServerMiddleware>()
                    {
                        new StaticFileMiddleware(_programOptions.AssetDirectoryName, staticFileMiddlewareAcceptRequest, _programOptions.RemovedMimeTypes, _programOptions.AddedMimeTypes),
                        new JavascriptServerMiddleware(_programOptions.ServerSideScriptDirectoryName, javascriptServerMiddlewareAcceptRequest, _programOptions.TransverseExecution, _programOptions.Websocket)
                    });
                }

                Console.WriteLine("Press any key to stop the server...");

                ws.Start();
                Console.ReadKey();
                ws.Stop();
            }
            else
            {
                if (_programOptions.ArgumentError)
                    Console.WriteLine(_programOptions.ErrorMessage);

                switch (_programOptions.HelpInfo)
                {
                    case ApplicationHelpCategory.Version:
                        PrintVersion();
                        break;

                    case ApplicationHelpCategory.MimeTypes:
                        PrintMimeTypes();
                        break;

                    default:
                        PrintHelp();
                        break;
                }
            }
		}

        private static void PrintHeader()
        {
            Console.WriteLine("TestWebber");
            Console.WriteLine("A quick and simple web server for testing and development use.");
            Console.WriteLine("Copyright (c) 2017 Macks L. All rights reserved.");
            Console.WriteLine();
            Console.WriteLine();
        }

        private static void PrintMimeTypes()
		{
            PrintHeader();

			Console.WriteLine("Embedded mimetypes");
			Console.WriteLine("==================");

            var mimeTypes = _programOptions.DefaultMimeTypes;
            foreach (string ext in mimeTypes.Keys)
			{
				Console.WriteLine(string.Format("{0}\t{1}", ext, mimeTypes[ext]));
			}
		}

		private static void PrintVersion()
		{
			Console.WriteLine(_programOptions.ApplicationVersion);
		}

		private static void PrintHelp()
		{
            PrintHeader();

            Console.WriteLine("testwebber [-d path] [-i hostname] [-!ss | -s server] [-t] [-k] [-a asset] [-m extension:mimetype] [-v]");
			Console.WriteLine("testwebber --show-mimetypes");
			Console.WriteLine("testwebber -ver");
			Console.WriteLine("testwebber -h");
			Console.WriteLine();
			Console.WriteLine(@"-d|--wwwroot      Path to files and scripts where the web server will serve files from");
			Console.WriteLine(@"                  Default is <CurrentDir>\wwwroot");
			Console.WriteLine(@"-i|--hostname     Host name to listen. May be specified more than once.");
			Console.WriteLine(@"                  Default is http://localhost:8080/");
            Console.WriteLine(@"                  To serve externally, runas admin and specify http://*:8080/");
            Console.WriteLine(@"-k|--websocket    Enables websocket feature.");
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
            Console.WriteLine("# Allows everybody to connect to your webserver");
            Console.WriteLine("# You need to run testwebber as admin");
            Console.WriteLine("testwebber -i http://*:8080/");
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
	}
}
