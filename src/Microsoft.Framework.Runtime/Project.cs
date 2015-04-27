// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Text;
using Microsoft.Framework.Runtime.Compilation;
using Microsoft.Framework.Runtime.Helpers;
using Microsoft.Framework.Runtime.Json;
using Newtonsoft.Json;
using NuGet;

namespace Microsoft.Framework.Runtime
{
    public class Project : ICompilationProject
    {
        public const string ProjectFileName = "project.json";

        internal static readonly TypeInformation DefaultRuntimeCompiler = new TypeInformation("Microsoft.Framework.Runtime.Roslyn", "Microsoft.Framework.Runtime.Roslyn.RoslynProjectCompiler");
        internal static readonly TypeInformation DefaultDesignTimeCompiler = new TypeInformation("Microsoft.Framework.Runtime.Compilation.DesignTime", "Microsoft.Framework.Runtime.DesignTimeHostProjectCompiler");

        internal static TypeInformation DefaultCompiler = DefaultRuntimeCompiler;

        private readonly Dictionary<FrameworkName, TargetFrameworkInformation> _targetFrameworks = new Dictionary<FrameworkName, TargetFrameworkInformation>();
        private readonly Dictionary<FrameworkName, CompilerOptions> _compilationOptions = new Dictionary<FrameworkName, CompilerOptions>();
        private readonly Dictionary<string, CompilerOptions> _configurations = new Dictionary<string, CompilerOptions>(StringComparer.OrdinalIgnoreCase);

        private CompilerOptions _defaultCompilerOptions;

        private TargetFrameworkInformation _defaultTargetFrameworkConfiguration;

        public Project()
        {
        }

        public string ProjectFilePath { get; private set; }

        public string ProjectDirectory
        {
            get
            {
                return Path.GetDirectoryName(ProjectFilePath);
            }
        }

        public string Name { get; private set; }

        public string Title { get; set; }

        public string Description { get; private set; }

        public string Copyright { get; set; }

        public string Summary { get; set; }

        public string[] Authors { get; private set; }

        public string[] Owners { get; private set; }

        public bool EmbedInteropTypes { get; set; }

        public SemanticVersion Version { get; private set; }

        // Temporary while old and new runtime are separate
        string ICompilationProject.Version { get { return Version?.ToString(); } }
        string ICompilationProject.AssemblyFileVersion { get { return AssemblyFileVersion?.ToString(); } }

        public Version AssemblyFileVersion { get; private set; }

        public IList<LibraryDependency> Dependencies { get; private set; }

        public CompilerServices CompilerServices { get; private set; }

        public string WebRoot { get; private set; }

        public string EntryPoint { get; private set; }

        public string ProjectUrl { get; private set; }

        public string LicenseUrl { get; set; }

        public string IconUrl { get; set; }

        public bool RequireLicenseAcceptance { get; private set; }

        public string[] Tags { get; private set; }

        public bool IsLoadable { get; set; }

        public IProjectFilesCollection Files { get; private set; }

        public IDictionary<string, string> Commands { get; private set; }

        public IDictionary<string, IEnumerable<string>> Scripts { get; private set; }

        public IEnumerable<TargetFrameworkInformation> GetTargetFrameworks()
        {
            return _targetFrameworks.Values;
        }

        public IEnumerable<string> GetConfigurations()
        {
            return _configurations.Keys;
        }

        public static bool HasProjectFile(string path)
        {
            string projectPath = Path.Combine(path, ProjectFileName);

            return File.Exists(projectPath);
        }

        public static bool TryGetProject(string path, out Project project, ICollection<ICompilationMessage> diagnostics = null)
        {
            project = null;

            string projectPath = null;

            if (string.Equals(Path.GetFileName(path), ProjectFileName, StringComparison.OrdinalIgnoreCase))
            {
                projectPath = path;
                path = Path.GetDirectoryName(path);
            }
            else if (!HasProjectFile(path))
            {
                return false;
            }
            else
            {
                projectPath = Path.Combine(path, ProjectFileName);
            }

            // Assume the directory name is the project name if none was specified
            var projectName = PathUtility.GetDirectoryName(path);
            projectPath = Path.GetFullPath(projectPath);

            try
            {
                using (var stream = File.OpenRead(projectPath))
                {
                    project = GetProjectFromStream(stream, projectName, projectPath, diagnostics);
                }
            }
            catch (JsonReaderException ex)
            {
                throw FileFormatException.Create(ex, projectPath);
            }

            return true;
        }

