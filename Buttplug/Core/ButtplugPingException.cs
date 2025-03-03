﻿// <copyright file="ButtplugPingException.cs" company="Nonpolynomial Labs LLC">
// Buttplug C# Source Code File - Visit https://buttplug.io for more info about the project.
// Copyright (c) Nonpolynomial Labs LLC. All rights reserved.
// Licensed under the BSD 3-Clause license. See LICENSE file in the project root for full license information.
// </copyright>

using System;
using Robust.Buttplug.Core.Messages;

namespace Robust.Buttplug.Core
{
    public class ButtplugPingException : ButtplugException
    {
        /// <inheritdoc />
        public ButtplugPingException(string message, uint id = ButtplugConsts.SystemMsgId, Exception inner = null)
            : base(message, Error.ErrorClass.ERROR_PING, id, inner)
        {
        }
    }
}