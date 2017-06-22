using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using System.Threading.Tasks;
using RazorRenderer;
using System.IO;
using System.Collections.Generic;
using System.Linq;

namespace Blazor.Sdk.Host
{
    public static class RazorCompilation
    {
        static PathString binDir = new PathString("/_bin");
        static IDictionary<string, byte[]> cachedCompilationResults = new Dictionary<string, byte[]>();
        static object cachedCompilationResultsLock = new object();
        static FileSystemWatcher activeFileSystemWatcher; // If we don't hold a reference to this, it gets disposed automatically on Linux (though not on Windows)

        public static void UseRazorCompilation(this IApplicationBuilder builder)
        {
            var rootDir = Directory.GetCurrentDirectory();
            BeginFileSystemWatcher(rootDir);

            builder.Use(async (context, next) =>
            {
                var req = context.Request;
                if (req.Path.StartsWithSegments(binDir) && req.Query["type"] == "razorviews")
                {
                    await ServeCompiledAssembly(context, rootDir);
                }
                else
                {
                    await next();
                }
            });
        }

        private static void BeginFileSystemWatcher(string rootDir)
        {
            activeFileSystemWatcher = new FileSystemWatcher
            {
                Path = rootDir,
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName,
                IncludeSubdirectories = true
            };

            activeFileSystemWatcher.Deleted += (sender, evtArgs) => OnFileSystemWatcherEvent(evtArgs);
            activeFileSystemWatcher.Created += (sender, evtArgs) => OnFileSystemWatcherEvent(evtArgs);
            activeFileSystemWatcher.Changed += (sender, evtArgs) => OnFileSystemWatcherEvent(evtArgs);
            activeFileSystemWatcher.Renamed += (sender, evtArgs) => OnFileSystemWatcherEvent(evtArgs);

            activeFileSystemWatcher.EnableRaisingEvents = true;
        }

        private static void OnFileSystemWatcherEvent(FileSystemEventArgs evtArgs)
        {
            // We only care about .cshtml files
            if (Path.GetExtension(evtArgs.FullPath) == ".cshtml")
            {
                lock (cachedCompilationResultsLock)
                {
                    cachedCompilationResults.Clear();
                }

                LiveReloading.RequestReload();
            }
        }

        private static async Task ServeCompiledAssembly(HttpContext context, string rootDir)
        {
            // Determine the desired views assembly name based on the URL
            var requestPath = context.Request.Path.Value;
            var assemblyFilename = requestPath.Substring(requestPath.LastIndexOf('/') + 1);

            // Get or create cached compilation result. Doesn't really matter that we might be blocking
            // other request threads with this lock, as this is a development-time feature only.
            byte[] compiledAssembly;
            lock (cachedCompilationResultsLock)
            {
                var cacheKey = assemblyFilename;
                if (!cachedCompilationResults.ContainsKey(cacheKey))
                {
                    cachedCompilationResults[cacheKey] = PerformCompilation(assemblyFilename, rootDir);
                }

                compiledAssembly = cachedCompilationResults[cacheKey];
            }

            // Actually serve it
            context.Response.ContentType = "application/octet-steam";
            await context.Response.Body.WriteAsync(compiledAssembly, 0, compiledAssembly.Length);
        }

        private static byte[] PerformCompilation(string assemblyFilename, string rootDir)
        {
            // Get the list of assembly paths to reference during compilation. Currently this
            // is just the main app assembly, so that you can have code-behind classes.
            // TODO: Reference all the same assemblies that the main app assembly does. Not
            // certain how to get that info. Might be enough to have the client pass through
            // via querystring the list of referenced assemblies declared on the <script> tag.
            var inferredMainAssemblyFilename = InferMainAssemblyFilename(assemblyFilename);
            var referenceAssemblyFilenames = new List<string>();
            if (!string.IsNullOrEmpty(inferredMainAssemblyFilename))
            {
                referenceAssemblyFilenames.Add(inferredMainAssemblyFilename);
            }

            using (var ms = new MemoryStream())
            {
                RazorVDomCompiler.CompileToStream(
                    enableLogging: false,
                    rootDir: rootDir,
                    referenceAssemblies: referenceAssemblyFilenames.Select(filename => Path.Combine("bin", "Debug", "netcoreapp2.0", filename)).ToArray(),
                    outputAssemblyName: Path.GetFileNameWithoutExtension(assemblyFilename),
                    outputStream: ms);

                return ms.ToArray();
            }
        }

        private static string InferMainAssemblyFilename(string viewsAssemblyFilename)
        {
            const string viewsAssemblySuffix = ".Views.dll";
            if (viewsAssemblyFilename.EndsWith(viewsAssemblySuffix))
            {
                var partBeforeSuffix = viewsAssemblyFilename.Substring(0, viewsAssemblyFilename.Length - viewsAssemblySuffix.Length);
                return $"{partBeforeSuffix}.dll";
            }

            return null;
        }
    }
}