        public static Project GetProject(string json, string projectName, string projectPath, ICollection<ICompilationMessage> diagnostics = null)
        {
            var ms = new MemoryStream(Encoding.UTF8.GetBytes(json));

            var project = GetProjectFromStream(ms, projectName, projectPath, diagnostics);

            return project;
        }

        internal static Project GetProjectFromStream(Stream stream, string projectName, string projectPath, ICollection<ICompilationMessage> diagnostics = null)
        {
            var project = new Project();

            var deserializer = new JsonDeserializer();
            var streamReader = new StreamReader(stream);
            var projectContentDictionary = deserializer.Deserialize(streamReader.ReadToEnd()) as IDictionary<string, object>;

            if (projectContentDictionary == null)
            {
                // TODO: add line information
                throw new FileFormatException("Failed to parse project.json");
            }

            var rawProject2 = new JsonObject(projectContentDictionary);

            // Meta-data properties
            project.Name = projectName;
            project.ProjectFilePath = Path.GetFullPath(projectPath);

            project.Version = rawProject2.ValueAs<SemanticVersion>("version", versionInObject =>
            {
                var version = versionInObject as string;
                if (version == null)
                {
                    return new SemanticVersion("1.0.0");
                }
                else
                {
                    try
                    {
                        var buildVersion = Environment.GetEnvironmentVariable("DNX_BUILD_VERSION");
                        return SpecifySnapshot(version, buildVersion);
                    }
                    catch (Exception /*ex*/)
                    {
                        //var lineInfo = (IJsonLineInfo)version;
                        //throw FileFormatException.Create(ex, version, project.ProjectFilePath);

                        throw new FileFormatException("Fail to parse version");
                    }
                }
            });

            var fileVersion = Environment.GetEnvironmentVariable("DNX_ASSEMBLY_FILE_VERSION");
            if (string.IsNullOrWhiteSpace(fileVersion))
            {
                project.AssemblyFileVersion = project.Version.Version;
            }
            else
            {
                try
                {
                    var simpleVersion = project.Version.Version;
                    project.AssemblyFileVersion = new Version(simpleVersion.Major,
                        simpleVersion.Minor,
                        simpleVersion.Build,
                        int.Parse(fileVersion));
                }
                catch (FormatException ex)
                {
                    throw new FormatException("The assembly file version is invalid: " + fileVersion, ex);
                }
            }

            project.Description = rawProject2.ValueAsString("description");
            project.Summary = rawProject2.ValueAsString("summary");
            project.Copyright = rawProject2.ValueAsString("copyright");
            project.Title = rawProject2.ValueAsString("title");
            project.WebRoot = rawProject2.ValueAsString("webroot");
            project.EntryPoint = rawProject2.ValueAsString("entryPoint");
            project.ProjectUrl = rawProject2.ValueAsString("projectUrl");
            project.LicenseUrl = rawProject2.ValueAsString("licenseUrl");
            project.IconUrl = rawProject2.ValueAsString("iconUrl");

            project.Authors = rawProject2.ValueAsArray<string>("authors") ?? new string[] { };
            project.Owners = rawProject2.ValueAsArray<string>("owners") ?? new string[] { };
            project.Tags = rawProject2.ValueAsArray<string>("tags") ?? new string[] { };

            project.RequireLicenseAcceptance = rawProject2.ValueAsBoolean("requireLicenseAcceptance", defaultValue: false);
            project.IsLoadable = rawProject2.ValueAsBoolean("loadable", defaultValue: true);
            // TODO: Move this to the dependencies node
            project.EmbedInteropTypes = rawProject2.ValueAsBoolean("embedInteropTypes", defaultValue: false);

            project.Dependencies = new List<LibraryDependency>();

            // Project files
            project.Files = new ProjectFilesCollection(projectContentDictionary,
                                                       project.ProjectDirectory,
                                                       project.ProjectFilePath,
                                                       diagnostics);

            var compilerInfo = rawProject2.ValueAsJsonObject("compiler");
            if (compilerInfo != null)
            {
                var languageName = compilerInfo.ValueAsString("name") ?? "C#";
                var compilerAssembly = compilerInfo.ValueAsString("compilerAssembly");
                var compilerType = compilerInfo.ValueAsString("compilerType");

                var compiler = new TypeInformation(compilerAssembly, compilerType);
                project.CompilerServices = new CompilerServices(languageName, compiler);
            }

            project.Commands = rawProject2.ValueAs<IDictionary<string, string>>("commands", value =>
            {
                var dict = value as IDictionary<string, object>;
                if (dict != null)
                {
                    return dict.Where(pair => pair.Value is string)
                               .ToDictionary(keySelector: pair => pair.Key,
                                             elementSelector: pair => pair.Value as string,
                                             comparer: StringComparer.OrdinalIgnoreCase);
                }
                else
                {
                    return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                }
            });

            project.Scripts = rawProject2.ValueAs<IDictionary<string, IEnumerable<string>>>("scripts", value =>
            {
                var dict = value as IDictionary<string, object>;
                if (dict != null)
                {
                    var result = new Dictionary<string, IEnumerable<string>>(StringComparer.OrdinalIgnoreCase);

                    var jsonobject = new JsonObject(dict);
                    foreach (var key in dict.Keys)
                    {
                        var stringValue = jsonobject.ValueAsString(key);
                        if (stringValue != null)
                        {
                            result[key] = new string[] { stringValue };
                        }

                        var arrayValue = jsonobject.ValueAsArray<string>(key);
                        if (arrayValue != null)
                        {
                            result[key] = arrayValue;
                        }

                        /* TODO: add error handling

                            throw FileFormatException.Create(
                            string.Format("The value of a script in {0} can only be a string or an array of strings", ProjectFileName),
                            value,
                            project.ProjectFilePath);
                        */
                    }

                    return result;
                }
                else
                {
                    return new Dictionary<string, IEnumerable<string>>(StringComparer.OrdinalIgnoreCase);
                }
            });

            project.BuildTargetFrameworksAndConfigurations(rawProject2);

            PopulateDependencies(
                project.ProjectFilePath,
                project.Dependencies,
                rawProject2,
                "dependencies",
                isGacOrFrameworkReference: false);

            return project;
        }

