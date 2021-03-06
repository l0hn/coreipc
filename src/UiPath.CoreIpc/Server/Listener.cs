﻿using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Security;
using System.Threading;
using System.Threading.Tasks;

namespace UiPath.CoreIpc
{
    public class ListenerSettings
    {
        public ListenerSettings(string name) => Name = name;
        public byte ConcurrentAccepts { get; set; } = 5;
        public byte MaxReceivedMessageSizeInMegabytes { get; set; } = 2;
        public bool EncryptAndSign { get; set; }
        public string Name { get; }
        public TimeSpan RequestTimeout { get; set; } = Timeout.InfiniteTimeSpan;
        internal IServiceProvider ServiceProvider { get; set; }
        internal IDictionary<string, EndpointSettings> Endpoints { get; set; }
    }
    abstract class Listener
    {
        protected Listener(ListenerSettings settings)
        {
            Settings = settings;
            MaxMessageSize = settings.MaxReceivedMessageSizeInMegabytes * 1024 * 1024;
            Logger = ServiceProvider.GetRequiredService<ILogger<Listener>>();
        }
        public string Name => Settings.Name;
        protected ILogger Logger { get; }
        public IServiceProvider ServiceProvider => Settings.ServiceProvider;
        public ListenerSettings Settings { get; }
        public int MaxMessageSize { get; }
        public Task ListenAsync(CancellationToken token) =>
            Task.WhenAll(Enumerable.Range(1, Settings.ConcurrentAccepts).Select(async _ =>
            {
                while (!token.IsCancellationRequested)
                {
                    await AcceptConnection(token);
                }
            }));
        protected abstract Task AcceptConnection(CancellationToken token);
        class ServerConnection : ICreateCallback
        {
            private readonly Listener _listener;
            private readonly Connection _connection;
            private readonly Server _server;
            public ServerConnection(Listener listener, Stream network, Func<ServerConnection, IClient> clientFactory, CancellationToken cancellationToken)
            {
                _listener = listener;
                var stream = Settings.EncryptAndSign ? new NegotiateStream(network) : network;
                var serializer = Settings.ServiceProvider.GetRequiredService<ISerializer>();
                _connection = new Connection(stream, serializer, Logger, _listener.Name, _listener.MaxMessageSize);
                _server = new Server(Logger, Settings, _connection, cancellationToken, new Lazy<IClient>(() => clientFactory(this)));
                Listen().LogException(Logger, _listener.Name);
                return;
                async Task Listen()
                {
                    if (Settings.EncryptAndSign)
                    {
                        await AuthenticateAsServer();
                    }
                    // close the connection when the service host closes
                    using (cancellationToken.Register(_connection.Dispose))
                    {
                        await _connection.Listen();
                    }
                    return;
                    async Task AuthenticateAsServer()
                    {
                        var negotiateStream = (NegotiateStream)_connection.Network;
                        try
                        {
                            await negotiateStream.AuthenticateAsServerAsync();
                        }
                        catch
                        {
                            _connection.Dispose();
                            throw;
                        }
                        Debug.Assert(negotiateStream.IsEncrypted && negotiateStream.IsSigned);
                    }
                }
            }
            public ILogger Logger => _listener.Logger;
            public ListenerSettings Settings => _listener.Settings;
            TCallbackContract ICreateCallback.GetCallback<TCallbackContract>(EndpointSettings endpoint)
            {
                var configuredCallbackContract = endpoint.CallbackContract;
                if (configuredCallbackContract == null || !typeof(TCallbackContract).IsAssignableFrom(configuredCallbackContract))
                {
                    throw new ArgumentException($"Callback contract mismatch. Requested {typeof(TCallbackContract)}, but it's {configuredCallbackContract?.ToString() ?? "not configured"}.");
                }
                Logger.LogInformation($"Create callback {_listener.Name}");
                var serializer = _listener.ServiceProvider.GetRequiredService<ISerializer>();
                var serviceClient = new ServiceClient<TCallbackContract>(serializer, Settings.RequestTimeout, Logger, (_, __) => Task.FromResult(_connection));
                return serviceClient.CreateProxy();
            }
        }
        protected void HandleConnection(Stream network, Func<ICreateCallback, IClient> clientFactory, CancellationToken cancellationToken) => new ServerConnection(this, network, clientFactory, cancellationToken);
    }
}