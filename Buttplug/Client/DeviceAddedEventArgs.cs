﻿// <copyright file="DeviceAddedEventArgs.cs" company="Nonpolynomial Labs LLC">
// Buttplug C# Source Code File - Visit https://buttplug.io for more info about the project.
// Copyright (c) Nonpolynomial Labs LLC. All rights reserved.
// Licensed under the BSD 3-Clause license. See LICENSE file in the project root for full license information.
// </copyright>

namespace Robust.Buttplug.Client
{
    /// <summary>
    /// Event wrapper for Buttplug DeviceAdded or DeviceRemoved messages. Used when the server
    /// informs the client of a device connecting or disconnecting.
    /// </summary>
    public class DeviceAddedEventArgs
    {
        /// <summary>
        /// The client representation of a Buttplug Device.
        /// </summary>
        public readonly ButtplugClientDevice Device;

        /// <summary>
        /// Initializes a new instance of the <see cref="DeviceAddedEventArgs"/> class.
        /// </summary>
        /// <param name="device">Device being added.</param>
        public DeviceAddedEventArgs(ButtplugClientDevice device)
        {
            Device = device;
        }
    }
}