        private static SemanticVersion SpecifySnapshot(string version, string snapshotValue)
        {
            if (version.EndsWith("-*"))
            {
                if (string.IsNullOrEmpty(snapshotValue))
                {
                    version = version.Substring(0, version.Length - 2);
                }
                else
                {
                    version = version.Substring(0, version.Length - 1) + snapshotValue;
                }
            }

            return new SemanticVersion(version);
        }

        private static void PopulateDependencies(
            string projectPath,
            IList<LibraryDependency> results,
            JsonObject settings,
            string propertyName,
            bool isGacOrFrameworkReference)
        {
            var dependencies = settings.ValueAsJsonObject(propertyName);  //settings[propertyName] as JObject;
            if (dependencies != null)
            {
                foreach (var dependencyKey in dependencies.Keys)
                {
                    if (string.IsNullOrEmpty(dependencyKey))
                    {
                        // TODO: add line information
                        throw new FileFormatException("Unable to resolve dependency ''.");
                    }

                    // Support 
                    // "dependencies" : {
                    //    "Name" : "1.0"
                    // }

                    //var dependencyValue = dependency.Value;
                    var dependencyTypeValue = LibraryDependencyType.Default;

                    //JToken dependencyVersionToken = dependencyValue;

                    string dependencyVersionValue = dependencies.ValueAsString(dependencyKey);
                    if (dependencyVersionValue == null)
                    {
                        var versionStructure = dependencies.ValueAsJsonObject(dependencyKey);
                        if (versionStructure == null)
                        {
                            // TODO: need line information
                            throw new FileFormatException("Unrecoganizable format of dependency version of " + dependencyKey);
                        }

                        dependencyVersionValue = versionStructure.ValueAsString("version");

                        IEnumerable<string> strings;
                        if (TryGetStringEnumerable(versionStructure, "type", out strings))
                        {
                            dependencyTypeValue = LibraryDependencyType.Parse(strings);
                        }
                    }

                    SemanticVersionRange dependencyVersionRange = null;

                    if (!string.IsNullOrEmpty(dependencyVersionValue))
                    {
                        try
                        {
                            dependencyVersionRange = VersionUtility.ParseVersionRange(dependencyVersionValue);
                        }
                        catch (Exception /*ex*/)
                        {
                            throw new FieldAccessException("Failed to parse version range from: " + dependencyVersionValue);
                            //throw FileFormatException.Create(
                            //    ex,
                            //    dependencyVersionToken,
                            //    projectPath);
                        }
                    }

                    //var dependencyLineInfo = (IJsonLineInfo)dependency;

                    results.Add(new LibraryDependency
                    {
                        LibraryRange = new LibraryRange
                        {
                            Name = dependencyKey,
                            VersionRange = dependencyVersionRange,
                            IsGacOrFrameworkReference = isGacOrFrameworkReference,
                            FileName = projectPath,
                            // TODO: add them back
                            //Line = dependencyLineInfo.LineNumber,
                            //Column = dependencyLineInfo.LinePosition
                        },
                        Type = dependencyTypeValue
                    });
                }
            }
        }

