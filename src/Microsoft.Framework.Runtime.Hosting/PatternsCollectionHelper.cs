// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Framework.Runtime.Json;

namespace Microsoft.Framework.Runtime
{
    internal static class PatternsCollectionHelper
    {
        private static readonly char[] PatternSeparator = new[] { ';' };

        public static IEnumerable<string> GetPatternsCollection(JsonObject rawProject, string projectDirectory, string projectFilePath, string propertyName, IEnumerable<string> defaultPatterns = null)
        {
            defaultPatterns = defaultPatterns ?? Enumerable.Empty<string>();

            try
            {
                if (!rawProject.Keys.Contains(propertyName))
                {
                    return CreateCollection(projectDirectory, defaultPatterns.ToArray());
                }

                string valueInString = rawProject.ValueAsString(propertyName);
                if (valueInString != null)
                {
                    return CreateCollection(projectDirectory, valueInString);
                }

                string[] valuesInArray = rawProject.ValueAsArray<string>(propertyName);
                if (valuesInArray != null)
                {
                    return CreateCollection(projectDirectory, valuesInArray);
                }
            }
            catch (Exception ex)
            {
                // TODO: add line information
                throw new FileFormatException(ex.Message, ex);
            }

            // TODO: add line information
            throw new FileFormatException("Value must be either string or array.");
        }

        private static IEnumerable<string> CreateCollection(string projectDirectory, params string[] patternsStrings)
        {
            var patterns = patternsStrings.SelectMany(patternsString => GetSourcesSplit(patternsString))
                                          .Select(patternString => patternString.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar));

            foreach (var pattern in patterns)
            {
                if (Path.IsPathRooted(pattern))
                {
                    throw new InvalidOperationException(string.Format("Patten {0} is a rooted path, which is not supported.", pattern));
                }
            }

            return new List<string>(patterns.Select(pattern => FolderToPattern(pattern, projectDirectory)));
        }

        private static IEnumerable<string> GetSourcesSplit(string sourceDescription)
        {
            if (string.IsNullOrEmpty(sourceDescription))
            {
                return Enumerable.Empty<string>();
            }

            return sourceDescription.Split(PatternSeparator, StringSplitOptions.RemoveEmptyEntries);
        }

        private static string FolderToPattern(string candidate, string projectDir)
        {
            // This conversion is needed to support current template

            // If it's already a pattern, no change is needed
            if (candidate.Contains('*'))
            {
                return candidate;
            }

            // If the given string ends with a path separator, or it is an existing directory
            // we convert this folder name to a pattern matching all files in the folder
            if (candidate.EndsWith(@"\") ||
                candidate.EndsWith("/") ||
                Directory.Exists(Path.Combine(projectDir, candidate)))
            {
                return Path.Combine(candidate, "**", "*");
            }

            // Otherwise, it represents a single file
            return candidate;
        }
    }
}