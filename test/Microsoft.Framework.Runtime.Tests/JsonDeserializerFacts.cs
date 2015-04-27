// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections.Generic;
using Microsoft.Framework.Runtime.Json;
using Xunit;

namespace Microsoft.Framework.Runtime.Tests
{
    public class JsonDeserializerFacts
    {
        [Fact]
        public void DeserialzeEmptyString()
        {
            var target = new JsonDeserializer();

            var result = target.Deserialize(string.Empty);

            Assert.Null(result);
        }

        [Fact]
        public void DeserialzeIntegerArray()
        {
            var target = new JsonDeserializer();

            var raw = target.Deserialize("[1,2,3]");
            Assert.NotNull(raw);

            var list = raw as IList<object>;
            Assert.NotNull(list);
            Assert.Equal(3, list.Count);
            Assert.Equal(1, (int)list[0]);
            Assert.Equal(2, (int)list[1]);
            Assert.Equal(3, (int)list[2]);
        }

        [Fact]
        public void DeserializeStringArray()
        {
            var target = new JsonDeserializer();

            var raw = target.Deserialize(@"[""a"", ""b"", ""c"" ]");
            Assert.NotNull(raw);

            var list = raw as IList<object>;
            Assert.NotNull(list);
            Assert.Equal(3, list.Count);
            Assert.Equal("a", (string)list[0]);
            Assert.Equal("b", (string)list[1]);
            Assert.Equal("c", (string)list[2]);
        }

        [Fact]
        public void DeserializeSimpleObject()
        {
            var target = new JsonDeserializer();

            var raw = target.Deserialize(@"
{
    ""key1"": ""value1"",
    ""key2"": 99,
    ""key3"": true,
    ""key4"": [""str1"", ""str2"", ""str3""],
    ""key5"": {
        ""subkey1"": ""subvalue1"",
        ""subkey2"": [1, 2]
    }
}");
            Assert.NotNull(raw);

            var dict = raw as IDictionary<string, object>;
            Assert.NotNull(dict);
            Assert.Equal("value1", (string)dict["key1"]);
            Assert.Equal(99, (int)dict["key2"]);
            Assert.Equal(true, (bool)dict["key3"]);

            var list = dict["key4"] as IList<object>;
            Assert.NotNull(list);
            Assert.Equal(3, list.Count);
            Assert.Equal("str1", (string)list[0]);
            Assert.Equal("str2", (string)list[1]);
            Assert.Equal("str3", (string)list[2]);

            var jobject = dict["key5"] as IDictionary<string, object>;
            Assert.NotNull(jobject);
            Assert.Equal("subvalue1", (string)jobject["subkey1"]);

            var subArray = jobject["subkey2"] as IList<object>;
            Assert.NotNull(subArray);
            Assert.Equal(2, subArray.Count);
            Assert.Equal(1, (int)subArray[0]);
            Assert.Equal(2, (int)subArray[1]);
        }
    }
}