        private static bool TryGetStringEnumerable(JsonObject parent, string property, out IEnumerable<string> result)
        {
            var collection = new List<string>();
            var valueInString = parent.ValueAsString(property);
            if (valueInString != null)
            {
                collection.Add(valueInString);
            }
            else
            {
                var valueInArray = parent.ValueAsArray<string>(property);
                if (valueInArray != null)
                {
                    collection.AddRange(valueInArray);
                }
                else
                {
                    result = null;
                    return false;
                }
            }

            result = collection.SelectMany(value => value.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries));
            return true;
        }

        public CompilerOptions GetCompilerOptions()
        {
            return _defaultCompilerOptions;
        }

        public CompilerOptions GetCompilerOptions(string configurationName)
        {
            CompilerOptions options;
            if (_configurations.TryGetValue(configurationName, out options))
            {
                return options;
            }

            return null;
        }

        public CompilerOptions GetCompilerOptions(FrameworkName frameworkName)
        {
            CompilerOptions options;
            if (_compilationOptions.TryGetValue(frameworkName, out options))
            {
                return options;
            }

            return null;
        }

        public ICompilerOptions GetCompilerOptions(FrameworkName targetFramework,
                                                  string configurationName)
        {
            // Get all project options and combine them
            var rootOptions = GetCompilerOptions();
            var configurationOptions = configurationName != null ? GetCompilerOptions(configurationName) : null;
            var targetFrameworkOptions = targetFramework != null ? GetCompilerOptions(targetFramework) : null;

            // Combine all of the options
            return CompilerOptions.Combine(rootOptions, configurationOptions, targetFrameworkOptions);
        }

        public TargetFrameworkInformation GetTargetFramework(FrameworkName targetFramework)
        {
            TargetFrameworkInformation targetFrameworkInfo;
            if (_targetFrameworks.TryGetValue(targetFramework, out targetFrameworkInfo))
            {
                return targetFrameworkInfo;
            }

            IEnumerable<TargetFrameworkInformation> compatibleConfigurations;
            if (VersionUtility.TryGetCompatibleItems(targetFramework, GetTargetFrameworks(), out compatibleConfigurations) &&
                compatibleConfigurations.Any())
            {
                targetFrameworkInfo = compatibleConfigurations.FirstOrDefault();
            }

            return targetFrameworkInfo ?? _defaultTargetFrameworkConfiguration;
        }

        private void BuildTargetFrameworksAndConfigurations(JsonObject projectJsonObject)
        {
            // Get the shared compilationOptions
            _defaultCompilerOptions = GetCompilationOptions(projectJsonObject) ?? new CompilerOptions();

            _defaultTargetFrameworkConfiguration = new TargetFrameworkInformation
            {
                Dependencies = new List<LibraryDependency>()
            };

            // Add default configurations
            _configurations["Debug"] = new CompilerOptions
            {
                Defines = new[] { "DEBUG", "TRACE" },
                Optimize = false
            };

            _configurations["Release"] = new CompilerOptions
            {
                Defines = new[] { "RELEASE", "TRACE" },
                Optimize = true
            };

            // The configuration node has things like debug/release compiler settings
            /*
                {
                    "configurations": {
                        "Debug": {
                        },
                        "Release": {
                        }
                    }
                }
            */

            var configurations = projectJsonObject.ValueAsJsonObject("configurations");
            if (configurations != null)
            {
                foreach (var configKey in configurations.Keys)
                {
                    var compilerOptions = GetCompilationOptions(configurations.ValueAsJsonObject(configKey));

                    // Only use this as a configuration if it's not a target framework
                    _configurations[configKey] = compilerOptions;
                }
            }

            // The frameworks node is where target frameworks go
            /*
                {
                    "frameworks": {
                        "net45": {
                        },
                        "k10": {
                        }
                    }
                }
            */

            var frameworks = projectJsonObject.ValueAsJsonObject("frameworks");
            if (frameworks != null)
            {
                foreach (var frameworkKey in frameworks.Keys)
                {
                    try
                    {
                        BuildTargetFrameworkNode(frameworkKey, frameworks.ValueAsJsonObject(frameworkKey));
                    }
                    catch (Exception ex)
                    {
                        throw new FileFormatException(string.Format("Failed to parse framework {0}.", frameworkKey), ex);
                        //throw FileFormatException.Create(ex, framework.Value, ProjectFilePath);
                    }
                }
            }
        }

