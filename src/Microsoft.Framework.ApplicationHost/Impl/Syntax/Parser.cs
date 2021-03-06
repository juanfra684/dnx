// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

namespace Microsoft.Framework.ApplicationHost.Impl.Syntax
{
    internal delegate Result<TValue> Parser<TValue>(Cursor cursor);
}