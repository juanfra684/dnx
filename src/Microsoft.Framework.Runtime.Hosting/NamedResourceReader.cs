// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.


using System.Collections.Generic;
using System.IO;
using Microsoft.Framework.Runtime.Json;

namespace Microsoft.Framework.Runtime
{
    internal static class NamedResourceReader
    {
        public static IDictionary<string, string> ReadNamedResources(JsonObject rawProject, string projectFilePath)
        {
            if (!rawProject.Keys.Contains("namedResource"))
            {
                return new Dictionary<string, string>();
            }

            var namedResourceToken = rawProject.ValueAsJsonObject("namedResource");
            if (namedResourceToken == null)
            {
                throw new FileFormatException(string.Format("Value must of namedResource be object."));
            }

            var namedResources = new Dictionary<string, string>();

            foreach (var namedResourceKey in namedResourceToken.Keys)
            {
                var resourcePath = namedResourceToken.ValueAsString(namedResourceKey);
                if (resourcePath == null)
                {
                    throw new FileFormatException("Value must be string.");
                }

                if (resourcePath.Contains("*"))
                {
                    throw new FileFormatException("Value cannot contain wildcards.");
                }

                var resourceFileFullPath = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(projectFilePath), resourcePath));

                if (namedResources.ContainsKey(namedResourceKey))
                {
                    throw new FileFormatException(string.Format("The named resource {0} already exists.", namedResourceKey));
                }

                namedResources.Add(namedResourceKey, resourceFileFullPath);
            }

            return namedResources;
        }

        public static void ApplyNamedResources(IDictionary<string, string> namedResources, IDictionary<string, string> resources)
        {
            foreach (var namedResource in namedResources)
            {
                // The named resources dictionary is like the project file
                // key = name, value = path to resource
                if (resources.ContainsKey(namedResource.Value))
                {
                    resources[namedResource.Value] = namedResource.Key;
                }
                else
                {
                    resources.Add(namedResource.Value, namedResource.Key);
                }
            }
        }
    }
}