        /// <summary>
        /// Parse a Json object which represents project configuration for a specified framework
        /// </summary>
        /// <param name="frameworkKey">The name of the framework</param>
        /// <param name="frameworkValue">The Json object represent the settings</param>
        /// <returns>Returns true if it successes.</returns>
        private bool BuildTargetFrameworkNode(string frameworkKey, JsonObject frameworkValue)
        {
            // If no compilation options are provided then figure them out from the node
            var compilerOptions = GetCompilationOptions(frameworkValue) ??
                                  new CompilerOptions();

            var frameworkName = FrameworkNameHelper.ParseFrameworkName(frameworkKey);

            // If it's not unsupported then keep it
            if (frameworkName == VersionUtility.UnsupportedFrameworkName)
            {
                // REVIEW: Should we skip unsupported target frameworks
                return false;
            }

            // Add the target framework specific define
            var defines = new HashSet<string>(compilerOptions.Defines ?? Enumerable.Empty<string>());
            var frameworkDefinition = Tuple.Create(frameworkKey, frameworkName);
            var frameworkDefine = FrameworkNameHelper.MakeDefaultTargetFrameworkDefine(frameworkDefinition);

            if (!string.IsNullOrEmpty(frameworkDefine))
            {
                defines.Add(frameworkDefine);
            }

            compilerOptions.Defines = defines;

            var targetFrameworkInformation = new TargetFrameworkInformation
            {
                FrameworkName = frameworkName,
                Dependencies = new List<LibraryDependency>()
            };

            //var properties = d targetFramework.Value.Value<JObject>();
            var frameworkDependencies = new List<LibraryDependency>();

            PopulateDependencies(
                ProjectFilePath,
                frameworkDependencies,
                frameworkValue,
                "dependencies",
                isGacOrFrameworkReference: false);

            var frameworkAssemblies = new List<LibraryDependency>();
            PopulateDependencies(
                ProjectFilePath,
                frameworkAssemblies,
                frameworkValue,
                "frameworkAssemblies",
                isGacOrFrameworkReference: true);

            frameworkDependencies.AddRange(frameworkAssemblies);
            targetFrameworkInformation.Dependencies = frameworkDependencies;

            targetFrameworkInformation.WrappedProject = frameworkValue.ValueAsString("wrappedProject");

            var binNode = frameworkValue.ValueAsJsonObject("bin");
            if (binNode != null)
            {
                targetFrameworkInformation.AssemblyPath = binNode.ValueAsString("assembly");
                targetFrameworkInformation.PdbPath = binNode.ValueAsString("pdb");
            }

            _compilationOptions[frameworkName] = compilerOptions;
            _targetFrameworks[frameworkName] = targetFrameworkInformation;

            return true;
        }

        private static CompilerOptions GetCompilationOptions(JsonObject rawObject)
        {
            var rawOptions = rawObject.ValueAsJsonObject("compilationOptions");
            if (rawOptions == null)
            {
                return null;
            }

            return new CompilerOptions
            {
                Defines = rawOptions.ValueAsArray<string>("define"),
                LanguageVersion = rawOptions.ValueAsString("languageVersion"),
                AllowUnsafe = rawOptions.ValueAsNullableBoolean("allowUnsafe"),
                Platform = rawOptions.ValueAsString("platform"),
                WarningsAsErrors = rawOptions.ValueAsNullableBoolean("warningsAsErrors"),
                Optimize = rawOptions.ValueAsNullableBoolean("optimize"),
            };
        }
    }
}
