// <copyright file="ButtplugClient.cs" company="Nonpolynomial Labs LLC">
// Buttplug C# Source Code File - Visit https://buttplug.io for more info about the project.
// Copyright (c) Nonpolynomial Labs LLC. All rights reserved.
// Licensed under the BSD 3-Clause license. See LICENSE file in the project root for full license information.
// </copyright>

using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Robust.Buttplug.Core;
using Robust.Buttplug.Core.Messages;

namespace Robust.Buttplug.Client
{
    public class ButtplugClient :
#if NETSTANDARD2_1_OR_GREATER
        IDisposable, IAsyncDisposable
#else
        IDisposable
#endif
    {
        /// <summary>
        /// Name of the client, used for server UI/permissions.
        /// </summary>
        ///
        public string Name { get; }

        /// <summary>
        /// Event fired on Buttplug device added, either after connect or while scanning for devices.
        /// </summary>
        public event EventHandler<DeviceAddedEventArgs> DeviceAdded;

        /// <summary>
        /// Event fired on Buttplug device removed. Can fire at any time after device connection.
        /// </summary>
        public event EventHandler<DeviceRemovedEventArgs> DeviceRemoved;

        /// <summary>
        /// Fires when an error that was not provoked by a client action is received from the server,
        /// such as a device exception, message parsing error, etc... Server may possibly disconnect
        /// after this event fires.
        /// </summary>
        public event EventHandler<ButtplugExceptionEventArgs> ErrorReceived;

        /// <summary>
        /// Event fired when the server has finished scanning for devices.
        /// </summary>
        public event EventHandler ScanningFinished;

        /// <summary>
        /// Event fired when a server ping timeout has occured.
        /// </summary>
        public event EventHandler PingTimeout;

        /// <summary>
        /// Event fired when a server disconnect has occured.
        /// </summary>
        public event EventHandler ServerDisconnect;

        /// <summary>
        /// Gets list of devices currently connected to the server.
        /// </summary>
        /// <value>
        /// A list of connected Buttplug devices.
        /// </value>
        public ButtplugClientDevice[] Devices => _devices.Values.ToArray();

        /// <summary>
        /// Gets a value indicating whether the client is connected to a server.
        /// </summary>
        /// <value>
        /// Value indicating whether the client is connected to a server.
        /// </value>
        public bool Connected => _connector?.Connected == true;

        /// <summary>
        /// Ping timer.
        /// </summary>
        /// <remarks>
        /// Sends a ping message to the server whenever the timer triggers. Usually runs at
        /// (requested ping interval / 2).
        /// </remarks>
        protected Timer _pingTimer;

        internal ButtplugClientMessageHandler _handler;

        /// <summary>
        /// Stores information about devices currently connected to the server.
        /// </summary>
        private readonly ConcurrentDictionary<uint, ButtplugClientDevice> _devices =
            new ConcurrentDictionary<uint, ButtplugClientDevice>();

        /// <summary>
        /// Connector to use for the client. Can be local (server embedded), IPC, Websocket, etc...
        /// </summary>
        private IButtplugClientConnector _connector;

        /// <summary>
        /// Initializes a new instance of the <see cref="ButtplugClient"/> class.
        /// </summary>
        /// <param name="clientName">The name of the client (used by the server for UI and permissions).</param>
        /// <param name="connector">Connector for the client.</param>
        public ButtplugClient(string clientName)
        {
            Name = clientName;
        }

        // ReSharper disable once UnusedMember.Global
        public async Task ConnectAsync(IButtplugClientConnector connector, CancellationToken token = default)
        {
            if (Connected)
            {
                throw new ButtplugHandshakeException("Client already connected to a server.");
            }
            ButtplugUtils.ArgumentNotNull(connector, nameof(connector));

            // Reset client internals
            _connector = connector;
            _connector.Disconnected += (obj, eventArgs) => ServerDisconnect?.Invoke(obj, eventArgs);
            _connector.InvalidMessageReceived += ConnectorErrorHandler;
            _connector.MessageReceived += MessageReceivedHandler;
            _devices.Clear();
            _handler = new ButtplugClientMessageHandler(connector);


            await _connector.ConnectAsync(token).ConfigureAwait(false);

            var res = await _handler.SendMessageAsync(new RequestServerInfo(Name), token).ConfigureAwait(false);
            switch (res)
            {
                case ServerInfo si:
                    if (si.MaxPingTime > 0)
                    {
                        _pingTimer?.Dispose();
                        _pingTimer = new Timer(OnPingTimer, null, 0,
                            Convert.ToInt32(Math.Round(si.MaxPingTime / 2.0, 0)));
                    }

                    if (si.MessageVersion < ButtplugConsts.CurrentSpecVersion)
                    {
                        await DisconnectAsync().ConfigureAwait(false);
                        throw new ButtplugHandshakeException(
                            $"Buttplug Server's schema version ({si.MessageVersion}) is less than the client's ({ButtplugConsts.CurrentSpecVersion}). A newer server is required.",
                            res.Id);
                    }

                    // Get full device list and populate internal list
                    var resp = await _handler.SendMessageAsync(new RequestDeviceList()).ConfigureAwait(false);
                    if (resp is DeviceList list)
                    {
                        foreach (var d in list.Devices)
                        {
                            if (_devices.ContainsKey(d.DeviceIndex))
                            {
                                continue;
                            }

                            var device = new ButtplugClientDevice(_handler, d);
                            _devices[d.DeviceIndex] = device;
                            DeviceAdded?.Invoke(this, new DeviceAddedEventArgs(device));
                        }
                    }
                    else
                    {
                        await DisconnectAsync().ConfigureAwait(false);
                        if (resp is Error errResp)
                        {
                            throw ButtplugException.FromError(errResp);
                        }

                        throw new ButtplugHandshakeException(
                            "Received unknown response to DeviceList handshake query");
                    }

                    break;

                case Error e:
                    await DisconnectAsync().ConfigureAwait(false);
                    throw ButtplugException.FromError(e);

                default:
                    await DisconnectAsync().ConfigureAwait(false);
                    throw new ButtplugHandshakeException($"Unrecognized message {res.Name} during handshake", res.Id);
            }
        }

        public async Task DisconnectAsync()
        {
            if (!Connected)
            {
                return;
            }

            _connector.MessageReceived -= MessageReceivedHandler;
            await _connector.DisconnectAsync().ConfigureAwait(false);
            ServerDisconnect?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Instructs the server to start scanning for devices. New devices will be raised as <see
        /// cref="DeviceAdded"/> events. When scanning completes, an <see cref="ScanningFinished"/>
        /// event will be triggered.
        /// </summary>
        /// <param name="token">Cancellation token, for cancelling action externally if it is not yet finished.</param>
        /// <returns>
        /// Void on success, throws <see cref="ButtplugClientException" /> otherwise.
        /// </returns>
        // ReSharper disable once UnusedMember.Global
        public async Task StartScanningAsync(CancellationToken token = default)
        {
            await _handler.SendMessageExpectOk(new StartScanning(), token).ConfigureAwait(false);
        }

        /// <summary>
        /// Instructs the server to stop scanning for devices. If scanning was in progress, a <see
        /// cref="ScanningFinished"/> event will be sent when the server has stopped scanning.
        /// </summary>
        /// <param name="token">Cancellation token, for cancelling action externally if it is not yet finished.</param>
        /// <returns>
        /// Void on success, throws <see cref="ButtplugClientException" /> otherwise.
        /// </returns>
        // ReSharper disable once UnusedMember.Global
        public async Task StopScanningAsync(CancellationToken token = default)
        {
            await _handler.SendMessageExpectOk(new StopScanning(), token).ConfigureAwait(false);
        }


        /// <summary>
        /// Instructs the server to send stop command to all connected devices.
        /// </summary>
        /// <param name="token">Cancellation token, for cancelling action externally if it is not yet finished.</param>
        /// <returns>
        /// Void on success, throws <see cref="ButtplugClientException" /> otherwise.
        /// </returns>
        // ReSharper disable once UnusedMember.Global
        public async Task StopAllDevicesAsync(CancellationToken token = default)
        {
            await _handler.SendMessageExpectOk(new StopAllDevices(), token).ConfigureAwait(false);
        }

        private void ConnectorErrorHandler(object sender, ButtplugExceptionEventArgs exception)
        {
            ErrorReceived?.Invoke(this, exception);
        }

        /// <summary>
        /// Message Received event handler. Either tries to match incoming messages as replies to
        /// messages we've sent, or fires an event related to an incoming event, like device
        /// additions/removals, log messages, etc.
        /// </summary>
        /// <param name="sender">Object sending the open event, unused.</param>
        /// <param name="args">Event parameters, including the data received.</param>
        private async void MessageReceivedHandler(object sender, MessageReceivedEventArgs args)
        {
            var msg = args.Message;

            switch (msg)
            {
                case DeviceAdded d:
                    var dev = new ButtplugClientDevice(_handler, d);
                    _devices.AddOrUpdate(d.DeviceIndex, dev, (u, device) => dev);
                    DeviceAdded?.Invoke(this, new DeviceAddedEventArgs(dev));
                    break;

                case DeviceRemoved d:
                    if (!_devices.ContainsKey(d.DeviceIndex))
                    {
                        ErrorReceived?.Invoke(this,
                            new ButtplugExceptionEventArgs(
                                new ButtplugDeviceException(
                                    "Got device removed message for unknown device.",
                                    msg.Id)));
                        return;
                    }

                    if (_devices.TryRemove(d.DeviceIndex, out var oldDev))
                    {
                        DeviceRemoved?.Invoke(this, new DeviceRemovedEventArgs(oldDev));
                    }

                    break;

                case ScanningFinished _:
                    // The scanning finished event is self explanatory and doesn't require extra arguments.
                    ScanningFinished?.Invoke(this, EventArgs.Empty);
                    break;

                case Error e:
                    // This will both log the error and fire it from our ErrorReceived event handler.
                    ErrorReceived?.Invoke(this, new ButtplugExceptionEventArgs(ButtplugException.FromError(e)));

                    if (e.ErrorCode == Error.ErrorClass.ERROR_PING)
                    {
                        PingTimeout?.Invoke(this, EventArgs.Empty);
                        await DisconnectAsync().ConfigureAwait(false);
                    }

                    break;

                default:
                    ErrorReceived?.Invoke(this,
                        new ButtplugExceptionEventArgs(
                            new ButtplugMessageException(
                                $"Got unhandled message: {msg}",
                                msg.Id)));
                    break;
            }
        }

        /// <summary>
        /// Manages the ping timer, sending pings at the rate faster than requested by the server. If
        /// the ping timer handler does not run, it means the event loop is blocked, and the server
        /// will stop all devices and disconnect.
        /// </summary>
        /// <param name="state">State of the Timer.</param>
        private async void OnPingTimer(object state)
        {
            try
            {
                await _handler.SendMessageExpectOk(new Ping()).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                ErrorReceived?.Invoke(this, new ButtplugExceptionEventArgs(new ButtplugPingException("Exception thrown during ping update", ButtplugConsts.SystemMsgId, e)));

                // If SendMessageAsync throws, we're probably already disconnected, but just make sure.
                await DisconnectAsync().ConfigureAwait(false);
            }
        }

        protected virtual void Dispose(bool disposing)
        {
            DisconnectAsync().GetAwaiter().GetResult();
        }

        /// <summary>
        /// Finalizes an instance of the <see cref="ButtplugClient"/> class, closing the connector if
        /// it is still open.
        /// </summary>
        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }

#if NETSTANDARD2_1_OR_GREATER
        protected virtual async ValueTask DisposeAsync(bool disposing)
        {
            await DisconnectAsync();
        }

        /// <summary>
        /// Finalizes an instance of the <see cref="ButtplugClient"/> class, closing the connector if
        /// it is still open.
        /// </summary>
        public async ValueTask DisposeAsync()
        {
            await DisposeAsync(disposing: true);
            GC.SuppressFinalize(this);
        }
#endif
    }
}