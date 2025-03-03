﻿// <copyright file="ButtplugUtils.cs" company="Nonpolynomial Labs LLC">
// Buttplug C# Source Code File - Visit https://buttplug.io for more info about the project.
// Copyright (c) Nonpolynomial Labs LLC. All rights reserved.
// Licensed under the BSD 3-Clause license. See LICENSE file in the project root for full license information.
// </copyright>

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using Robust.Buttplug.Core.Messages;

namespace Robust.Buttplug.Core
{
    public static class ButtplugUtils
    {
        /// <summary>
        /// Returns all ButtplugMessage deriving types in the assembly the core library is linked to.
        /// </summary>
        /// <returns></returns>
        public static IEnumerable<Type> GetAllMessageTypes()
        {
            IEnumerable<Type> allTypes;

            // Some classes in the library may not load on certain platforms due to missing symbols.
            // If this is the case, we should still find messages even though an exception was thrown.
            try
            {
                allTypes = Assembly.GetAssembly(typeof(ButtplugMessage))?.GetTypes();
            }
            catch (ReflectionTypeLoadException e)
            {
                allTypes = e.Types;
            }

            // Classes should derive from ButtplugMessage. ButtplugDeviceMessage is a special generic case.
            return (allTypes ?? throw new InvalidOperationException())
                    .Where(type => type != null &&
                                    type.IsClass &&
                                    type.IsSubclassOf(typeof(ButtplugMessage)) &&
                                    type != typeof(ButtplugDeviceMessage));
        }

        /// <summary>
        /// Ensures that the specified argument is not null.
        /// </summary>
        /// <param name="argument">The argument.</param>
        /// <param name="argumentName">Name of the argument.</param>
        /// <remarks>https://stackoverflow.com/questions/29184887/best-way-to-check-for-null-parameters-guard-clauses.</remarks>
        [DebuggerStepThrough]
        public static void ArgumentNotNull(object argument, string argumentName)
        {
            if (argument == null)
            {
                throw new ArgumentNullException(argumentName);
            }
        }

        /// <summary>
        /// Given a string, tries to return the ButtplugMessage Type (as in, C# Class Type) denoted by the string.
        /// </summary>
        /// <remarks>
        /// Added as part of the Buttplug.Core utils so we don't have to worry about Assembly resolution.
        /// </remarks>
        /// <param name="messageName">Name of the message type to find a Type for. Case-sensitive.</param>
        /// <returns>Type object of message type if it exists, otherwise null.</returns>
        public static Type GetMessageType(string messageName)
        {
            return Type.GetType($"Buttplug.Core.Messages.{messageName}");
        }
    }
}
