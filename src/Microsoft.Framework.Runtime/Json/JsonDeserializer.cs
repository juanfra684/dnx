// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Microsoft.Framework.Runtime.Json
{
    internal class JsonDeserializer
    {
        // maximum number of entries a Json deserialized dictionary is allowed to have
        private const int _maxJsonDeserializerMembers = Int32.MaxValue;
        private const int _maxDeserializeDepth = 100;
        private const int _maxInputLength = 2097152;

        private JsonString _input;

        public object Deserialize(string input)
        {
            if (input == null)
            {
                throw new ArgumentNullException(nameof(input));
            }

            if (input.Length > _maxInputLength)
            {
                throw new ArgumentException(JsonDeserializerResource.JSON_MaxJsonLengthExceeded, nameof(input));
            }

            _input = new JsonString(input);

            object result = DeserializeInternal(0);

            if (_input.GetNextNonEmptyChar() != null)
            {
                throw new ArgumentException(string.Format(CultureInfo.InvariantCulture, JsonDeserializerResource.JSON_IllegalPrimitive, _input.ToString()));
            }

            return result;
        }

        public JsonObject DeserializeAsObject(string input)
        {
            var dictionary = Deserialize(input) as IDictionary<string, object>;

            if (dictionary != null)
            {
                return new JsonObject(dictionary);
            }
            else
            {
                return null;
            }
        }

        public JsonObject DeserializeAsObject(Stream stream)
        {
            var reader = new StreamReader(stream);
            return DeserializeAsObject(reader.ReadToEnd());
        }

        private object DeserializeInternal(int depth)
        {
            if (++depth > _maxDeserializeDepth)
            {
                throw new ArgumentException(_input.GetDebugString(JsonDeserializerResource.JSON_DepthLimitExceeded));
            }

            var nextChar = _input.GetNextNonEmptyChar();
            if (nextChar == null)
            {
                return null;
            }

            _input.MovePrev();

            if (IsNextElementObject(nextChar))
            {
                return DeserializeDictionary(depth);
            }

            if (IsNextElementArray(nextChar))
            {
                return DeserializeList(depth);
            }

            if (IsNextElementString(nextChar))
            {
                return DeserializeString();
            }

            return DeserializePrimitiveObject();
        }

        private IList<object> DeserializeList(int depth)
        {
            var list = new List<object>();
            var c = _input.MoveNext();
            if (c != '[')
            {
                throw new ArgumentException(_input.GetDebugString(JsonDeserializerResource.JSON_InvalidArrayStart));
            }

            bool expectMore = false;
            while ((c = _input.GetNextNonEmptyChar()) != null && c != ']')
            {
                _input.MovePrev();
                object o = DeserializeInternal(depth);
                list.Add(o);

                expectMore = false;
                // we might be done here.
                c = _input.GetNextNonEmptyChar();
                if (c == ']')
                {
                    break;
                }

                expectMore = true;
                if (c != ',')
                {
                    throw new ArgumentException(_input.GetDebugString(JsonDeserializerResource.JSON_InvalidArrayExpectComma));
                }
            }
            if (expectMore)
            {
                throw new ArgumentException(_input.GetDebugString(JsonDeserializerResource.JSON_InvalidArrayExtraComma));
            }
            if (c != ']')
            {
                throw new ArgumentException(_input.GetDebugString(JsonDeserializerResource.JSON_InvalidArrayEnd));
            }
            return list;
        }

        private IDictionary<string, object> DeserializeDictionary(int depth)
        {
            IDictionary<string, object> dictionary = null;
            var c = _input.MoveNext();
            if (c != '{')
            {
                throw new ArgumentException(_input.GetDebugString(JsonDeserializerResource.JSON_ExpectedOpenBrace));
            }

            // Loop through each JSON entry in the input object
            while ((c = _input.GetNextNonEmptyChar()) != null)
            {
                _input.MovePrev();

                if (c == ':')
                {
                    throw new ArgumentException(_input.GetDebugString(JsonDeserializerResource.JSON_InvalidMemberName));
                }

                string memberName = null;
                if (c != '}')
                {
                    // Find the member name
                    memberName = DeserializeMemberName();
                    c = _input.GetNextNonEmptyChar();
                    if (c != ':')
                    {
                        throw new ArgumentException(_input.GetDebugString(JsonDeserializerResource.JSON_InvalidObject));
                    }
                }

                if (dictionary == null)
                {
                    dictionary = new Dictionary<string, object>();

                    // If the object contains nothing (i.e. {}), we're done
                    if (memberName == null)
                    {
                        // Move the cursor to the '}' character.
                        c = _input.GetNextNonEmptyChar();
                        break;
                    }
                }

                ThrowIfMaxJsonDeserializerMembersExceeded(dictionary.Count);

                // Deserialize the property value.  Here, we don't know its type
                object propVal = DeserializeInternal(depth);
                dictionary[memberName] = propVal;
                c = _input.GetNextNonEmptyChar();
                if (c == '}')
                {
                    break;
                }

                if (c != ',')
                {
                    throw new ArgumentException(_input.GetDebugString(JsonDeserializerResource.JSON_InvalidObject));
                }
            }

            if (c != '}')
            {
                throw new ArgumentException(_input.GetDebugString(JsonDeserializerResource.JSON_InvalidObject));
            }

            return dictionary;
        }

        // Deserialize a member name.
        // e.g. { MemberName: ... }
        // e.g. { 'MemberName': ... }
        // e.g. { "MemberName": ... }
        private string DeserializeMemberName()
        {
            // It could be double quoted, single quoted, or not quoted at all
            var c = _input.GetNextNonEmptyChar();
            if (c == null)
            {
                return null;
            }

            _input.MovePrev();

            // If it's quoted, treat it as a string
            if (IsNextElementString(c))
            {
                return DeserializeString();
            }

            // Non-quoted token
            return DeserializePrimitiveToken();
        }

        private object DeserializePrimitiveObject()
        {
            string input = DeserializePrimitiveToken();
            if (input.Equals("null"))
            {
                return null;
            }

            if (input.Equals("true"))
            {
                return true;
            }

            if (input.Equals("false"))
            {
                return false;
            }

            // Is it a floating point value
            bool hasDecimalPoint = input.IndexOf('.') >= 0;
            // DevDiv 56892: don't try to parse to Int32/64/Decimal if it has an exponent sign
            bool hasExponent = input.LastIndexOf("e", StringComparison.OrdinalIgnoreCase) >= 0;
            // [Last]IndexOf(char, StringComparison) overload doesn't exist, so search for "e" as a string not a char
            // Use 'Last'IndexOf since if there is an exponent it would be more quickly found starting from the end of the string
            // since 'e' is always toward the end of the number. e.g. 1.238907598768972987E82

            if (!hasExponent)
            {
                // when no exponent, could be Int32, Int64, Decimal, and may fall back to Double
                // otherwise it must be Double

                if (!hasDecimalPoint)
                {
                    // No decimal or exponent. All Int32 and Int64s fall into this category, so try them first
                    // First try int
                    int n;
                    if (Int32.TryParse(input, NumberStyles.Integer, CultureInfo.InvariantCulture, out n))
                    {
                        // NumberStyles.Integer: AllowLeadingWhite, AllowTrailingWhite, AllowLeadingSign
                        return n;
                    }

                    // Then try a long
                    long l;
                    if (Int64.TryParse(input, NumberStyles.Integer, CultureInfo.InvariantCulture, out l))
                    {
                        // NumberStyles.Integer: AllowLeadingWhite, AllowTrailingWhite, AllowLeadingSign
                        return l;
                    }
                }

                // No exponent, may or may not have a decimal (if it doesn't it couldn't be parsed into Int32/64)
                decimal dec;
                if (decimal.TryParse(input, NumberStyles.Number, CultureInfo.InvariantCulture, out dec))
                {
                    // NumberStyles.Number: AllowLeadingWhite, AllowTrailingWhite, AllowLeadingSign,
                    //                      AllowTrailingSign, AllowDecimalPoint, AllowThousands
                    return dec;
                }
            }

            // either we have an exponent or the number couldn't be parsed into any previous type. 
            Double d;
            if (Double.TryParse(input, NumberStyles.Float, CultureInfo.InvariantCulture, out d))
            {
                // NumberStyles.Float: AllowLeadingWhite, AllowTrailingWhite, AllowLeadingSign, AllowDecimalPoint, AllowExponent
                return d;
            }

            // must be an illegal primitive
            throw new ArgumentException(String.Format(CultureInfo.InvariantCulture, JsonDeserializerResource.JSON_IllegalPrimitive, input));
        }

        private string DeserializePrimitiveToken()
        {
            var sb = new StringBuilder();
            char? c = null;
            while ((c = _input.MoveNext()) != null)
            {
                if (Char.IsLetterOrDigit(c.Value) || c.Value == '.' ||
                    c.Value == '-' || c.Value == '_' || c.Value == '+')
                {

                    sb.Append(c.Value);
                }
                else
                {
                    _input.MovePrev();
                    break;
                }
            }

            return sb.ToString();
        }

        private string DeserializeString()
        {
            var sb = new StringBuilder();
            var escapedChar = false;

            var c = _input.MoveNext();

            // First determine which quote is used by the string.
            var quoteChar = CheckQuoteChar(c);
            while ((c = _input.MoveNext()) != null)
            {
                if (c == '\\')
                {
                    if (escapedChar)
                    {
                        sb.Append('\\');
                        escapedChar = false;
                    }
                    else
                    {
                        escapedChar = true;
                    }

                    continue;
                }

                if (escapedChar)
                {
                    AppendCharToBuilder(c, sb);
                    escapedChar = false;
                }
                else
                {
                    if (c == quoteChar)
                    {
                        return Utf16StringValidator.ValidateString(sb.ToString());
                    }

                    sb.Append(c.Value);
                }
            }

            throw new ArgumentException(_input.GetDebugString(JsonDeserializerResource.JSON_UnterminatedString));
        }

        private void AppendCharToBuilder(char? c, StringBuilder sb)
        {
            if (c == '"' || c == '\'' || c == '/')
            {
                sb.Append(c.Value);
            }
            else if (c == 'b')
            {
                sb.Append('\b');
            }
            else if (c == 'f')
            {
                sb.Append('\f');
            }
            else if (c == 'n')
            {
                sb.Append('\n');
            }
            else if (c == 'r')
            {
                sb.Append('\r');
            }
            else if (c == 't')
            {
                sb.Append('\t');
            }
            else if (c == 'u')
            {
                sb.Append((char)int.Parse(_input.MoveNext(4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
            }
            else
            {
                throw new ArgumentException(_input.GetDebugString(JsonDeserializerResource.JSON_BadEscape));
            }
        }

        private char CheckQuoteChar(char? c)
        {
            var quoteChar = '"';
            if (c == '\'')
            {
                quoteChar = c.Value;
            }
            else if (c != '"')
            {
                // Fail if the string is not quoted.
                throw new ArgumentException(_input.GetDebugString(JsonDeserializerResource.JSON_StringNotQuoted));
            }

            return quoteChar;
        }

        // MSRC 12038: limit the maximum number of entries that can be added to a Json deserialized dictionary,
        // as a large number of entries potentially can result in too many hash collisions that may cause DoS
        private void ThrowIfMaxJsonDeserializerMembersExceeded(int count)
        {
            if (count >= _maxJsonDeserializerMembers)
            {
                throw new InvalidOperationException(string.Format(JsonDeserializerResource.JSON_MaxJsonDeserializerMembers, _maxJsonDeserializerMembers));
            }
        }

        private static bool IsNextElementArray(char? c)
        {
            return c == '[';
        }

        private static bool IsNextElementObject(char? c)
        {
            return c == '{';
        }

        private static bool IsNextElementString(char? c)
        {
            return c == '"' || c == '\'';
        }
    }
}
