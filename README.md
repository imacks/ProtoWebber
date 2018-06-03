What is ProtoWebber
==================
This is a no-fuss quick-and-easy webserver.

I first wrote ProtoWebber as a pet project in an attempt to understand how a webserver works. Later on it 
became my choice for local web application development.

Thus, you shouldn't use this in a production environment. Try NginX or NodeJS instead.

Instead, this is a lightweight webserver you can use when creating a website. Save yourself all 
the hassle of setting up your dev environment and just get to work on the webapp!


Usage
-----
Just unzip all to a folder of your choice. Click `testwebber.exe` and navigate to `http://localhost:8080`.

Static files are under `(installDir)\wwwroot\assets` and server side scripts are in `(installDir)\wwwroot\server`.

Currently supports server-side JavaScript. Backed by the awesome ChakraCore engine!

```powershell
C:\> testwebber /?

TestWebber
A quick and simple web server for testing and development use.
Copyright (c) 2017 Macks L. All rights reserved.

testwebber [-d path] [-h hostname] [-!ss | -s server] [-a asset] [-m extension:mimetype] [-v]
testwebber --show-mimetypes
testwebber -ver
testwebber -h

-d|--wwwroot      Path to files and scripts where the web server will serve files from
                  Default is <CurrentDir>\wwwroot
-i|--hostname     Host name to listen. May be specified more than once.
                  Default is http://localhost:8080/
-!ss|--disableserverscript
                  Disables server side scripting.
-s|--server       Name of server side scripting directory in wwwroot.
                  Default is 'server'.
-a|--asset        Name of assets directory in wwwroot.
                  Default is 'assets'.
-m|--mime         Additional mime types. To remove, -<extension>. May be specified more than once.
                  To get a list of stock mime type mappings, use testwebber '--show-mimetypes'
--show-mimetypes  Shows a list of stock mimetypes. Not to be used with any other parameters.

Common arguments:
-ver|--version    Shows version number of this application
-v|--verbose      Displays verbose information
-h|/?|--help      Shows this help screen.


EXAMPLES
========
# Run a web server on http://localhost:8080.
# - If request starts with '/assets':
#   - look for the file in (CurrentDir)/wwwroot/assets and serve as static file.
#   - if not found, return 404
# - Otherwise, webserver will look for files ending in .js or .jsx in (CurrentDir)/wwwroot/server, and execute it on on server side if found.
# - No luck? webserver will try to execute (CurrentDir)/wwwroot/server/index.js or (CurrentDir)/wwwroot/server/index.jsx
# - If all else fails, webserver will return a 404 error.
testwebber

# Disables server side scripting. Serves everything under (CurrentDir)/wwwroot/assets and (CurrentDir)/wwwroot/pages as static files.
testwebber -a assets -a pages -!ss

# Removes the file extension .tiff mimetype
testwebber -m -.tiff

# Removes the file extension .tiff mimetype; adds custom extension .foo and map to text/csv mimetype.
testwebber -m -.tiff -m .foo:text/csv

```


Building
--------
**Important!** You probably want to change the location where packages will be stored.

Open up `(repoDir)/src/globa.bsd` with your favorite editor, and look for these lines:
```bash
pkgDir = ${rootDir}'packages/oneget'
dotnetSdkDir = ${rootDir}'packages/dotnetsdk'
#credDir = ${rootDir}'Credentials/imacks'
```

Change `${rootDir}` to `${repoDir}`. This will cause all packages required to build this project to be stored under `(repoDir)/packages`.
Note that I'm using .NETCore SDK, so it should cost a little more than 1GB for everything to be ready.

Btw, I'm using [DotNetBuild](http://www.github.com/buildcenter/dotnetbuild) to stage my build:

```powershell
# show help
build /?

# stage the build first
build configure

# build debug version. will download the necessary tools and packages automatically
build debug *

# build a release version
build release *
```
