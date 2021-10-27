using SharpCompress.Archives.Tar;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Writers;

namespace MethodContainerizer
{
    internal static class DockerfileBuilder
    {
        /// <summary>
        /// Generates a new Dockerfile build context and creates a TAR file
        /// </summary>
        /// <param name="assembly">Assembly to be dockerized</param>
        /// <param name="method">The method that this assembly is made for</param>
        /// <returns>The filesystem path to the build context TAR</returns>
        public static string BuildDockerContext(Assembly assembly, MethodInfo method)
        {
            // TODO: Clean up post-run
            var tempPath = Path.GetTempPath();
            var jobId = "kuberpc-" + Guid.NewGuid().ToString().Replace("-", "");
            var jobPath = Path.Combine(tempPath, jobId);

            GenerateBuildFiles(assembly, jobPath);
            return GenerateTar(jobPath);
        }

        /// <summary>
        /// Builds out a new Docker build context with a C# project to import and run the method assembly
        /// </summary>
        private static void GenerateBuildFiles(Assembly assembly, string jobPath)
        {
            Directory.CreateDirectory(jobPath);

            var generator = new Lokad.ILPack.AssemblyGenerator();
            var assemblyBytes = generator.GenerateAssemblyBytes(assembly);

            var sourceFiles = new Dictionary<string, string>
            {
                { "Runner.csproj", Properties.Resources.RunnerProjectFile },
                { "Program.cs", Properties.Resources.RunnerProgram },
                { "Entrypoint.cs", Properties.Resources.RunnerEntrypoint },
                { "appsettings.json", Properties.Resources.RunnerAppSettings },
                { "Startup.cs", Properties.Resources.RunnerStartup },
                { "Dockerfile", Properties.Resources.Dockerfile.Replace("{AssemblyName}", $"{assembly.GetName().Name}.dll") }
            };

            foreach (var sourceFile in sourceFiles)
            {
                File.WriteAllText(Path.Combine(jobPath, sourceFile.Key), sourceFile.Value);
            }

            File.WriteAllBytes(Path.Combine(jobPath, "program.dll"), assemblyBytes);
        }

        /// <summary>
        /// Generates a TAR file from a build context path
        /// </summary>
        /// <returns>The filesystem path to the TAR file/returns>
        private static string GenerateTar(string contextPath)
        {
            var tarId = Guid.NewGuid().ToString().Replace("-", "");
            var tarTempPath = Path.Combine(Path.GetTempPath(), tarId);
            Directory.CreateDirectory(tarTempPath);
            var tarTempFilePath = Path.Combine(tarTempPath, "docker.tar");

            var tar = TarArchive.Create();
            tar.AddAllFromDirectory(Path.Combine(contextPath));
            tar.SaveTo(tarTempFilePath, new WriterOptions(CompressionType.BZip2) { LeaveStreamOpen = false });

            return tarTempFilePath;
        }
    }
}
