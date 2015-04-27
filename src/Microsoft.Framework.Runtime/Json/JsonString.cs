// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;

namespace Microsoft.Framework.Runtime.Json
{
    internal class JsonString
    {
        private readonly string _content;

        private int _index;

        public JsonString(string content)
        {
            _content = content;
        }

        public Nullable<char> GetNextNonEmptyChar()
        {
            while (_content.Length > _index)
            {
                char c = _content[_index++];
                if (!Char.IsWhiteSpace(c))
                {
                    return c;
                }
            }

            return null;
        }

        public Nullable<char> MoveNext()
        {
            if (_content.Length > _index)
            {
                return _content[_index++];
            }

            return null;
        }

        public string MoveNext(int count)
        {
            if (_content.Length >= _index + count)
            {
                string result = _content.Substring(_index, count);
                _index += count;

                return result;
            }

            return null;
        }

        public void MovePrev()
        {
            if (_index > 0)
            {
                _index--;
            }
        }

        public override string ToString()
        {
            if (_content.Length > _index)
            {
                return _content.Substring(_index);
            }

            return String.Empty;
        }

        public string GetDebugString(string message)
        {
            return message + " (" + _index + "): " + _content;
        }
    }
}
