using ProtoWebber;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TestWebber
{
    internal enum ApplicationHelpCategory
    {
        Help = 0,
        Version = 1,
        MimeTypes = 2
    }

    internal class ProgramOptions
    {
        private Version _version = new Version(1, 0, 0, 0);

        private string _rootDirectory;
        private string[] _prefixBinding;
        private string[] _assetDir;
        private string _serverScriptDir;
        private bool _verboseMode = false;
        private List<string> _mimeRemove = new List<string>();
        private Dictionary<string, string> _mimeAdd = new Dictionary<string, string>();
        private bool _disableServerScript = false;
        private bool _allowTransverseExecution = false;
        private bool _allowWebsocket = false;
        private bool _helpMode = false;
        private bool _argumentError = false;
        private string _errorMessage = null;
        private bool _continueRunning = true;
        private ApplicationHelpCategory _printBeforeQuit;

        public ProgramOptions(string[] args)
        {
            _continueRunning = ProcessArgs(args);
        }

        public bool ArgumentError
        {
            get { return _argumentError; }
        }

        public string ErrorMessage
        {
            get { return _errorMessage; }
        }

        public ApplicationHelpCategory HelpInfo
        {
            get { return _printBeforeQuit; }
        }

        public bool ContinueRunning
        {
            get { return _continueRunning; }
        }

        public bool HelpMode
        {
            get { return _helpMode; }
        }

        public Version ApplicationVersion
        {
            get { return _version; }
        }

        public string RootDirectory
        {
            get { return _rootDirectory; }
            set { _rootDirectory = value; }
        }

        public string[] PrefixBinding
        {
            get { return _prefixBinding; }
            set { _prefixBinding = value; }
        }

        public string[] AssetDirectoryName
        {
            get { return _assetDir; }
            set { _assetDir = value; }
        }

        public string ServerSideScriptDirectoryName
        {
            get { return _serverScriptDir; }
            set { _serverScriptDir = value; }
        }

        public bool ServerSideScript
        {
            get { return !_disableServerScript; }
        }

        public bool TransverseExecution
        {
            get { return _allowTransverseExecution; }
        }

        public bool Websocket
        {
            get { return _allowWebsocket; }
        }

        public bool Verbose
        {
            get { return _verboseMode; }
        }

        public ReadOnlyDictionary<string, string> MimeTypes
        {
            get
            {
                var midware = new StaticFileMiddleware("dummy");
                var mimes = midware.MimeTypes;
                foreach (string add in _mimeAdd.Keys)
                {
                    mimes.Add(add, _mimeAdd[add]);
                }
                foreach (string rm in _mimeRemove)
                {
                    mimes.Remove(rm);
                }

                return new ReadOnlyDictionary<string, string>(mimes);
            }
        }

        public ReadOnlyDictionary<string, string> AddedMimeTypes
        {
            get
            {
                return new ReadOnlyDictionary<string, string>(_mimeAdd);
            }
        }

        public string[] RemovedMimeTypes
        {
            get
            {
                return _mimeRemove.ToArray();
            }
        }
        public ReadOnlyDictionary<string, string> DefaultMimeTypes
        {
            get
            {
                var midware = new StaticFileMiddleware("dummy");
                return new ReadOnlyDictionary<string, string>(midware.MimeTypes);
            }
        }

        private bool ProcessArgs(string[] args)
        {
            int lastArg = 0;
            List<string> hostPrefix = new List<string>();
            List<string> assetPath = new List<string>();
            string serverScriptPath = null;
            bool disableServerScript = false;
            bool allowTransverseExecution = false;
            bool allowWebsocket = false;

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
                        _errorMessage = "Argument -i/--hostname requires a value.";
                        return false;
                    }
                    hostPrefix.Add(args[lastArg + 1].TrimEnd('/') + "/");
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
                else if (IsArg(args[lastArg], "k", "websocket"))
                {
                    allowWebsocket = true;
                }
                else if (IsArg(args[lastArg], "s", "server"))
                {
                    if (args.Length <= (lastArg + 1))
                    {
                        _errorMessage = "Argument -s/--server requires a value.";
                        return false;
                    }
                    serverScriptPath = args[lastArg + 1].Trim('/', '\\');
                    lastArg += 1;
                }
                else if (IsArg(args[lastArg], "a", "asset"))
                {
                    if (args.Length <= (lastArg + 1))
                    {
                        _errorMessage = "Argument -a/--asset requires a value.";
                        return false;
                    }
                    assetPath.Add(args[lastArg + 1].Trim('/', '\\'));
                    lastArg += 1;
                }
                else if (IsArg(args[lastArg], "m", "mime"))
                {
                    if (args.Length <= (lastArg + 1))
                    {
                        _errorMessage = "Argument -m/--mime requires a value.";
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
                            _errorMessage = "Invalid syntax. To add a mimetype, use: -m <ext>:<mimetype>" + "\n" +
                                "EXAMPLE: -m .woff:application/octet-stream";
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
                    _continueRunning = false;
                    _printBeforeQuit = ApplicationHelpCategory.MimeTypes;
                    return false;
                }
                else if (IsArg(args[lastArg], "ver", "version"))
                {
                    _continueRunning = false;
                    _printBeforeQuit = ApplicationHelpCategory.Version;
                    return false;
                }
                else if (IsArg(args[lastArg], "h", "help") || args[lastArg] == "/?")
                {
                    _continueRunning = false;
                    _printBeforeQuit = ApplicationHelpCategory.Help;
                    return false;
                }
                else
                {
                    _continueRunning = false;
                    _printBeforeQuit = ApplicationHelpCategory.Help;
                    return false;
                }
            }

            if (!disableServerScript)
            {
                foreach (string assetPathItem in assetPath)
                {
                    if (assetPathItem == serverScriptPath)
                    {
                        _errorMessage = "Asset folder name cannot be the same as the server script path name!";
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
            if (allowWebsocket == true)
                _allowWebsocket = true;

            return true;
        }

        private static bool IsArg(string candidate, string longName)
        {
            return IsArg(candidate, shortName: null, longName: longName);
        }

        private static bool IsArg(string candidate, string shortName, string longName)
        {
            return (shortName != null && candidate.Equals("-" + shortName)) ||
                (longName != null && candidate.Equals("--" + longName));
        }
    }
}
